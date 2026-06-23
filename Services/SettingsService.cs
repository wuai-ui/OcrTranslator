namespace OcrTranslator.Services;

/// <summary>
/// 强类型配置访问层：包装 AppSettings，提供语义化属性。
/// 单例，每次读取走 AppSettings 内存缓存。
/// </summary>
public sealed class SettingsService
{
    // ── 百度 API 凭证 ────────────────────────────────────────
    public string OcrApiKey { get => AppSettings.GetString("OcrApiKey", "") ?? ""; set => AppSettings.Set("OcrApiKey", value); }
    public string OcrSecretKey { get => AppSettings.GetString("OcrSecretKey", "") ?? ""; set => AppSettings.Set("OcrSecretKey", value); }
    public string VoiceApiKey { get => AppSettings.GetString("VoiceApiKey", "") ?? ""; set => AppSettings.Set("VoiceApiKey", value); }
    public string VoiceSecretKey { get => AppSettings.GetString("VoiceSecretKey", "") ?? ""; set => AppSettings.Set("VoiceSecretKey", value); }
    public string TransAppId { get => AppSettings.GetString("TransAppId", "") ?? ""; set => AppSettings.Set("TransAppId", value); }
    public string TransSecretKey { get => AppSettings.GetString("TransSecretKey", "") ?? ""; set => AppSettings.Set("TransSecretKey", value); }

    // ── 翻译行为 ──────────────────────────────────────────────
    public bool AutoTranslate { get => AppSettings.GetBool("AutoTranslate", true); set => AppSettings.Set("AutoTranslate", value); }

    // ── 热键 ─────────────────────────────────────────────────
    public string OcrHotkey { get => AppSettings.GetString("OcrHotkey", "Shift+S") ?? "Shift+S"; set => AppSettings.Set("OcrHotkey", value); }
    public string TranslateHotkey { get => AppSettings.GetString("TranslateHotkey", "Shift+F") ?? "Shift+F"; set => AppSettings.Set("TranslateHotkey", value); }

    // ── 主题 ─────────────────────────────────────────────────
    public string Theme { get => AppSettings.GetString("Theme", "System") ?? "System"; set => AppSettings.Set("Theme", value); }

    // ── 选中状态记忆 ─────────────────────────────────────────
    public int SelectedMode { get => AppSettings.GetInt("SelectedMode", 1); set => AppSettings.Set("SelectedMode", value); }
    public int SelectedSpeechTarget { get => AppSettings.GetInt("SelectedSpeechTarget", 0); set => AppSettings.Set("SelectedSpeechTarget", value); }
    public int SelectedTtsProtocol { get => AppSettings.GetInt("SelectedTtsProtocol", 0); set => AppSettings.Set("SelectedTtsProtocol", value); }
    public int SelectedVoiceCategory { get => AppSettings.GetInt("SelectedVoiceCategory", 0); set => AppSettings.Set("SelectedVoiceCategory", value); }
    public int SelectedVoice { get => AppSettings.GetInt("SelectedVoice", 0); set => AppSettings.Set("SelectedVoice", value); }

    // ── OCR 模式 / 字体 显隐开关 ─────────────────────────────
    public bool IsModeShown(string modeName) => AppSettings.GetBool($"ShowMode_{modeName}", true);
    public void SetModeShown(string modeName, bool shown) => AppSettings.Set($"ShowMode_{modeName}", shown);
    public bool IsFontShown(string fontName) => AppSettings.GetBool($"ShowFont_{fontName}", true);
    public void SetFontShown(string fontName, bool shown) => AppSettings.Set($"ShowFont_{fontName}", shown);

    /// <summary>凭证是否已配置（用于首次启动引导）。</summary>
    public bool HasOcrCredentials => !string.IsNullOrWhiteSpace(OcrApiKey) && !string.IsNullOrWhiteSpace(OcrSecretKey);
    public bool HasTranslateCredentials => !string.IsNullOrWhiteSpace(TransAppId) && !string.IsNullOrWhiteSpace(TransSecretKey);
}
