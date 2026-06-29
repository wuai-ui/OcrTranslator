using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OcrTranslator.Abstractions;

namespace OcrTranslator.Services;

/// <summary>
/// 百度云 OAuth Token 提供者：OCR 与语音两套 Token 分别缓存，
/// SemaphoreSlim + double-check 防止并发重复鉴权。
/// 从 v3 BaiduApi 的 token 逻辑原样搬迁。
/// </summary>
public sealed class BaiduTokenProvider : IBaiduTokenProvider
{
    private readonly HttpClient _client;

    public BaiduTokenProvider(HttpClient client) => _client = client;

    private string _ocrApiKey = "";
    private string _ocrSecretKey = "";
    private string _voiceApiKey = "";
    private string _voiceSecretKey = "";

    private readonly SemaphoreSlim _ocrLock = new(1, 1);
    private string? _ocrAccessToken;
    private DateTimeOffset _ocrTokenExpireTime = DateTimeOffset.MinValue;

    private readonly SemaphoreSlim _voiceLock = new(1, 1);
    private string? _voiceAccessToken;
    private DateTimeOffset _voiceTokenExpireTime = DateTimeOffset.MinValue;

    /// <summary>密钥热更新：变化时清空已缓存 Token 引发重新鉴权。</summary>
    public void UpdateCredentials(string ocrApiKey, string ocrSecretKey, string voiceApiKey, string voiceSecretKey)
    {
        if (_ocrApiKey != ocrApiKey || _voiceApiKey != voiceApiKey)
        {
            _ocrAccessToken = null;
            _voiceAccessToken = null;
        }
        _ocrApiKey = ocrApiKey;
        _ocrSecretKey = ocrSecretKey;
        _voiceApiKey = voiceApiKey;
        _voiceSecretKey = voiceSecretKey;
    }

    public async Task<string> GetVoiceTokenAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_voiceApiKey) || string.IsNullOrWhiteSpace(_voiceSecretKey))
            throw new Exception("请先在控制中心 (设置) 中配置 语音TTS API Key");

        // 快速路径：Token 有效时直接返回，不加锁
        if (!string.IsNullOrEmpty(_voiceAccessToken) && DateTimeOffset.UtcNow < _voiceTokenExpireTime)
            return _voiceAccessToken!;

        await _voiceLock.WaitAsync(ct);
        try
        {
            // Double-check
            if (!string.IsNullOrEmpty(_voiceAccessToken) && DateTimeOffset.UtcNow < _voiceTokenExpireTime)
                return _voiceAccessToken!;

            string url = $"https://aip.baidubce.com/oauth/2.0/token?grant_type=client_credentials&client_id={_voiceApiKey}&client_secret={_voiceSecretKey}";
            var response = await _client.PostAsync(url, null, ct);
            var jsonStr = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(jsonStr);
            if (doc.RootElement.TryGetProperty("access_token", out var token))
            {
                _voiceAccessToken = token.GetString();
                _voiceTokenExpireTime = DateTimeOffset.UtcNow.AddDays(29);
                return _voiceAccessToken!;
            }
            throw new Exception("获取 Voice Token 失败，请检查控制中心里的语音密钥是否正确");
        }
        finally { _voiceLock.Release(); }
    }

    public async Task<string> GetOcrTokenAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_ocrApiKey) || string.IsNullOrWhiteSpace(_ocrSecretKey))
            throw new Exception("请先在控制中心 (设置) 中配置 OCR API Key");

        if (!string.IsNullOrEmpty(_ocrAccessToken) && DateTimeOffset.UtcNow < _ocrTokenExpireTime)
            return _ocrAccessToken!;

        await _ocrLock.WaitAsync(ct);
        try
        {
            if (!string.IsNullOrEmpty(_ocrAccessToken) && DateTimeOffset.UtcNow < _ocrTokenExpireTime)
                return _ocrAccessToken!;

            string url = $"https://aip.baidubce.com/oauth/2.0/token?grant_type=client_credentials&client_id={_ocrApiKey}&client_secret={_ocrSecretKey}";
            var response = await _client.PostAsync(url, null, ct);
            var jsonStr = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(jsonStr);
            if (doc.RootElement.TryGetProperty("access_token", out var token))
            {
                _ocrAccessToken = token.GetString();
                _ocrTokenExpireTime = DateTimeOffset.UtcNow.AddDays(29);
                return _ocrAccessToken!;
            }
            throw new Exception("获取 OCR Token 失败，请检查控制中心里的 OCR 密钥是否正确");
        }
        finally { _ocrLock.Release(); }
    }
}
