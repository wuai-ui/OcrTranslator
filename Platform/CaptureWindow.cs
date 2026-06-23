#nullable disable
using System;
using System.IO;
using System.Runtime.InteropServices;
using OcrTranslator.Services;

namespace OcrTranslator.Platform
{
    /// <summary>
    /// 截图框选窗口 - 纯 Win32 分层窗口实现（v4 原样保留，仅迁移命名空间）
    ///
    /// 架构设计：
    ///   - WS_EX_LAYERED 分层窗口 + UpdateLayeredWindow(ULW_OPAQUE) 零闪烁
    ///   - WH_MOUSE_LL 全局鼠标钩子捕获框选
    ///   - WH_KEYBOARD_LL 全局键盘钩子捕获 ESC/Enter
    ///   - 全程物理像素坐标，无 DPI 转换
    ///   - 原始截图副本 + 压暗副本，选区内部恢复原图
    ///
    /// 功能清单：
    ///   1. 鼠标指针立即变为十字准星
    ///   2. 选区外部压暗（AlphaBlend）
    ///   3. 选区边框蓝色实线（2px）
    ///   4. 8个控制点可调整大小
    ///   5. 选区内部可拖动移动
    ///   6. 不同位置显示不同光标
    ///   7. 实时显示选区尺寸和位置
    ///   8. 框选完成自动确认
    ///   9. ESC 取消
    /// </summary>
    public sealed class CaptureWindow : IDisposable
    {
        public event Action<byte[]> ImageCaptured;
        public event Action Cancelled;

        // 屏幕参数
        private readonly byte[] _screenBytes;
        private readonly int _screenLeft, _screenTop, _screenW, _screenH;

        // 位图资源
        private IntPtr _hWnd;
        private IntPtr _hBitmap;      // 当前显示
        private IntPtr _hBitmapOrig;  // 原始截图
        private IntPtr _hBitmapDark;  // 压暗截图
        private bool _disposed;

        // 选区状态
        private bool _isDragging, _isMoving, _isResizing;
        private int _resizeHandle;
        private int _startX, _startY, _curX, _curY;
        private int _selX, _selY, _selW, _selH;
        private bool _hasSelection;

        // 常量
        private const int HANDLE_SIZE = 8;
        private const int MIN_SIZE = 10;
        private const uint ULW_OPAQUE = 4;
        private const int DARK_ALPHA = 100;

        // 钩子
        private IntPtr _mouseHook, _keyboardHook;
        private N.LowLevelMouseProc _mouseProc;
        private N.LowLevelKeyboardProc _keyProc;
        private N.WndProcDelegate _wndProc;

        // 预加载光标
        private IntPtr _curCross, _curArrow, _curNWSE, _curNESW, _curNS, _curWE, _curMove;

        public CaptureWindow(byte[] screenBytes, int left, int top, int w, int h)
        {
            _screenBytes = screenBytes;
            _screenLeft = left; _screenTop = top;
            _screenW = w; _screenH = h;

            _curCross = N.LoadCursor(IntPtr.Zero, 32515);
            _curArrow = N.LoadCursor(IntPtr.Zero, 32512);
            _curNWSE = N.LoadCursor(IntPtr.Zero, 32642);
            _curNESW = N.LoadCursor(IntPtr.Zero, 32643);
            _curNS = N.LoadCursor(IntPtr.Zero, 32645);
            _curWE = N.LoadCursor(IntPtr.Zero, 32644);
            _curMove = N.LoadCursor(IntPtr.Zero, 32646);
        }

        public void Show()
        {
            _hBitmap = PngToHBitmap(_screenBytes, _screenW, _screenH);
            _hBitmapOrig = DupBitmap(_hBitmap, _screenW, _screenH);
            _hBitmapDark = MakeDarkBitmap(_hBitmap, _screenW, _screenH, DARK_ALPHA);

            N.SetCursor(_curCross);
            CreateWindow();
            _mouseProc = MouseProc;
            _mouseHook = N.SetWindowsHookEx(14, _mouseProc, N.GetModuleHandle(null), 0);
            _keyProc = KeyProc;
            _keyboardHook = N.SetWindowsHookEx(13, _keyProc, N.GetModuleHandle(null), 0);
            Logger.Info("CaptureWindow", $"截图窗口已显示: ({_screenLeft},{_screenTop}) {_screenW}x{_screenH}");
        }

