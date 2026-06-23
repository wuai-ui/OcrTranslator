using System.Collections.Generic;

namespace OcrTranslator.Models;

/// <summary>
/// 百度 TTS 全量音色目录：4 分类共 44 个音色（含方言）。
/// 数据从 v3 MainWindow 原样搬迁。
/// </summary>
public static class VoiceCatalog
{
    private static readonly VoiceInfo[] _basic =
    {
        new("度小美 (标准女声)", 0, VoiceCategory.Basic),
        new("度小宇 (标准男声)", 1, VoiceCategory.Basic),
        new("度逍遥 (情感男声)", 3, VoiceCategory.Basic),
        new("度丫丫 (可爱女童)", 4, VoiceCategory.Basic),
    };

    private static readonly VoiceInfo[] _premium =
    {
        new("度逍遥 (精品)", 5003, VoiceCategory.Premium),
        new("度小鹿 (甜美女声)", 5118, VoiceCategory.Premium),
        new("度博文 (情感男声)", 106, VoiceCategory.Premium),
        new("度小童 (儿童)", 110, VoiceCategory.Premium),
        new("度小萌 (可爱女童)", 111, VoiceCategory.Premium),
        new("度米朵 (可爱女童)", 103, VoiceCategory.Premium),
        new("度小娇 (情感女声)", 5, VoiceCategory.Premium),
    };

    private static readonly VoiceInfo[] _superb =
    {
        new("度逍遥 (臻品)", 4003, VoiceCategory.Superb),
        new("度博文 (臻品)", 4106, VoiceCategory.Superb),
        new("度小贤", 4115, VoiceCategory.Superb),
        new("度小鹿 (臻品)", 4119, VoiceCategory.Superb),
        new("度灵儿", 4105, VoiceCategory.Superb),
        new("度小乔", 4117, VoiceCategory.Superb),
        new("度小雯", 4100, VoiceCategory.Superb),
        new("度米朵 (臻品)", 4103, VoiceCategory.Superb),
        new("度姗姗", 4144, VoiceCategory.Superb),
        new("度小贝", 4278, VoiceCategory.Superb),
        new("度清风", 4143, VoiceCategory.Superb),
        new("度小新", 4140, VoiceCategory.Superb),
        new("度小彦", 4129, VoiceCategory.Superb),
        new("度星河", 4149, VoiceCategory.Superb),
        new("度小清", 4254, VoiceCategory.Superb),
        new("度博文 (臻品2)", 4206, VoiceCategory.Superb),
        new("南方", 4226, VoiceCategory.Superb),
    };

    private static readonly VoiceInfo[] _elite =
    {
        new("度涵竹 (开朗女)", 4189, VoiceCategory.Elite),
        new("度嫣然 (活泼女)", 4194, VoiceCategory.Elite),
        new("度泽言 (温暖男)", 4193, VoiceCategory.Elite),
        new("度怀安", 4195, VoiceCategory.Elite),
        new("度清影", 4196, VoiceCategory.Elite),
        new("度沁遥", 4197, VoiceCategory.Elite),
        new("度小粤 (粤语)", 20100, VoiceCategory.Elite),
        new("度晓芸", 20101, VoiceCategory.Elite),
        new("四川小哥 (方言)", 4257, VoiceCategory.Elite),
        new("度阿闽 (闽南语)", 4132, VoiceCategory.Elite),
        new("度小蓉 (四川话)", 4139, VoiceCategory.Elite),
        new("台媒女声", 5977, VoiceCategory.Elite),
        new("度小台 (台湾腔)", 4007, VoiceCategory.Elite),
        new("度湘玉 (陕西话)", 4150, VoiceCategory.Elite),
        new("度阿锦 (粤语)", 4134, VoiceCategory.Elite),
        new("度筱林", 4172, VoiceCategory.Elite),
    };

    /// <summary>分类显示名（用于下拉框），顺序与枚举一致。</summary>
    public static readonly IReadOnlyList<string> CategoryNames = new[] { "基础", "精品", "臻品", "大模型" };

    /// <summary>取某分类下的全部音色。</summary>
    public static IReadOnlyList<VoiceInfo> OfCategory(VoiceCategory category) => category switch
    {
        VoiceCategory.Basic => _basic,
        VoiceCategory.Premium => _premium,
        VoiceCategory.Superb => _superb,
        VoiceCategory.Elite => _elite,
        _ => _elite,
    };

    /// <summary>分类枚举 ↔ 分类显示名。</summary>
    public static VoiceCategory CategoryFromIndex(int index) => index switch
    {
        0 => VoiceCategory.Basic,
        1 => VoiceCategory.Premium,
        2 => VoiceCategory.Superb,
        _ => VoiceCategory.Elite,
    };

    /// <summary>默认音色 id（度丫丫，基础音库）。</summary>
    public const int DefaultVoiceId = 4;
}
