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

}

/// <summary>
/// 一种 OCR 识别模式：显示名 ↔ 百度 API path ↔ 结果类别 ↔ 是否显示位置坐标。
/// </summary>
public sealed record OcrMode(string DisplayName, string ApiPath, OcrCategory Category, string Quota = "")
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
        new OcrMode("通用-标准",    "general_basic",      OcrCategory.General,  "50000次/天"),
        new OcrMode("通用-含位置",  "general",            OcrCategory.General,  "500次/天"),
        new OcrMode("高精-标准",    "accurate_basic",     OcrCategory.General,  "500次/天"),
        new OcrMode("高精-含位置",  "accurate",           OcrCategory.General,  "50次/天"),
        new OcrMode("网络图",       "webimage",           OcrCategory.General,  "500次/天"),
        new OcrMode("手写识别",     "handwriting",        OcrCategory.General,  "50次/天"),
        new OcrMode("数字识别",     "numbers",            OcrCategory.General,  "200次/天"),
        new OcrMode("身份证",       "idcard",             OcrCategory.IdCard,   "500次/天"),
        new OcrMode("银行卡",       "bankcard",           OcrCategory.BankCard, "500次/天"),
        new OcrMode("驾驶证识别",   "driving_license",    OcrCategory.General,  "200次/天"),
        new OcrMode("行驶证识别",   "vehicle_license",    OcrCategory.General,  "200次/天"),
        new OcrMode("营业执照识别", "business_license",   OcrCategory.General,  "200次/天"),
        new OcrMode("车牌识别",     "license_plate",      OcrCategory.General,  "200次/天"),
        new OcrMode("通用票据识别", "receipt",            OcrCategory.General,  "200次/天"),
        new OcrMode("增值税发票识别","vat_invoice",       OcrCategory.General,  "500次/天"),
        new OcrMode("火车票识别",   "train_ticket",       OcrCategory.General,  "50次/天"),
        new OcrMode("出租车票识别", "taxi_ticket",        OcrCategory.General,  "50次/天"),
    };

    /// <summary>按显示名查找；找不到时回退到「通用-标准」。</summary>
    public static OcrMode ByDisplayName(string? name)
    {
        foreach (var m in All)
            if (m.DisplayName == name) return m;
        return All[1];
    }
}
