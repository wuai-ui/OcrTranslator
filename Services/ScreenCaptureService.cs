using System;
using System.IO;
using System.Runtime.InteropServices;
using OcrTranslator.Platform.Win32;

namespace OcrTranslator.Services;

/// <summary>
/// 屏幕截图服务：截取鼠标所在显示器的物理像素（Win32 BitBlt，避免 System.Drawing 的 DPI 自动缩放）。
/// 从 v3 MainWindow.StartOCR 的截图逻辑提取。
/// </summary>
public sealed class ScreenCaptureService
{
    /// <summary>截取鼠标所在显示器，返回 PNG 字节与物理坐标区域。</summary>
    public CapturedScreen CaptureAtCursor()
    {
        NativeMethods.GetCursorPos(out var pt);
        IntPtr hMonitor = NativeMethods.MonitorFromPoint(pt, 2);
        var mi = new NativeMethods.MONITORINFO
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.MONITORINFO>()
        };
        NativeMethods.GetMonitorInfo(hMonitor, ref mi);

        int left = mi.rcMonitor.left;
        int top = mi.rcMonitor.top;
        int width = mi.rcMonitor.right - mi.rcMonitor.left;
        int height = mi.rcMonitor.bottom - mi.rcMonitor.top;

        byte[] screenBytes;
        IntPtr hScreen = NativeMethods.GetDC(IntPtr.Zero);
        try
        {
            IntPtr hMemDC = NativeMethods.CreateCompatibleDC(hScreen);
            IntPtr hBitmap = NativeMethods.CreateCompatibleBitmap(hScreen, width, height);
            IntPtr hOldBmp = NativeMethods.SelectObject(hMemDC, hBitmap);
            NativeMethods.BitBlt(hMemDC, 0, 0, width, height, hScreen, left, top, NativeMethods.SRCCOPY);
            NativeMethods.SelectObject(hMemDC, hOldBmp);

            using (var bmp = System.Drawing.Image.FromHbitmap(hBitmap))
            using (var ms = new MemoryStream())
            {
                bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                screenBytes = ms.ToArray();
                Logger.Info("ScreenCapture", $"截图完成: {bmp.Width}x{bmp.Height}, {screenBytes.Length} bytes");
            }

            NativeMethods.DeleteObject(hBitmap);
            NativeMethods.DeleteDC(hMemDC);
        }
        finally
        {
            NativeMethods.ReleaseDC(IntPtr.Zero, hScreen);
        }

        return new CapturedScreen(screenBytes, left, top, width, height);
    }
}

/// <summary>一次屏幕截图的结果。</summary>
public sealed record CapturedScreen(byte[] PngBytes, int Left, int Top, int Width, int Height);
