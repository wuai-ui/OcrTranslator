using System.Collections.Generic;
using Microsoft.UI.Xaml;
using OcrTranslator.Models;

namespace OcrTranslator.Services;

/// <summary>
/// 三态主题服务：在根 FrameworkElement 上设置 RequestedTheme 实现运行时即时切换
/// （Light / Dark / System），持久化到 settings。支持多窗口同步（主窗口 + 设置窗口）。
/// </summary>
public sealed class ThemeService
{
    private readonly SettingsService _settings;
    private readonly List<FrameworkElement> _roots = new();
    public AppTheme Current { get; private set; }

    public ThemeService(SettingsService settings)
    {
        _settings = settings;
        Current = AppThemeExtensions.Parse(_settings.Theme);
    }

    /// <summary>绑定一个根元素（通常是 Window.Content），多窗口各自注册以同步主题。</summary>
    public void ApplyTo(FrameworkElement root)
    {
        if (!_roots.Contains(root)) _roots.Add(root);
        root.RequestedTheme = ToElementTheme(Current);
    }

    /// <summary>切换主题并持久化，同步到所有已注册根元素。</summary>
    public void Apply(AppTheme theme)
    {
        Current = theme;
        _settings.Theme = theme.ToPersistedString();
        var elementTheme = ToElementTheme(theme);
        foreach (var root in _roots)
            root.RequestedTheme = elementTheme;
    }

    private static ElementTheme ToElementTheme(AppTheme theme) => theme switch
    {
        AppTheme.Light => ElementTheme.Light,
        AppTheme.Dark => ElementTheme.Dark,
        _ => ElementTheme.Default,
    };
}
