using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OcrTranslator.Abstractions;
using OcrTranslator.Models;

namespace OcrTranslator.Services;

/// <summary>
/// 百度云 OCR 引擎。多模态解析：根据 OcrMode.Category 进入不同 JSON 解析分支。
/// 通用文本走「动态均值抗漂移排版算法」。算法从 v3 BaiduApi 原样搬迁。
/// </summary>
public sealed class BaiduOcrEngine : IOcrEngine
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly IBaiduTokenProvider _tokens;

    public BaiduOcrEngine(IBaiduTokenProvider tokens) => _tokens = tokens;

    public async Task<string> RecognizeAsync(byte[] imageBytes, OcrMode mode, CancellationToken ct = default)
    {
        string token = await _tokens.GetOcrTokenAsync(ct);
        string url = $"https://aip.baidubce.com/rest/2.0/ocr/v1/{mode.ApiPath}?access_token={token}";

        var parameters = new List<KeyValuePair<string, string>>
        {
            new("image", Convert.ToBase64String(imageBytes))
        };
        if (mode.Category == OcrCategory.IdCard)
            parameters.Add(new KeyValuePair<string, string>("id_card_side", "front"));

        var content = new FormUrlEncodedContent(parameters);
        var response = await Client.PostAsync(url, content, ct);
        var jsonStr = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(jsonStr);

        if (doc.RootElement.TryGetProperty("error_code", out _) && doc.RootElement.TryGetProperty("error_msg", out var err))
            throw new Exception($"百度API拒绝: {err.GetString()}");

        // 分支 1：结构化银行卡
        if (mode.Category == OcrCategory.BankCard)
        {
            if (doc.RootElement.TryGetProperty("result", out var res))
            {
                string bankName = res.TryGetProperty("bank_name", out var bn) ? bn.GetString() ?? "未知银行" : "未知银行";
                string cardNum = res.TryGetProperty("bank_card_number", out var cn) ? cn.GetString() ?? "未识别卡号" : "未识别卡号";
                return $"[银行卡信息]\n银行: {bankName}\n卡号: {cardNum}";
            }
            return "未提取到有效银行卡数据";
        }

        // 分支 2：结构化证件/票据（words_result 是 object → 键值对输出）
        // 覆盖身份证、驾驶证、行驶证、营业执照、车牌、票据等所有 words_result object 类型
        if (!doc.RootElement.TryGetProperty("words_result", out var wordsResult))
            return "识别结果为空";

        if (wordsResult.ValueKind == JsonValueKind.Object)
        {
            var lines = new List<string>();
            foreach (var prop in wordsResult.EnumerateObject())
                if (prop.Value.TryGetProperty("words", out var wordsVal)) lines.Add($"{prop.Name}: {wordsVal.GetString()}");
            if (lines.Any()) return string.Join("\n", lines);
            return "未提取到有效数据";
        }

        // 分支 3：长文/通用排版（words_result 是 array）
        var linesData = new List<(string text, int top, int left, int height)>();
        foreach (var item in wordsResult.EnumerateArray())
        {
            if (item.TryGetProperty("location", out var loc))
                linesData.Add((item.GetProperty("words").GetString() ?? "", loc.GetProperty("top").GetInt32(), loc.GetProperty("left").GetInt32(), loc.GetProperty("height").GetInt32()));
            else
                linesData.Add((item.GetProperty("words").GetString() ?? "", 0, 0, 0));
        }
        if (linesData.Count == 0) return "";

        if (linesData.All(x => x.height == 0)) return string.Join("\n", linesData.Select(x => x.text));

        // 动态均值抗漂移排版，根据行平均高度的 60% 判定是否在同一行（v3 原版算法）
        linesData = linesData.OrderBy(x => x.top).ToList();
        var groupedLines = new List<string>();
        var currentGroup = new List<(string text, int top, int left, int height)> { linesData[0] };

        for (int i = 1; i < linesData.Count; i++)
        {
            var current = linesData[i];
            double avgTop = currentGroup.Average(x => x.top);
            double avgHeight = currentGroup.Average(x => x.height);

            if (Math.Abs(current.top - avgTop) <= (avgHeight * 0.6)) currentGroup.Add(current);
            else
            {
                currentGroup = currentGroup.OrderBy(x => x.left).ToList();
                groupedLines.Add(string.Join(" ", currentGroup.Select(x => x.text)));
                currentGroup = new List<(string text, int top, int left, int height)> { current };
            }
        }
        if (currentGroup.Any())
        {
            currentGroup = currentGroup.OrderBy(x => x.left).ToList();
            groupedLines.Add(string.Join(" ", currentGroup.Select(x => x.text)));
        }
        return string.Join("\n", groupedLines);
    }

}
