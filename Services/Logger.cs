using System;
using System.IO;
using System.Text;

namespace OcrTranslator.Services;

/// <summary>
/// 全局日志记录器。
/// 日志文件位置：%LOCALAPPDATA%\GeekOCR_v4\logs\app_YYYY-MM-DD.log（v4 独立目录，与 v3 隔离）
/// </summary>
public static class Logger
{
    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GeekOCR_v4", "logs");

    private static readonly object _lock = new();
    private static string? _currentLogFile;

    static Logger()
    {
        try
        {
            if (!Directory.Exists(LogDir))
                Directory.CreateDirectory(LogDir);

            // 启动时清理 30 天前的旧日志
            CleanupOldLogs(30);
        }
        catch { }
    }

    private static void CleanupOldLogs(int maxAgeDays)
    {
        try
        {
            var cutoff = DateTime.Now.AddDays(-maxAgeDays);
            foreach (var file in Directory.GetFiles(LogDir, "app_*.log"))
            {
                var fi = new FileInfo(file);
                if (fi.LastWriteTime < cutoff)
                {
                    fi.Delete();
                    System.Diagnostics.Debug.WriteLine($"[Logger] 已清理旧日志: {fi.Name}");
                }
            }
        }
        catch { }
    }

    private static string GetLogFile()
    {
        string today = DateTime.Now.ToString("yyyy-MM-dd");
        if (_currentLogFile == null || !_currentLogFile.Contains(today))
        {
            _currentLogFile = Path.Combine(LogDir, $"app_{today}.log");
        }
        return _currentLogFile;
    }

    public static void Log(string level, string category, string message, Exception? ex = null)
    {
        try
        {
            var sb = new StringBuilder();
            sb.Append($"[{DateTime.Now:HH:mm:ss.fff}] ");
            sb.Append($"[{level}] ");
            sb.Append($"[{category}] ");
            sb.Append(message);

            if (ex != null)
            {
                sb.AppendLine();
                sb.Append($"  Exception: {ex.GetType().Name}: {ex.Message}");
                if (ex.StackTrace != null)
                {
                    sb.AppendLine();
                    sb.Append($"  StackTrace: {ex.StackTrace}");
                }
            }

            string line = sb.ToString();
            string logFile = GetLogFile();

            lock (_lock)
            {
                File.AppendAllText(logFile, line + Environment.NewLine);
            }

            System.Diagnostics.Debug.WriteLine(line);
        }
        catch { }
    }

    public static void Info(string category, string message) => Log("INFO", category, message);
    public static void Debug(string category, string message) => Log("DEBUG", category, message);
    public static void Warn(string category, string message) => Log("WARN", category, message);
    public static void Error(string category, string message, Exception? ex = null) => Log("ERROR", category, message, ex);

    public static string GetLogDirectory() => LogDir;
}
