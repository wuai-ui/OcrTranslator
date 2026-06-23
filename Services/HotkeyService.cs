#nullable disable
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace OcrTranslator.Services
{
    /// <summary>
    /// 系统级热键管理器（v3 HotkeyManager 原样搬迁） - 参考天若OCR实现
    ///
    /// 关键设计：
    ///   1. 直接在目标窗口句柄上注册热键
    ///   2. 通过子类化窗口拦截 WM_HOTKEY 消息
    ///   3. 截图时注销热键，完成后重新注册
    /// </summary>
    public sealed class HotkeyService : IDisposable
    {
        public event Action<string> HotkeyPressed;

        private const int WM_HOTKEY = 0x0312;
        private const int GWL_WNDPROC = -4;

        private readonly Dictionary<int, string> _idToName = new();
        private readonly Dictionary<string, int> _nameToId = new(StringComparer.OrdinalIgnoreCase);
        private int _idCounter = 100; // 从100开始，避免与系统ID冲突

        private IntPtr _hWnd;
        private IntPtr _oldWndProc;
        private WndProcDelegate _wndProc;
        private bool _disposed;

        public HotkeyService(IntPtr hWnd)
        {
            _hWnd = hWnd;

            // 子类化窗口，拦截 WM_HOTKEY 消息
            _wndProc = WndProc;
            _oldWndProc = SetWindowLongPtr(hWnd, GWL_WNDPROC, Marshal.GetFunctionPointerForDelegate(_wndProc));

            Logger.Info("HotkeyService", $"已初始化, hWnd=0x{hWnd:X}, 旧WndProc=0x{_oldWndProc:X}");
        }

        private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                if (_idToName.TryGetValue(id, out string name))
                {
                    Logger.Info("HotkeyService", $"热键触发: {name} (ID={id})");
                    try { HotkeyPressed?.Invoke(name); }
                    catch (Exception ex) { Logger.Error("HotkeyService", "处理异常", ex); }
                }
                return IntPtr.Zero;
            }

            return CallWindowProc(_oldWndProc, hWnd, msg, wParam, lParam);
        }

        /// <summary>
        /// 注册热键，格式如 "Shift+S", "Ctrl+Alt+T", "F4"
        /// </summary>
        public bool RegisterHotkey(string hotkey, string name)
        {
            if (_hWnd == IntPtr.Zero) return false;

            ParseHotkey(hotkey, out uint mod, out uint vk);
            if (vk == 0) { Logger.Warn("HotkeyService", $"无效快捷键: {hotkey}"); return false; }

            int id = _idCounter++;

            if (RegisterHotKey(_hWnd, id, mod, vk))
            {
                _idToName[id] = name;
                _nameToId[hotkey] = id;
                Logger.Info("HotkeyService", $"注册成功: {hotkey} -> {name} (ID={id})");
                return true;
            }
            else
            {
                int err = Marshal.GetLastWin32Error();
                string reason = err switch
                {
                    1409 => "快捷键已被其他程序占用",
                    87 => "参数无效",
                    _ => $"错误码={err}"
                };
                Logger.Error("HotkeyService", $"注册失败: {hotkey} -> {name} | {reason}");
                return false;
            }
        }

        public bool UnregisterHotkey(string hotkey)
        {
            if (_nameToId.TryGetValue(hotkey, out int id))
            {
                if (UnregisterHotKey(_hWnd, id))
                {
                    _nameToId.Remove(hotkey);
                    _idToName.Remove(id);
                    Logger.Info("HotkeyService", $"注销成功: {hotkey} (ID={id})");
                    return true;
                }
            }
            return false;
        }

        public void ClearAll()
        {
            foreach (var kvp in _idToName) UnregisterHotKey(_hWnd, kvp.Key);
            _idToName.Clear();
            _nameToId.Clear();
            Logger.Info("HotkeyService", "已清除所有热键");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            ClearAll();

            // 恢复原WndProc
            if (_oldWndProc != IntPtr.Zero && _hWnd != IntPtr.Zero)
            {
                SetWindowLongPtr(_hWnd, GWL_WNDPROC, _oldWndProc);
                _oldWndProc = IntPtr.Zero;
            }

            Logger.Info("HotkeyService", "已释放资源");
        }

        private static void ParseHotkey(string hotkey, out uint mod, out uint vk)
        {
            mod = 0; vk = 0;
            foreach (var part in hotkey.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                switch (part.ToUpper())
                {
                    case "CTRL": case "CONTROL": mod |= 0x0002; break;
                    case "ALT": mod |= 0x0001; break;
                    case "SHIFT": mod |= 0x0004; break;
                    case "WIN": case "WINDOWS": mod |= 0x0008; break;
                    default: vk = VkCode(part); break;
                }
            }
        }

        private static uint VkCode(string key) => key.ToUpper() switch
        {
            "F1" => 0x70, "F2" => 0x71, "F3" => 0x72, "F4" => 0x73,
            "F5" => 0x74, "F6" => 0x75, "F7" => 0x76, "F8" => 0x77,
            "F9" => 0x78, "F10" => 0x79, "F11" => 0x7A, "F12" => 0x7B,
            "SPACE" => 0x20, "ENTER" => 0x0D, "ESC" => 0x1B, "TAB" => 0x09,
            "BACKSPACE" => 0x08, "DELETE" => 0x2E, "INSERT" => 0x2D,
            "HOME" => 0x24, "END" => 0x23, "PAGEUP" => 0x21, "PAGEDOWN" => 0x22,
            "LEFT" => 0x25, "UP" => 0x26, "RIGHT" => 0x27, "DOWN" => 0x28,
            "PRINTSCREEN" => 0x2C, "PAUSE" => 0x13,
            _ => key.Length == 1 ? (uint)key.ToUpper()[0] : 0
        };

        #region Win32

        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
        [DllImport("user32.dll")] static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        #endregion
    }
}
