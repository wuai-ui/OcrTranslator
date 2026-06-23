using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using OcrTranslator.Platform.Win32;

namespace OcrTranslator.Services;

/// <summary>
/// 窗口位置/大小记忆服务：按前缀持久化主窗口、设置窗口的几何信息。
/// </summary>
public sealed class WindowPlacementService
{
    /// <summary>保存窗口当前位置与大小。</summary>
    public void Save(Window window, string prefix)
    {
        AppSettings.BeginBatch();
        try
        {
            var pos = window.AppWindow.Position;
            var size = window.AppWindow.Size;
            AppSettings.Set($"{prefix}X", pos.X);
            AppSettings.Set($"{prefix}Y", pos.Y);
            AppSettings.Set($"{prefix}W", size.Width);
            AppSettings.Set($"{prefix}H", size.Height);
        }
        catch { }
        finally { AppSettings.EndBatch(); }
    }

    /// <summary>
    /// 恢复窗口位置与大小；无记录时使用默认尺寸并可居中。
    /// </summary>
    public void Restore(Window window, string prefix, int defaultW, int defaultH, bool centerIfNone = true)
    {
        int savedW = AppSettings.GetInt($"{prefix}W", 0);
        int savedH = AppSettings.GetInt($"{prefix}H", 0);

        if (savedW > 0 && savedH > 0)
        {
            window.AppWindow.Resize(new SizeInt32(savedW, savedH));
            int savedX = AppSettings.GetInt($"{prefix}X", -1);
            int savedY = AppSettings.GetInt($"{prefix}Y", -1);
            if (savedX >= 0 && savedY >= 0)
                window.AppWindow.Move(new PointInt32(savedX, savedY));
            else
                Center(window);
        }
        else
        {
            window.AppWindow.Resize(new SizeInt32(defaultW, defaultH));
            if (centerIfNone) Center(window);
        }
    }

    /// <summary>窗口居中到主显示器工作区。</summary>
    public void Center(Window window)
    {
        var displayArea = DisplayArea.GetFromPoint(new PointInt32(0, 0), DisplayAreaFallback.Primary);
        if (displayArea == null) return;
        var workArea = displayArea.WorkArea;
        var windowSize = window.AppWindow.Size;
        int x = (workArea.Width - windowSize.Width) / 2 + workArea.X;
        int y = (workArea.Height - windowSize.Height) / 2 + workArea.Y;
        window.AppWindow.Move(new PointInt32(x, y));
    }
}
