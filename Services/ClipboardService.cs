using System;
using System.Runtime.InteropServices;
using System.Threading;
using OcrTranslator.Platform.Win32;

namespace OcrTranslator.Services;

/// <summary>
/// 剪贴板读写 + 键盘模拟服务：支持快捷翻译的「Ctrl+C 读取 → 翻译 → Ctrl+V 原地替换」。
/// 从 v3 MainWindow 的剪贴板逻辑提取。
/// </summary>
public sealed class ClipboardService
{
    public string? GetText()
    {
        if (!NativeMethods.IsClipboardFormatAvailable(NativeMethods.CF_UNICODETEXT)) return null;
        if (!NativeMethods.OpenClipboard(IntPtr.Zero)) return null;
        try
        {
            IntPtr hData = NativeMethods.GetClipboardData(NativeMethods.CF_UNICODETEXT);
            if (hData == IntPtr.Zero) return null;
            IntPtr pData = NativeMethods.GlobalLock(hData);
            if (pData == IntPtr.Zero) return null;
            try { return Marshal.PtrToStringUni(pData); }
            finally { NativeMethods.GlobalUnlock(hData); }
        }
        finally { NativeMethods.CloseClipboard(); }
    }

    public void SetText(string text)
    {
        if (!NativeMethods.OpenClipboard(IntPtr.Zero)) return;
        try
        {
            NativeMethods.EmptyClipboard();
            int bytes = (text.Length + 1) * 2;
            IntPtr hGlobal = NativeMethods.GlobalAlloc(NativeMethods.GMEM_MOVEABLE, (UIntPtr)bytes);
            if (hGlobal == IntPtr.Zero) return;
            IntPtr pGlobal = NativeMethods.GlobalLock(hGlobal);
            if (pGlobal == IntPtr.Zero) return;
            try
            {
                Marshal.Copy(text.ToCharArray(), 0, pGlobal, text.Length);
                Marshal.WriteInt16(pGlobal + text.Length * 2, 0);
            }
            finally { NativeMethods.GlobalUnlock(hGlobal); }
            NativeMethods.SetClipboardData(NativeMethods.CF_UNICODETEXT, hGlobal);
        }
        finally { NativeMethods.CloseClipboard(); }
    }

    public void SendCopy() => SendKeyCombo(NativeMethods.VK_CONTROL, NativeMethods.VK_C);
    public void SendPaste() => SendKeyCombo(NativeMethods.VK_CONTROL, NativeMethods.VK_V);

    /// <summary>模拟修饰键+普通键的组合按键（先释放所有修饰键避免粘连）。</summary>
    public static void SendKeyCombo(int modifierVk, int keyVk)
    {
        // 先释放可能按住的修饰键
        NativeMethods.keybd_event(NativeMethods.VK_SHIFT, 0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
        NativeMethods.keybd_event(NativeMethods.VK_CONTROL, 0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
        NativeMethods.keybd_event(NativeMethods.VK_ALT, 0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
        Thread.Sleep(30);

        NativeMethods.keybd_event((byte)modifierVk, 0, 0, UIntPtr.Zero);
        NativeMethods.keybd_event((byte)keyVk, 0, 0, UIntPtr.Zero);
        NativeMethods.keybd_event((byte)keyVk, 0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
        NativeMethods.keybd_event((byte)modifierVk, 0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
    }
}
