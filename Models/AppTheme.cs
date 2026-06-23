namespace OcrTranslator.Models;

/// <summary>
/// 应用主题三态：亮色 / 暗色 / 跟随系统。
/// </summary>
public enum AppTheme
{
    Light,
    Dark,
    System,
}

/// <summary>
/// 主题枚举与持久化字符串之间的转换。
/// </summary>
public static class AppThemeExtensions
{
    public static AppTheme Parse(string? value)
    {
        return (value?.Trim().ToLowerInvariant()) switch
        {
            "light" => AppTheme.Light,
            "dark" => AppTheme.Dark,
            _ => AppTheme.System,
        };
    }

    public static string ToPersistedString(this AppTheme theme) => theme switch
    {
        AppTheme.Light => "Light",
        AppTheme.Dark => "Dark",
        _ => "System",
    };
}
