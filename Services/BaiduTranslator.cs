using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OcrTranslator.Abstractions;

namespace OcrTranslator.Services;

/// <summary>
/// 百度翻译引擎。
/// 保留 v3 的两项优化：
///   1. 智能语言方向：from=auto，按目标语言分段
///   2. 混合语言分段翻译：文本按中/英切段，只翻译非目标语言部分
/// 翻译密钥（AppId/SecretKey）实时从 SettingsService 读取，设置页修改后立即生效。
/// </summary>
public sealed class BaiduTranslator : ITranslator
{
    private readonly HttpClient _client;
    private readonly SettingsService _settings;

    public BaiduTranslator(SettingsService settings, HttpClient client)
    {
        _settings = settings;
        _client = client;
    }

    public async Task<string> TranslateAsync(string text, string toLang, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";

        // 混合语言分割翻译：将文本按中/英文分段，只翻译非目标语言的部分（各段并行，减少总耗时）
        var segments = SplitByLanguage(text, toLang);
        if (segments.Count > 1)
        {
            var tasks = segments.Select(seg => seg.isTargetLang
                ? Task.FromResult(seg.text)
                : TranslateRawAsync(seg.text, "auto", toLang, ct)).ToArray();
            var results = await Task.WhenAll(tasks);
            return string.Concat(results);
        }

        return await TranslateRawAsync(text, "auto", toLang, ct);
    }

    /// <summary>按语言分段：将文本拆成（文本, 是否已是目标语言）的列表。</summary>
    private static List<(string text, bool isTargetLang)> SplitByLanguage(string text, string toLang)
    {
        var result = new List<(string text, bool isTargetLang)>();
        bool isChineseTarget = toLang == "zh";
        int segStart = 0;
        int currentType = 0; // 0=unknown, 1=chinese, 2=english

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            int charType;
            if (c >= 0x4e00 && c <= 0x9fa5) charType = 1;
            else if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')) charType = 2;
            else charType = currentType;

            if (currentType == 0) currentType = charType;

            if (charType != currentType && charType != 0)
            {
                string seg = text.Substring(segStart, i - segStart);
                if (!string.IsNullOrWhiteSpace(seg))
                    result.Add((seg, currentType == 1 == isChineseTarget));
                segStart = i;
                currentType = charType;
            }
        }

        if (segStart < text.Length)
        {
            string seg = text.Substring(segStart);
            if (!string.IsNullOrWhiteSpace(seg))
                result.Add((seg, currentType == 1 == isChineseTarget));
        }

        return result;
    }

    private async Task<string> TranslateRawAsync(string text, string fromLang, string toLang, CancellationToken ct)
    {
        string appId = _settings.TransAppId;
        string secretKey = _settings.TransSecretKey;
        if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(secretKey))
            throw new Exception("请先在控制中心 (设置) 中配置 翻译 APP ID 和密钥");

        string salt = Random.Shared.Next(32768, 65536).ToString();
        string signStr = appId + text + salt + secretKey;
        string sign = GetMd5Hash(signStr);
        string url = $"https://fanyi-api.baidu.com/api/trans/vip/translate?q={Uri.EscapeDataString(text)}&from={fromLang}&to={toLang}&appid={appId}&salt={salt}&sign={sign}";

        var response = await _client.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        var jsonStr = await response.Content.ReadAsStringAsync(ct);

        using var doc = JsonDocument.Parse(jsonStr);
        if (doc.RootElement.TryGetProperty("trans_result", out var transResult))
        {
            var dsts = transResult.EnumerateArray().Select(x => x.GetProperty("dst").GetString());
            return string.Join("\n", dsts);
        }
        if (doc.RootElement.TryGetProperty("error_msg", out var errorMsg))
            throw new Exception($"翻译失败: {errorMsg.GetString()}");

        return "";
    }

    private static string GetMd5Hash(string input)
    {
        using var md5 = MD5.Create();
        var bytes = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
        var sb = new StringBuilder();
        foreach (var b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }
}
