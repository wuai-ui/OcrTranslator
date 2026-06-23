using System.Text.RegularExpressions;

namespace OcrTranslator.Services;

/// <summary>
/// 语言方向判定：消除 v3 里重复 3 次的中英文字符统计逻辑。
/// </summary>
public static class LanguageDetector
{
    private static readonly Regex ChineseChars = new(@"[一-龥]", RegexOptions.Compiled);
    private static readonly Regex EnglishChars = new(@"[a-zA-Z]", RegexOptions.Compiled);

    /// <summary>统计中文字符数。</summary>
    public static int CountChinese(string text) => ChineseChars.Matches(text).Count;

    /// <summary>统计英文字母数。</summary>
    public static int CountEnglish(string text) => EnglishChars.Matches(text).Count;

    /// <summary>
    /// 翻译目标语言：英文多于中文则译为 zh，否则译为 en。
    /// </summary>
    public static string TranslateTarget(string text)
    {
        int cn = CountChinese(text);
        int en = CountEnglish(text);
        return en >= cn ? "zh" : "en";
    }

    /// <summary>
    /// TTS 朗读语言：仅当完全没有中文且英文占优时用 en，否则 zh。
    /// </summary>
    public static string SpeechLanguage(string text)
    {
        int cn = CountChinese(text);
        int en = CountEnglish(text);
        return (en >= cn && cn == 0) ? "en" : "zh";
    }
}
