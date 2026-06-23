using System;
using System.Diagnostics;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OcrTranslator.Abstractions;
using OcrTranslator.Services;

namespace OcrTranslator.ViewModels;

/// <summary>
/// 主窗口视图模型：承载可测试的翻译业务与 UI 状态。
/// 平台互操作（截图/热键/剪贴板/TTS 播放）留在 View code-behind，
/// 通过更新本 VM 的属性驱动 UI（x:Bind 双向绑定）。
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly ITranslator _translator;
    private readonly SettingsService _settings;

    public MainViewModel(ITranslator translator, SettingsService settings)
    {
        _translator = translator;
        _settings = settings;
    }

    /// <summary>OCR 原文（可编辑，TwoWay 绑定）。</summary>
    [ObservableProperty] private string _originalText = string.Empty;

    /// <summary>翻译译文（只读显示）。</summary>
    [ObservableProperty] private string _translatedText = string.Empty;

    /// <summary>OCR 耗时标签。</summary>
    [ObservableProperty] private string _ocrElapsed = string.Empty;

    /// <summary>翻译耗时标签。</summary>
    [ObservableProperty] private string _translateElapsed = string.Empty;

    /// <summary>是否正在朗读。</summary>
    [ObservableProperty] private bool _isSpeaking;

    /// <summary>朗读按钮文字（朗读 / 停止）。</summary>
    [ObservableProperty] private string _speakLabel = "朗读";

    /// <summary>手动翻译（翻译按钮命令）。</summary>
    [RelayCommand]
    private async Task TranslateAsync()
    {
        await TranslateCoreAsync(OriginalText);
    }

    /// <summary>翻译指定文本（供 OCR 自动翻译复用）。</summary>
    public async Task TranslateTextAsync(string text)
    {
        await TranslateCoreAsync(text);
    }

    private async Task TranslateCoreAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.StartsWith("(")) return;
        TranslatedText = "翻译中...";
        TranslateElapsed = string.Empty;
        try
        {
            string toLang = LanguageDetector.TranslateTarget(text);
            var sw = Stopwatch.StartNew();
            string trans = await _translator.TranslateAsync(text, toLang);
            sw.Stop();
            TranslatedText = string.IsNullOrEmpty(trans) ? "(无翻译结果)" : trans;
            TranslateElapsed = $"[翻译耗时: {sw.ElapsedMilliseconds} ms]";
        }
        catch (Exception ex)
        {
            TranslatedText = $"翻译失败：{ex.Message}";
            TranslateElapsed = string.Empty;
        }
    }
}
