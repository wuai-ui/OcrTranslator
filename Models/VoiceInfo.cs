namespace OcrTranslator.Models;

/// <summary>
/// 百度 TTS 音色分类（基础 / 精品 / 臻品 / 大模型）。
/// </summary>
public enum VoiceCategory
{
    Basic,   // 基础
    Premium, // 精品
    Superb,  // 臻品
    Elite,   // 大模型
}

/// <summary>
/// 一个 TTS 音色：显示名 ↔ 百度 per 参数 ↔ 分类。
/// </summary>
public sealed record VoiceInfo(string Name, int Id, VoiceCategory Category);

/// <summary>
/// 朗读目标：原文 / 译文。
/// </summary>
public enum SpeechTarget
{
    Original,
    Translated,
}
