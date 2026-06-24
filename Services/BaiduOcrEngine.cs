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
        // 百度表格识别用 v2 高精度表格 API；其他用 v1 通用 API
        string url = mode.Category == OcrCategory.Table
            ? $"https://aip.baidubce.com/rest/2.0/ocr/v2/accurate_table?access_token={token}"
            : $"https://aip.baidubce.com/rest/2.0/ocr/v1/{mode.ApiPath}?access_token={token}";

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

        // 分支 0：表格识别（百度 accurate_table v2 API，按行列结构化输出）
        if (mode.Category == OcrCategory.Table)
        {
            if (!doc.RootElement.TryGetProperty("tables_result", out var tablesResult))
                return "未识别到表格";

            var sb = new System.Text.StringBuilder();
            foreach (var table in tablesResult.EnumerateArray())
            {
                int rows = table.TryGetProperty("rows", out var rEl) ? rEl.GetInt32() : 0;
                int cols = table.TryGetProperty("columns", out var cEl) ? cEl.GetInt32() : 0;
                if (rows == 0 || cols == 0) continue;

                // 收集单元格（兼容 header/body 分开格式）
                var cells = new List<(int row, int col, string text)>();
                if (table.TryGetProperty("cellInfos", out var cellInfos))
                {
                    foreach (var cell in cellInfos.EnumerateArray())
                        cells.Add(ParseCell(cell, rows, cols));
                }
                else
                {
                    if (table.TryGetProperty("header", out var header) && header.TryGetProperty("cellInfos", out var hCells))
                        foreach (var cell in hCells.EnumerateArray()) cells.Add(ParseCell(cell, rows, cols));
                    if (table.TryGetProperty("body", out var body) && body.TryGetProperty("cellInfos", out var bCells))
                        foreach (var cell in bCells.EnumerateArray()) cells.Add(ParseCell(cell, rows, cols));
                }

                // 构建二维数组
                var grid = new string[rows, cols];
                foreach (var (row, col, text) in cells)
                    if (row >= 0 && row < rows && col >= 0 && col < cols)
                        grid[row, col] = text;

                // 每行用制表符分隔列，保持表格结构
                for (int r = 0; r < rows; r++)
                {
                    var parts = new List<string>();
                    for (int c = 0; c < cols; c++)
                        parts.Add(grid[r, c] ?? "");
                    sb.AppendLine(string.Join("\t", parts));
                }
            }
            return sb.ToString().TrimEnd();
        }

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

        // 分支 2：结构化身份证
        if (mode.Category == OcrCategory.IdCard)
        {
            if (doc.RootElement.TryGetProperty("words_result", out var wr))
            {
                var idInfo = new List<string> { "[身份证人像面信息]" };
                foreach (var prop in wr.EnumerateObject())
                    if (prop.Value.TryGetProperty("words", out var wordsVal)) idInfo.Add($"{prop.Name}: {wordsVal.GetString()}");
                return string.Join("\n", idInfo);
            }
            return "未提取到有效身份证数据";
        }

        // 分支 3：长文/通用排版
        if (!doc.RootElement.TryGetProperty("words_result", out var wordsResult)) return "识别结果为空";

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

        // 改进排版：全局中位数高度 + 1.5 倍阈值聚类
        // 解决表格同行多列被拆行的问题（阈值从 0.6→1.5，基准从组内均值→全局中位数）
        linesData = linesData.OrderBy(x => x.top).ToList();

        // 全局中位数高度（比组内均值更稳定，不受组内文字大小差异影响）
        var heights = linesData.Where(x => x.height > 0).Select(x => x.height).OrderBy(x => x).ToList();
        int medianHeight = heights.Count > 0 ? heights[heights.Count / 2] : 20;
        double clusterThreshold = medianHeight * 1.5; // 1.5 倍，覆盖表格列间距内的 top 差异

        var groupedLines = new List<string>();
        var currentGroup = new List<(string text, int top, int left, int height)> { linesData[0] };

        for (int i = 1; i < linesData.Count; i++)
        {
            var current = linesData[i];
            double groupAvgTop = currentGroup.Average(x => x.top);

            if (Math.Abs(current.top - groupAvgTop) <= clusterThreshold)
                currentGroup.Add(current);
            else
            {
                groupedLines.Add(string.Join(" ", currentGroup.OrderBy(x => x.left).Select(x => x.text)));
                currentGroup = new List<(string text, int top, int left, int height)> { current };
            }
        }
        if (currentGroup.Any())
            groupedLines.Add(string.Join(" ", currentGroup.OrderBy(x => x.left).Select(x => x.text)));
        return string.Join("\n", groupedLines);
    }

    /// <summary>解析百度表格 API 的单元格 JSON（兼容 contents / words 字段名）。</summary>
    private static (int row, int col, string text) ParseCell(System.Text.Json.JsonElement cell, int maxRows, int maxCols)
    {
        int row = cell.TryGetProperty("row", out var r) ? r.GetInt32() : -1;
        int col = cell.TryGetProperty("col", out var c) ? c.GetInt32() : -1;
        string text = cell.TryGetProperty("contents", out var t) ? (t.GetString() ?? "").Trim()
            : cell.TryGetProperty("words", out var w) ? (w.GetString() ?? "").Trim()
            : "";
        return (row, col, text);
    }
}
