using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace OcrTranslator.Services;

/// <summary>
/// 基于 JSON 文件的本地配置存储，替代 ApplicationData.Current.LocalSettings
/// 兼容 WindowsPackageType=None（未打包模式）。
/// 配置路径：%LOCALAPPDATA%\GeekOCR_v4\settings.json（v4 独立，与 v3 隔离）
/// </summary>
public static class AppSettings
{
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GeekOCR_v4");

    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

    private static Dictionary<string, JsonElement>? _cache;
    private static readonly object _lock = new();
    private static int _batchDepth; // 批量更新嵌套计数：>0 时只改内存不写盘，归零时一次性落盘

    private static Dictionary<string, JsonElement> Load()
    {
        if (_cache != null) return _cache;

        lock (_lock)
        {
            if (_cache != null) return _cache;

            _cache = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    var doc = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
                    if (doc != null)
                    {
                        foreach (var kvp in doc)
                            _cache[kvp.Key] = kvp.Value;
                    }
                }
            }
            catch
            {
                // 配置文件损坏则忽略，使用默认值
            }
            return _cache;
        }
    }

    private static void Save()
    {
        if (_batchDepth > 0) return; // 批量模式下延迟写盘
        try
        {
            if (!Directory.Exists(SettingsDir))
                Directory.CreateDirectory(SettingsDir);

            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(_cache, options);
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // 写入失败时静默忽略
        }
    }

    public static string? GetString(string key, string? defaultValue = null)
    {
        var dict = Load();
        if (dict.TryGetValue(key, out var element))
        {
            if (element.ValueKind == JsonValueKind.String)
                return element.GetString();
            return element.ToString();
        }
        return defaultValue;
    }

    public static bool GetBool(string key, bool defaultValue = true)
    {
        var dict = Load();
        if (dict.TryGetValue(key, out var element))
        {
            if (element.ValueKind == JsonValueKind.True) return true;
            if (element.ValueKind == JsonValueKind.False) return false;
        }
        return defaultValue;
    }

    public static int GetInt(string key, int defaultValue = 0)
    {
        var dict = Load();
        if (dict.TryGetValue(key, out var element))
        {
            if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out int val))
                return val;
        }
        return defaultValue;
    }

    public static void Set(string key, string? value)
    {
        var dict = Load();
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(value));
        dict[key] = doc.RootElement.Clone();
        Save();
    }

    public static void Set(string key, bool value)
    {
        var dict = Load();
        using var doc = JsonDocument.Parse(value ? "true" : "false");
        dict[key] = doc.RootElement.Clone();
        Save();
    }

    public static void Set(string key, int value)
    {
        var dict = Load();
        using var doc = JsonDocument.Parse(value.ToString());
        dict[key] = doc.RootElement.Clone();
        Save();
    }

    public static bool ContainsKey(string key)
    {
        return Load().ContainsKey(key);
    }

    /// <summary>开启批量更新：期间所有 Set 只改内存不写盘，避免大量 Set 频繁 IO（如保存字体列表）。</summary>
    public static void BeginBatch() { _batchDepth++; }

    /// <summary>结束批量模式（支持嵌套，最外层结束时一次性写盘）。</summary>
    public static void EndBatch() { if (_batchDepth > 0 && --_batchDepth == 0) Save(); }
}
