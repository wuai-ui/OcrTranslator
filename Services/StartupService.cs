using System;
using System.Diagnostics;
using Microsoft.Win32;
using OcrTranslator.Platform.Win32;

namespace OcrTranslator.Services;

/// <summary>
/// 开机自启管理：写入 / 移除 HKCU 注册表 Run 键。
/// 键名使用 GeekOCR_v4，与 v3 隔离，两者可分别自启。
/// </summary>
public sealed class StartupService
{
    private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "GeekOCR_v4";

    public bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
            return key?.GetValue(AppName) != null;
        }
        catch { return false; }
    }

    public void SetEnabled(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
            if (key == null) return;

            if (enable)
            {
                string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (!string.IsNullOrEmpty(exePath))
                {
                    key.SetValue(AppName, $"\"{exePath}\"");
                    Logger.Info("StartupService", $"已设置开机自启: {exePath}");
                }
            }
            else
            {
                key.DeleteValue(AppName, false);
                Logger.Info("StartupService", "已移除开机自启");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("StartupService", "设置开机自启失败", ex);
        }
    }
}
