using System.Collections.Generic;

namespace OcrTranslator.Models;

/// <summary>
/// OCR 识别结果的结构类型，决定 JSON 解析分支。
/// </summary>
public enum OcrCategory
{
    /// <summary>通用文本：走聚类排版算法</summary>
    General,

    /// <summary>身份证：按 words_result 属性结构化输出</summary>
    IdCard,

    /// <summary>银行卡：按 result 字段结构化输出</summary>
    BankCard,

    /// <summary>表格识别：调百度表格 API v2，按 row/col 结构化输出（制表符分隔列）</summary>
    Table,
}

/// <summary>
/// 一种 OCR 识别模式：显示名 ↔ 百度 API path ↔ 结果类别。
/// </summary>
public sealed record OcrMode(string DisplayName, string ApiPath, OcrCategory Category)
{
    public bool IsStructured => Category != OcrCategory.General;
}

/// <summary>
/// 全部 8 种 OCR 模式的静态目录。
/// </summary>
public static class OcrModes
{
    public static readonly IReadOnlyList<OcrMode> All = new[]
    {
        new OcrMode("通用-含位置", "general", OcrCategory.General),
        new OcrMode("通用-标准", "general_basic", OcrCategory.General),
        new OcrMode("高精-含位置", "accurate", OcrCategory.General),
        new OcrMode("高精-标准", "accurate_basic", OcrCategory.General),
        new OcrMode("手写识别", "handwriting", OcrCategory.General),
        new OcrMode("表格识别", "table", OcrCategory.Table),
        new OcrMode("身份证", "idcard", OcrCategory.IdCard),
        new OcrMode("银行卡", "bankcard", OcrCategory.BankCard),
        new OcrMode("网络图", "webimage", OcrCategory.General),
    };

    /// <summary>按显示名查找；找不到时回退到「通用-标准」。</summary>
    public static OcrMode ByDisplayName(string? name)
    {
        foreach (var m in All)
            if (m.DisplayName == name) return m;
        return All[1];
    }
}