        private void CreateWindow()
        {
            string cls = "OcrCap_" + Guid.NewGuid().ToString("N")[..8];
            _wndProc = WndProc;
            var wc = new N.WNDCLASSEX { cbSize = Marshal.SizeOf<N.WNDCLASSEX>(), lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc), hInstance = N.GetModuleHandle(null), lpszClassName = cls, hCursor = _curCross };
            N.RegisterClassEx(ref wc);
            _hWnd = N.CreateWindowEx(0x00080008, cls, "", 0x80000000, _screenLeft, _screenTop, _screenW, _screenH, IntPtr.Zero, IntPtr.Zero, wc.hInstance, IntPtr.Zero);
            UpdateLayered();
            N.ShowWindow(_hWnd, 5);
            N.SetForegroundWindow(_hWnd);
        }

        private void UpdateLayered()
        {
            IntPtr hScreen = N.GetDC(IntPtr.Zero);
            IntPtr hMemDC = N.CreateCompatibleDC(hScreen);
            IntPtr hOld = N.SelectObject(hMemDC, _hBitmap);
            var dst = new N.POINT { x = _screenLeft, y = _screenTop };
            var sz = new N.SIZE { cx = _screenW, cy = _screenH };
            var src = new N.POINT { x = 0, y = 0 };
            var blend = new N.BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 0 };
            N.UpdateLayeredWindow(_hWnd, hScreen, ref dst, ref sz, hMemDC, ref src, 0, ref blend, ULW_OPAQUE);
            N.SelectObject(hMemDC, hOld);
            N.DeleteDC(hMemDC);
            N.ReleaseDC(IntPtr.Zero, hScreen);
        }

        private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == 0x0010) { N.DestroyWindow(hWnd); return IntPtr.Zero; }
            return N.DefWindowProc(hWnd, msg, wParam, lParam);
        }

        // ── 鼠标钩子 ──────────────────────────────────────────

        private IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                var pt = Marshal.PtrToStructure<N.MSLLHOOKSTRUCT>(lParam);
                int lx = pt.pt.x - _screenLeft, ly = pt.pt.y - _screenTop;
                switch ((int)wParam)
                {
                    case 0x0201: OnMouseDown(lx, ly); break;
                    case 0x0200: OnMouseMove(lx, ly); break;
                    case 0x0202: OnMouseUp(lx, ly); break;
                }
            }
            return N.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        }

        private void OnMouseDown(int x, int y)
        {
            if (_hasSelection)
            {
                int h = HitHandle(x, y);
                if (h >= 0) { _isResizing = true; _resizeHandle = h; _startX = x; _startY = y; return; }
                if (InSel(x, y)) { _isMoving = true; _startX = x; _startY = y; return; }
                _hasSelection = false;
            }
            _isDragging = true;
            _startX = _curX = x; _startY = _curY = y;
            Draw();
        }

        private void OnMouseMove(int x, int y)
        {
            if (_isDragging)
            {
                N.SetCursor(_curCross);
                _curX = x; _curY = y;
                _selX = Math.Min(_startX, _curX); _selY = Math.Min(_startY, _curY);
                _selW = Math.Abs(_curX - _startX); _selH = Math.Abs(_curY - _startY);
                Draw();
            }
            else if (_isMoving)
            {
                N.SetCursor(_curMove);
                _selX = Math.Max(0, Math.Min(_selX + x - _startX, _screenW - _selW));
                _selY = Math.Max(0, Math.Min(_selY + y - _startY, _screenH - _selH));
                _startX = x; _startY = y;
                Draw();
            }
            else if (_isResizing)
            {
                Resize(x, y); _startX = x; _startY = y; Draw();
            }
            else if (_hasSelection)
            {
                int h = HitHandle(x, y);
                if (h == 0 || h == 4) N.SetCursor(_curNWSE);
                else if (h == 2 || h == 6) N.SetCursor(_curNESW);
                else if (h == 1 || h == 5) N.SetCursor(_curNS);
                else if (h == 3 || h == 7) N.SetCursor(_curWE);
                else if (InSel(x, y)) N.SetCursor(_curMove);
                else N.SetCursor(_curCross);
            }
            else N.SetCursor(_curCross);
        }

        private void OnMouseUp(int x, int y)
        {
            if (_isDragging)
            {
                _isDragging = false;
                _curX = x; _curY = y;
                _selX = Math.Min(_startX, _curX); _selY = Math.Min(_startY, _curY);
                _selW = Math.Abs(_curX - _startX); _selH = Math.Abs(_curY - _startY);
                if (_selW > MIN_SIZE && _selH > MIN_SIZE) { _hasSelection = true; Draw(); Confirm(); return; }
                Draw();
            }
            else if (_isMoving) _isMoving = false;
            else if (_isResizing) _isResizing = false;
        }

        private bool InSel(int x, int y) => x >= _selX && x <= _selX + _selW && y >= _selY && y <= _selY + _selH;

        private int HitHandle(int x, int y)
        {
            if (!_hasSelection) return -1;
            int[] hx = { _selX, _selX + _selW / 2, _selX + _selW, _selX + _selW, _selX + _selW, _selX + _selW / 2, _selX, _selX };
            int[] hy = { _selY, _selY, _selY, _selY + _selH / 2, _selY + _selH, _selY + _selH, _selY + _selH, _selY + _selH / 2 };
            for (int i = 0; i < 8; i++) if (Math.Abs(x - hx[i]) <= HANDLE_SIZE && Math.Abs(y - hy[i]) <= HANDLE_SIZE) return i;
            return -1;
        }

        private void Resize(int x, int y)
        {
            switch (_resizeHandle)
            {
                case 0: _selW += _selX - x; _selH += _selY - y; _selX = x; _selY = y; break;
                case 1: _selH += _selY - y; _selY = y; break;
                case 2: _selW = x - _selX; _selH += _selY - y; _selY = y; break;
                case 3: _selW = x - _selX; break;
                case 4: _selW = x - _selX; _selH = y - _selY; break;
                case 5: _selH = y - _selY; break;
                case 6: _selW += _selX - x; _selX = x; _selH = y - _selY; break;
                case 7: _selW += _selX - x; _selX = x; break;
            }
            if (_selW < MIN_SIZE) _selW = MIN_SIZE;
            if (_selH < MIN_SIZE) _selH = MIN_SIZE;
        }

        // ── 键盘钩子 ──────────────────────────────────────────

        private IntPtr KeyProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (int)wParam == 0x0100)
            {
                int vk = Marshal.ReadInt32(lParam);
                if (vk == 0x1B) { Close(); return (IntPtr)1; }
            }
            return N.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
        }

        // ── 绘制 ──────────────────────────────────────────────

        private void Draw()
        {
            IntPtr hScreen = N.GetDC(IntPtr.Zero);
            IntPtr hBase = (_hasSelection || _isDragging) ? _hBitmapDark : _hBitmapOrig;

            // 恢复基础位图
            IntPtr hDC1 = N.CreateCompatibleDC(hScreen);
            IntPtr hOld1 = N.SelectObject(hDC1, hBase);
            IntPtr hDC2 = N.CreateCompatibleDC(hScreen);
            IntPtr hOld2 = N.SelectObject(hDC2, _hBitmap);
            N.BitBlt(hDC2, 0, 0, _screenW, _screenH, hDC1, 0, 0, 0x00CC0020);
            N.SelectObject(hDC1, hOld1); N.DeleteDC(hDC1);

            // 绘制选区
            if ((_hasSelection || _isDragging) && _selW > 0 && _selH > 0)
            {
                // 恢复选区内部为原图
                IntPtr hDC3 = N.CreateCompatibleDC(hScreen);
                IntPtr hOld3 = N.SelectObject(hDC3, _hBitmapOrig);
                N.BitBlt(hDC2, _selX, _selY, _selW, _selH, hDC3, _selX, _selY, 0x00CC0020);
                N.SelectObject(hDC3, hOld3); N.DeleteDC(hDC3);

                // 蓝色边框
                IntPtr hPen = N.CreatePen(0, 2, 0x00FF7700);
                IntPtr hOldPen = N.SelectObject(hDC2, hPen);
                IntPtr hNullBrush = N.GetStockObject(5);
                IntPtr hOldBrush = N.SelectObject(hDC2, hNullBrush);
                N.Rectangle(hDC2, _selX, _selY, _selX + _selW, _selY + _selH);
                N.SelectObject(hDC2, hOldPen); N.SelectObject(hDC2, hOldBrush);
                N.DeleteObject(hPen);

                // 8个控制点
                if (_hasSelection)
                {
                    IntPtr hWP = N.CreatePen(0, 1, 0x00FFFFFF);
                    IntPtr hWB = N.CreateSolidBrush(0x00FFFFFF);
                    IntPtr hOP = N.SelectObject(hDC2, hWP);
                    IntPtr hOB = N.SelectObject(hDC2, hWB);
                    int[] hx = { _selX, _selX + _selW / 2, _selX + _selW, _selX + _selW, _selX + _selW, _selX + _selW / 2, _selX, _selX };
                    int[] hy = { _selY, _selY, _selY, _selY + _selH / 2, _selY + _selH, _selY + _selH, _selY + _selH, _selY + _selH / 2 };
                    for (int i = 0; i < 8; i++) N.Rectangle(hDC2, hx[i] - 4, hy[i] - 4, hx[i] + 4, hy[i] + 4);
                    N.SelectObject(hDC2, hOP); N.SelectObject(hDC2, hOB);
                    N.DeleteObject(hWP); N.DeleteObject(hWB);
                }

                // 尺寸文本
                DrawInfo(hDC2);
            }

            N.SelectObject(hDC2, hOld2); N.DeleteDC(hDC2);
            N.ReleaseDC(IntPtr.Zero, hScreen);
            UpdateLayered();
        }

        private void DrawInfo(IntPtr hdc)
        {
            string sz = $"{_selW} × {_selH}";
            string ps = $"({_selX}, {_selY})";
            IntPtr hFont = N.CreateFont(14, 0, 0, 0, 400, 0, 0, 0, 0, 0, 0, 0, 0, "Segoe UI");
            IntPtr hOld = N.SelectObject(hdc, hFont);
            N.SetTextColor(hdc, 0x00FFFFFF);
            N.SetBkMode(hdc, 1);
            N.SetBkColor(hdc, 0x00000000);
            int tx = _selX + _selW + 5, ty = _selY + _selH + 5;
            if (tx + 100 > _screenW) tx = _selX - 100;
            if (ty + 40 > _screenH) ty = _selY - 40;
            N.TextOut(hdc, tx, ty, sz, sz.Length);
            N.TextOut(hdc, tx, ty + 16, ps, ps.Length);
            N.SelectObject(hdc, hOld); N.DeleteObject(hFont);
        }

        // ── 确认 & 裁剪 ──────────────────────────────────────

        private void Confirm()
        {
            if (!_hasSelection || _selW <= 0 || _selH <= 0) return;
            int px = Math.Max(0, Math.Min(_selX, _screenW - 1));
            int py = Math.Max(0, Math.Min(_selY, _screenH - 1));
            int pw = Math.Min(_selW, _screenW - px);
            int ph = Math.Min(_selH, _screenH - py);
            if (pw <= 0 || ph <= 0) { Close(); return; }

            Logger.Info("CaptureWindow", $"裁剪: ({px},{py}) {pw}x{ph}");

            byte[] cropped;
            using (var ms = new MemoryStream(_screenBytes))
            using (var bmp = new System.Drawing.Bitmap(ms))
            using (var part = bmp.Clone(new System.Drawing.Rectangle(px, py, pw, ph), bmp.PixelFormat))
            using (var outMs = new MemoryStream())
            {
                part.Save(outMs, System.Drawing.Imaging.ImageFormat.Png);
                cropped = outMs.ToArray();
            }

            Logger.Info("CaptureWindow", $"裁剪完成: {cropped.Length} bytes");

            // 先解除钩子
            if (_mouseHook != IntPtr.Zero) { N.UnhookWindowsHookEx(_mouseHook); _mouseHook = IntPtr.Zero; }
            if (_keyboardHook != IntPtr.Zero) { N.UnhookWindowsHookEx(_keyboardHook); _keyboardHook = IntPtr.Zero; }
            // PostMessage延迟销毁（不能在钩子线程直接DestroyWindow）
            N.PostMessage(_hWnd, 0x0010, IntPtr.Zero, IntPtr.Zero);
            ImageCaptured?.Invoke(cropped);
        }

        // ── 工具方法 ──────────────────────────────────────────

        private void Close()
        {
            if (_mouseHook != IntPtr.Zero) { N.UnhookWindowsHookEx(_mouseHook); _mouseHook = IntPtr.Zero; }
            if (_keyboardHook != IntPtr.Zero) { N.UnhookWindowsHookEx(_keyboardHook); _keyboardHook = IntPtr.Zero; }
            if (_hWnd != IntPtr.Zero) { N.PostMessage(_hWnd, 0x0010, IntPtr.Zero, IntPtr.Zero); _hWnd = IntPtr.Zero; }
            Cancelled?.Invoke();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Close();
            if (_hBitmap != IntPtr.Zero) { N.DeleteObject(_hBitmap); _hBitmap = IntPtr.Zero; }
            if (_hBitmapOrig != IntPtr.Zero) { N.DeleteObject(_hBitmapOrig); _hBitmapOrig = IntPtr.Zero; }
            if (_hBitmapDark != IntPtr.Zero) { N.DeleteObject(_hBitmapDark); _hBitmapDark = IntPtr.Zero; }
        }

        private static IntPtr PngToHBitmap(byte[] png, int w, int h)
        {
            using var ms = new MemoryStream(png);
            using var bmp = new System.Drawing.Bitmap(ms);
            using var bmp32 = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
            using var g = System.Drawing.Graphics.FromImage(bmp32);
            g.DrawImage(bmp, 0, 0, w, h);
            return bmp32.GetHbitmap();
        }

        private static IntPtr DupBitmap(IntPtr src, int w, int h)
        {
            IntPtr hS = N.GetDC(IntPtr.Zero);
            IntPtr d1 = N.CreateCompatibleDC(hS); IntPtr o1 = N.SelectObject(d1, src);
            IntPtr dst = N.CreateCompatibleBitmap(hS, w, h);
            IntPtr d2 = N.CreateCompatibleDC(hS); IntPtr o2 = N.SelectObject(d2, dst);
            N.BitBlt(d2, 0, 0, w, h, d1, 0, 0, 0x00CC0020);
            N.SelectObject(d1, o1); N.SelectObject(d2, o2);
            N.DeleteDC(d1); N.DeleteDC(d2); N.ReleaseDC(IntPtr.Zero, hS);
            return dst;
        }

        private static IntPtr MakeDarkBitmap(IntPtr src, int w, int h, byte alpha)
        {
            IntPtr hS = N.GetDC(IntPtr.Zero);
            IntPtr dark = DupBitmap(src, w, h);
            IntPtr black = N.CreateCompatibleBitmap(hS, w, h);
            IntPtr d1 = N.CreateCompatibleDC(hS); IntPtr o1 = N.SelectObject(d1, black);
            N.PatBlt(d1, 0, 0, w, h, 0x00000042);
            IntPtr d2 = N.CreateCompatibleDC(hS); IntPtr o2 = N.SelectObject(d2, dark);
            var blend = new N.BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = alpha, AlphaFormat = 0 };
            N.AlphaBlend(d2, 0, 0, w, h, d1, 0, 0, w, h, blend);
            N.SelectObject(d1, o1); N.SelectObject(d2, o2);
            N.DeleteDC(d1); N.DeleteDC(d2); N.DeleteObject(black);
            N.ReleaseDC(IntPtr.Zero, hS);
            return dark;
        }

        // ── Win32 API ─────────────────────────────────────────

        private static class N
        {
            public delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
            public delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);
            public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

            [StructLayout(LayoutKind.Sequential)] public struct POINT { public int x, y; }
            [StructLayout(LayoutKind.Sequential)] public struct SIZE { public int cx, cy; }
            [StructLayout(LayoutKind.Sequential)] public struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
            [StructLayout(LayoutKind.Sequential)] public struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData, flags, time; public IntPtr dwExtraInfo; }
            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            public struct WNDCLASSEX { public int cbSize; public uint style; public IntPtr lpfnWndProc; public int cbClsExtra, cbWndExtra; public IntPtr hInstance, hIcon, hCursor, hbrBackground; public string lpszMenuName, lpszClassName; public IntPtr hIconSm; }

            [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern ushort RegisterClassEx(ref WNDCLASSEX wc);
            [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr CreateWindowEx(uint ex, string cls, string wnd, uint style, int x, int y, int w, int h, IntPtr par, IntPtr menu, IntPtr inst, IntPtr param);
            [DllImport("user32.dll")] public static extern bool DestroyWindow(IntPtr h);
            [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
            [DllImport("user32.dll")] public static extern IntPtr DefWindowProc(IntPtr h, uint m, IntPtr w, IntPtr l);
            [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int n);
            [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
            [DllImport("user32.dll")] public static extern bool UpdateLayeredWindow(IntPtr h, IntPtr hdcD, ref POINT pD, ref SIZE sz, IntPtr hdcS, ref POINT pS, uint cr, ref BLENDFUNCTION bl, uint fl);
            [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr h);
            [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr h, IntPtr dc);
            [DllImport("user32.dll")] public static extern IntPtr SetWindowsHookEx(int id, LowLevelMouseProc p, IntPtr m, uint t);
            [DllImport("user32.dll")] public static extern IntPtr SetWindowsHookEx(int id, LowLevelKeyboardProc p, IntPtr m, uint t);
            [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr h);
            [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr h, int n, IntPtr w, IntPtr l);
            [DllImport("user32.dll")] public static extern IntPtr LoadCursor(IntPtr h, uint id);
            [DllImport("user32.dll")] public static extern IntPtr SetCursor(IntPtr h);
            [DllImport("gdi32.dll")] public static extern int TextOut(IntPtr dc, int x, int y, string s, int c);
            [DllImport("gdi32.dll")] public static extern uint SetTextColor(IntPtr dc, uint c);
            [DllImport("gdi32.dll")] public static extern int SetBkMode(IntPtr dc, int m);
            [DllImport("gdi32.dll")] public static extern uint SetBkColor(IntPtr dc, uint c);
            [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr dc);
            [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr dc);
            [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
            [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
            [DllImport("gdi32.dll")] public static extern bool BitBlt(IntPtr d, int dx, int dy, int w, int h, IntPtr s, int sx, int sy, uint op);
            [DllImport("gdi32.dll")] public static extern bool PatBlt(IntPtr dc, int x, int y, int w, int h, uint op);
            [DllImport("msimg32.dll")] public static extern bool AlphaBlend(IntPtr d, int dx, int dy, int dw, int dh, IntPtr s, int sx, int sy, int sw, int sh, BLENDFUNCTION bl);
            [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
            [DllImport("gdi32.dll")] public static extern IntPtr CreatePen(int style, int w, uint c);
            [DllImport("gdi32.dll")] public static extern IntPtr CreateSolidBrush(uint c);
            [DllImport("gdi32.dll")] public static extern bool Rectangle(IntPtr dc, int l, int t, int r, int b);
            [DllImport("gdi32.dll")] public static extern IntPtr GetStockObject(int id);
            [DllImport("gdi32.dll")] public static extern IntPtr CreateFont(int h, int w, int esc, int ori, int wt, uint it, uint ul, uint st, uint cs, uint op, uint cl, uint q, uint pf, string face);
            [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string name);
        }
    }
}
