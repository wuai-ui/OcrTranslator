using System.Threading;
using System.Threading.Tasks;
using OcrTranslator.Models;

namespace OcrTranslator.Abstractions;

/// <summary>
/// 语音合成（TTS）引擎抽象。当前实现为百度语音（HTTP + WebSocket 双协议）。
/// </summary>
public interface ISpeechSynthesizer
{
    /// <summary>
    /// 合成语音。
    /// </summary>
    /// <param name="text">待朗读文本（单段，建议 ≤ 300 字）。</param>
    /// <param name="voice">音色。</param>
    /// <param name="lan">语言代码（zh / en）。</param>
    /// <param name="useWebSocket">是否走 WebSocket 协议（大模型音色更稳）。</param>
    Task<byte[]> SynthesizeAsync(string text, VoiceInfo voice, string lan, bool useWebSocket, CancellationToken ct = default);
}
