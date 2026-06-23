using System.Threading;
using System.Threading.Tasks;

namespace OcrTranslator.Abstractions;

/// <summary>
/// 百度云 OAuth Token 提供者：分别缓存 OCR 与语音两套 Token，
/// 双检锁防止并发重复鉴权。密钥变化时调用 UpdateCredentials 失效缓存。
/// </summary>
public interface IBaiduTokenProvider
{
    /// <summary>获取 OCR access_token（带缓存）。</summary>
    Task<string> GetOcrTokenAsync(CancellationToken ct = default);

    /// <summary>获取语音 TTS access_token（带缓存）。</summary>
    Task<string> GetVoiceTokenAsync(CancellationToken ct = default);

    /// <summary>密钥热更新：设置页改 Key 后调用，失效已缓存 Token。</summary>
    void UpdateCredentials(string ocrApiKey, string ocrSecretKey, string voiceApiKey, string voiceSecretKey);
}
