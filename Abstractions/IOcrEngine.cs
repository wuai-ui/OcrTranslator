using System.Threading;
using System.Threading.Tasks;
using OcrTranslator.Models;

namespace OcrTranslator.Abstractions;

/// <summary>
/// OCR 识别引擎抽象。当前实现为百度云 OCR，未来可替换为 PaddleOCR 本地引擎等。
/// </summary>
public interface IOcrEngine
{
    /// <summary>
    /// 识别图片中的文字。
    /// </summary>
    /// <param name="imageBytes">PNG 格式的图片字节。</param>
    /// <param name="mode">OCR 模式（决定 API path 与结果解析分支）。</param>
    Task<string> RecognizeAsync(byte[] imageBytes, OcrMode mode, CancellationToken ct = default);
}
