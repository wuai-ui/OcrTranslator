using System.Threading;
using System.Threading.Tasks;

namespace OcrTranslator.Abstractions;

/// <summary>
/// 文本翻译引擎抽象。当前实现为百度翻译，未来可替换为 DeepL / Google 等。
/// </summary>
public interface ITranslator
{
    /// <summary>
    /// 翻译文本。实现内部应保留「智能语言方向 + 混合语言分段」优化。
    /// </summary>
    /// <param name="text">待翻译文本。</param>
    /// <param name="toLang">目标语言代码（如 zh / en）。</param>
    Task<string> TranslateAsync(string text, string toLang, CancellationToken ct = default);
}
