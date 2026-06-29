#nullable disable
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Extensions.DependencyInjection;
using System.Linq;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Windows.Graphics;
using OcrTranslator.Models;
using OcrTranslator.Services;

namespace OcrTranslator.Views
{
    public sealed partial class SettingsWindow : Window
    {
        private readonly SettingsService _settings;
        private readonly StartupService _startup;
        private readonly ThemeService _theme;

        // 缓存系统字体列表（避免每次打开设置页都调用系统 API）
#nullable enable
        private static List<string>? _cachedFontNames;
#nullable disable
        public static List<string> GetCachedFontNames()
        {
            if (_cachedFontNames == null)
            {
                _cachedFontNames = new List<string>();
                foreach (var f in System.Drawing.FontFamily.Families)
                    if (!f.Name.StartsWith("@")) _cachedFontNames.Add(f.Name);
            }
            return _cachedFontNames;
        }

        // 快捷键录制状态
        private bool _isRecording = false;
        private string _recordingTarget = ""; // "Ocr" 或 "Translate"
        private string _currentOcrHotkey;
        private string _currentTranslateHotkey;

        private const string DEFAULT_OCR_HOTKEY = "Shift+S";
        private const string DEFAULT_TRANSLATE_HOTKEY = "Shift+F";

        // Win32 键盘钩子用于录制
        private IntPtr _hookId = IntPtr.Zero;
        private Platform.Win32.NativeMethods.LowLevelKeyboardProc _hookProc;

        private bool _shiftPressed = false;
        private bool _ctrlPressed = false;
        private bool _altPressed = false;

        // 录制超时保护（10秒自动停止）
        private DispatcherTimer _recordingTimeoutTimer;

        // 主题加载中标志（避免初始化触发 SelectionChanged）
        private bool _isLoading = false;

        public SettingsWindow()
        {
            _settings = App.Services.GetService<SettingsService>();
            _startup = App.Services.GetService<StartupService>();
            _theme = App.Services.GetService<ThemeService>();

            InitializeComponent();

            // Mica 背景 + 主题同步
            SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
            if (this.Content is FrameworkElement root) _theme.ApplyTo(root);

            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
            UpdateCaptionColors();

            if (AppSettings.ContainsKey("SettingsWinW"))
            {
                this.AppWindow.MoveAndResize(new RectInt32(
                    AppSettings.GetInt("SettingsWinX", 0), AppSettings.GetInt("SettingsWinY", 0),
                    AppSettings.GetInt("SettingsWinW", 850), AppSettings.GetInt("SettingsWinH", 650)));
            }
            else { this.AppWindow.Resize(new SizeInt32(900, 700)); }

            this.Closed += (s, e) => {
                AppSettings.BeginBatch();
                AppSettings.Set("SettingsWinX", this.AppWindow.Position.X);
                AppSettings.Set("SettingsWinY", this.AppWindow.Position.Y);
                AppSettings.Set("SettingsWinW", this.AppWindow.Size.Width);
                AppSettings.Set("SettingsWinH", this.AppWindow.Size.Height);
                AppSettings.EndBatch();

                if (_isRecording)
                {
                    _isRecording = false;
                    _recordingTimeoutTimer?.Stop();
                    _recordingTimeoutTimer = null;
                    UnhookKeyboard();
                }
                else
                {
                    UnhookKeyboard();
                }
            };

            LoadSettings();
        }

        private void SettingsNav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            SystemPanel.Visibility = Visibility.Collapsed;
            ApiPanel.Visibility = Visibility.Collapsed;
            HotkeyPanel.Visibility = Visibility.Collapsed;
            OcrModePanel.Visibility = Visibility.Collapsed;
            FontPanel.Visibility = Visibility.Collapsed;
            AboutPanel.Visibility = Visibility.Collapsed;

            string tag = ((NavigationViewItem)args.SelectedItem).Tag.ToString();
            if (tag == "SystemTab") SystemPanel.Visibility = Visibility.Visible;
            else if (tag == "ApiTab") ApiPanel.Visibility = Visibility.Visible;
            else if (tag == "HotkeyTab") HotkeyPanel.Visibility = Visibility.Visible;
            else if (tag == "OcrModeTab") OcrModePanel.Visibility = Visibility.Visible;
            else if (tag == "FontTab") FontPanel.Visibility = Visibility.Visible;
            else if (tag == "AboutTab") AboutPanel.Visibility = Visibility.Visible;
        }

        private void LoadSettings()
        {
            OcrApiKeyBox.Text = _settings.OcrApiKey;
            OcrSecretKeyBox.Text = _settings.OcrSecretKey;
            VoiceApiKeyBox.Text = _settings.VoiceApiKey;
            VoiceSecretKeyBox.Text = _settings.VoiceSecretKey;
            TransAppIdBox.Text = _settings.TransAppId;
            TransSecretKeyBox.Text = _settings.TransSecretKey;

            AutoTranslateSwitch.IsOn = _settings.AutoTranslate;

            // 主题单选
            _isLoading = true;
            ThemeRadio.SelectedIndex = (int)_theme.Current;
            _isLoading = false;

            // OCR 模式列表（表格形式：分类/模式/API/调用量/Toggle）
            PopulateOcrModeTable(OcrModesContainer);

            // 默认 OCR 模式下拉框
            DefaultOcrModeComboBox.Items.Clear();
            foreach (var mode in OcrModes.All)
                DefaultOcrModeComboBox.Items.Add(new ComboBoxItem { Content = mode.DisplayName });
            string currentDefaultMode = _settings.DefaultOcrMode;
            int defaultModeIdx = 0;
            for (int i = 0; i < DefaultOcrModeComboBox.Items.Count; i++)
            {
                if ((DefaultOcrModeComboBox.Items[i] as ComboBoxItem)?.Content?.ToString() == currentDefaultMode)
                { defaultModeIdx = i; break; }
            }
            if (DefaultOcrModeComboBox.Items.Count > 0) DefaultOcrModeComboBox.SelectedIndex = defaultModeIdx;

            // 字体列表（表格形式）
            PopulateFontTable(FontsContainer);

            // 快捷键
            _currentOcrHotkey = _settings.OcrHotkey;
            _currentTranslateHotkey = _settings.TranslateHotkey;
            OcrHotkeyBtn.Content = _currentOcrHotkey;
            TranslateHotkeyBtn.Content = _currentTranslateHotkey;

            // 开机自启状态
            StartupSwitch.IsOn = _startup.IsEnabled();
        }

        private void PopulateMultiColumnToggles(StackPanel container, IList<string> items, string kind, int columns, IDictionary<string, string> quotas = null)
        {
            for (int i = 0; i < items.Count; i += columns)
            {
                var rowGrid = new Grid { ColumnSpacing = 16 };
                for (int c = 0; c < columns; c++)
                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                for (int c = 0; c < columns && i + c < items.Count; c++)
                {
                    string name = items[i + c];
                    bool shown = kind == "mode" ? _settings.IsModeShown(name) : _settings.IsFontShown(name);
                    string quota = quotas != null && quotas.TryGetValue(name, out var q) ? q : null;
                    var toggle = new ToggleSwitch
                    {
                        Header = quota != null ? $"{name}  —  调用量：{quota}" : name,
                        IsOn = shown,
                        OnContent = "显示",
                        OffContent = "隐藏",
                        MinWidth = 140,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                    };
                    toggle.Tag = kind; // 标记类型用于保存
                    Grid.SetColumn(toggle, c);
                    rowGrid.Children.Add(toggle);
                }
                container.Children.Add(rowGrid);
            }
        }

        // ── 字体配置表格（与 OCR 表格同风格：表头+交替背景+边框包裹+行高缩小） ──
        private void PopulateFontTable(StackPanel container)
        {
            container.Children.Clear();
            var tablePanel = new StackPanel();

            // 表头
            tablePanel.Children.Add(CreateFontTableRow("字体名称", "显隐", isHeader: true));

            // 数据行（使用缓存的字体列表）
            var fonts = GetCachedFontNames();

            for (int i = 0; i < fonts.Count; i++)
                tablePanel.Children.Add(CreateFontTableRow(fonts[i], null, false, i % 2 == 1, fonts[i]));

            // 左右边框包裹
            container.Children.Add(new Border
            {
                BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBorderBrush"],
                BorderThickness = new Thickness(1, 0, 1, 0),
                CornerRadius = new CornerRadius(8),
                Child = tablePanel,
            });
        }

        private Grid CreateFontTableRow(string c1, string c2, bool isHeader, bool isOdd = false, string tag = null)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            if (isHeader)
            {
                grid.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SubtleControlBrush"];
                grid.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBorderBrush"];
                grid.BorderThickness = new Thickness(0, 0, 0, 1);
                AddFontTextCell(grid, 0, c1, true);
                AddFontTextCell(grid, 1, c2, true);
            }
            else
            {
                if (isOdd) grid.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SubtleControlBrush"];
                AddFontTextCell(grid, 0, c1, false);
                var toggle = new ToggleSwitch { IsOn = _settings.IsFontShown(c1), OnContent = "", OffContent = "", MinWidth = 50, Tag = tag, VerticalAlignment = VerticalAlignment.Center, MinHeight = 0 };
                Grid.SetColumn(toggle, 1); grid.Children.Add(toggle);
            }
            return grid;
        }

        private void AddFontTextCell(Grid grid, int col, string text, bool isHeader)
        {
            var tb = new TextBlock
            {
                Text = text,
                FontWeight = isHeader ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
                FontSize = 12, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 3, 6, 3),
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[isHeader ? "TextSecondaryBrush" : "TextPrimaryBrush"],
            };
            Grid.SetColumn(tb, col); grid.Children.Add(tb);
        }

        // ── OCR 模式表格布局（分类/模式/API Path/调用量/Toggle） ──────────
        private void PopulateOcrModeTable(StackPanel container)
        {
            container.Children.Clear();
            var tablePanel = new StackPanel();

            var groups = new (string label, string[] modes)[]
            {
                ("通用", new[] { "通用-标准", "通用-含位置", "高精-标准", "高精-含位置", "网络图", "手写识别", "数字识别" }),
                ("证件", new[] { "身份证", "银行卡", "驾驶证识别", "行驶证识别", "营业执照识别", "车牌识别" }),
                ("票据", new[] { "通用票据识别", "增值税发票识别", "火车票识别", "出租车票识别" }),
            };

            // 表头
            tablePanel.Children.Add(CreateOcrTableRow("分类", "模式", "API Path", "调用量", "显隐", isHeader: true));

            // 数据行
            int rowIdx = 0;
            foreach (var (label, modes) in groups)
            {
                for (int i = 0; i < modes.Length; i++)
                {
                    var mode = OcrTranslator.Models.OcrModes.All.FirstOrDefault(m => m.DisplayName == modes[i]);
                    if (mode == null) continue;
                    tablePanel.Children.Add(CreateOcrModeRow(i == 0 ? label : "", mode, rowIdx % 2 == 1));
                    rowIdx++;
                }
            }
            // 左右边框包裹
            container.Children.Add(new Border
            {
                BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBorderBrush"],
                BorderThickness = new Thickness(1, 0, 1, 0),
                CornerRadius = new CornerRadius(8),
                Child = tablePanel,
            });
        }

        private Grid CreateOcrTableRow(string c1, string c2, string c3, string c4, string c5, bool isHeader)
        {
            var grid = new Grid { Margin = new Thickness(0, 1, 0, 1) };
            if (isHeader)
            {
                grid.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SubtleControlBrush"];
                grid.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBorderBrush"];
                grid.BorderThickness = new Thickness(0, 0, 0, 1);
            }
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(55) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            var fw = isHeader ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
            AddOcrTextCell(grid, 0, c1, fw);
            AddOcrTextCell(grid, 1, c2, fw);
            AddOcrTextCell(grid, 2, c3, fw);
            AddOcrTextCell(grid, 3, c4, fw);
            if (isHeader) AddOcrTextCell(grid, 4, c5, fw);
            return grid;
        }

        private Grid CreateOcrModeRow(string category, OcrTranslator.Models.OcrMode mode, bool isOdd = false)
        {
            var grid = new Grid { Margin = new Thickness(0, 1, 0, 1) };
            if (isOdd)
                grid.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SubtleControlBrush"];
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(55) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            if (!string.IsNullOrEmpty(category))
                AddOcrTextCell(grid, 0, category, Microsoft.UI.Text.FontWeights.SemiBold);
            AddOcrTextCell(grid, 1, mode.DisplayName, Microsoft.UI.Text.FontWeights.Normal);
            AddOcrTextCell(grid, 2, mode.ApiPath, Microsoft.UI.Text.FontWeights.Normal);
            AddOcrTextCell(grid, 3, mode.Quota, Microsoft.UI.Text.FontWeights.Normal);
            var toggle = new ToggleSwitch { IsOn = _settings.IsModeShown(mode.DisplayName), OnContent = "", OffContent = "", MinWidth = 50, Tag = mode.DisplayName, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(toggle, 4);
            grid.Children.Add(toggle);
            return grid;
        }

        private void AddOcrTextCell(Grid grid, int col, string text, Windows.UI.Text.FontWeight fw)
        {
            var tb = new TextBlock
            {
                Text = text, FontWeight = fw, FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 6, 6, 6),
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextPrimaryBrush"],
            };
            Grid.SetColumn(tb, col);
            grid.Children.Add(tb);
        }

        private void SaveToggleSettings(StackPanel container, string kind)
        {
            AppSettings.BeginBatch(); // 自保护：无论调用方是否包了批量，都不会频繁写盘
            try
            {
                foreach (UIElement elem in container.Children)
                {
                    if (elem is Grid rowGrid)
                    {
                        foreach (UIElement child in rowGrid.Children)
                        {
                            if (child is ToggleSwitch toggle)
                            {
                                string name = toggle.Tag as string ?? toggle.Header as string;
                                if (name == null) continue;
                                if (kind == "mode") _settings.SetModeShown(name, toggle.IsOn);
                                else _settings.SetFontShown(name, toggle.IsOn);
                            }
                        }
                    }
                }
            }
            finally { AppSettings.EndBatch(); }
        }

        private void ThemeRadio_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;
            AppTheme theme = ThemeRadio.SelectedIndex switch
            {
                0 => AppTheme.Light,
                1 => AppTheme.Dark,
                _ => AppTheme.System,
            };
            _theme.Apply(theme);
            UpdateCaptionColors();
        }

        /// <summary>设置 caption 按钮（最小化/最大化/关闭）颜色，确保在 Mica 背景下清晰可见。</summary>
        private void UpdateCaptionColors()
        {
            if (!AppWindowTitleBar.IsCustomizationSupported()) return;
            var titleBar = this.AppWindow.TitleBar;
            bool isDark = _theme.Current == AppTheme.Dark;
            var fg = isDark ? Microsoft.UI.Colors.White : Microsoft.UI.ColorHelper.FromArgb(255, 30, 30, 30);
            titleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
            titleBar.ButtonHoverBackgroundColor = Microsoft.UI.ColorHelper.FromArgb(40, 128, 128, 128);
            titleBar.ButtonPressedBackgroundColor = Microsoft.UI.ColorHelper.FromArgb(80, 128, 128, 128);
            titleBar.ButtonForegroundColor = fg;
            titleBar.ButtonHoverForegroundColor = fg;
        }

        // ── 开机自启 ──────────────────────────────────────────

        private void StartupSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            if (StartupSwitch.IsOn != _startup.IsEnabled())
                _startup.SetEnabled(StartupSwitch.IsOn);
        }

        // ── OCR 快捷键录制 ────────────────────────────────────

        private void OcrHotkeyBtn_Click(object sender, RoutedEventArgs e)
        {
            Logger.Info("Settings", "开始录制 OCR 快捷键...");
            if (_isRecording) StopRecording();
            _recordingTarget = "Ocr";
            _isRecording = true;
            OcrHotkeyBtn.Content = "请按下组合键...";
            OcrHotkeyBtn.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Orange);
            StartRecording();
        }

        private void TranslateHotkeyBtn_Click(object sender, RoutedEventArgs e)
        {
            Logger.Info("Settings", "开始录制翻译快捷键...");
            if (_isRecording) StopRecording();
            _recordingTarget = "Translate";
            _isRecording = true;
            TranslateHotkeyBtn.Content = "请按下组合键...";
            TranslateHotkeyBtn.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Orange);
            StartRecording();
        }

        private void OcrHotkeyReset_Click(object sender, RoutedEventArgs e)
        {
            _currentOcrHotkey = DEFAULT_OCR_HOTKEY;
            OcrHotkeyBtn.Content = _currentOcrHotkey;
        }

        private void TranslateHotkeyReset_Click(object sender, RoutedEventArgs e)
        {
            _currentTranslateHotkey = DEFAULT_TRANSLATE_HOTKEY;
            TranslateHotkeyBtn.Content = _currentTranslateHotkey;
        }

        private void StartRecording()
        {
            _shiftPressed = false;
            _ctrlPressed = false;
            _altPressed = false;
            _hookProc = HookCallback;
            _hookId = SetHook(_hookProc);

            _recordingTimeoutTimer?.Stop();
            _recordingTimeoutTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            _recordingTimeoutTimer.Tick += (s, e) =>
            {
                _recordingTimeoutTimer.Stop();
                if (_isRecording)
                {
                    StopRecording();
                    Logger.Warn("Settings", "快捷键录制超时，自动停止");
                }
            };
            _recordingTimeoutTimer.Start();
        }

        private void StopRecording()
        {
            _isRecording = false;
            _recordingTimeoutTimer?.Stop();
            _recordingTimeoutTimer = null;
            UnhookKeyboard();
            _recordingTarget = "";

            DispatcherQueue.TryEnqueue(() =>
            {
                OcrHotkeyBtn.Content = _currentOcrHotkey;
                TranslateHotkeyBtn.Content = _currentTranslateHotkey;
            });
        }

        private IntPtr SetHook(Platform.Win32.NativeMethods.LowLevelKeyboardProc proc)
        {
            var curModule = System.Diagnostics.Process.GetCurrentProcess().MainModule;
            return Platform.Win32.NativeMethods.SetWindowsHookEx(13, proc, Platform.Win32.NativeMethods.GetModuleHandle(curModule.ModuleName), 0);
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int vkCode = Marshal.ReadInt32(lParam);
                bool isKeyDown = wParam == (IntPtr)0x0100;
                bool isKeyUp = wParam == (IntPtr)0x0101;

                // ESC 取消录制
                if (isKeyDown && vkCode == 0x1B)
                {
                    StopRecording();
                    return Platform.Win32.NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
                }

                // 追踪修饰键状态
                if (vkCode == 0x10 || vkCode == 0xA0 || vkCode == 0xA1)
                {
                    if (isKeyDown) _shiftPressed = true;
                    else if (isKeyUp) _shiftPressed = false;
                    return Platform.Win32.NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
                }
                if (vkCode == 0x11 || vkCode == 0xA2 || vkCode == 0xA3)
                {
                    if (isKeyDown) _ctrlPressed = true;
                    else if (isKeyUp) _ctrlPressed = false;
                    return Platform.Win32.NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
                }
                if (vkCode == 0x12 || vkCode == 0xA4 || vkCode == 0xA5)
                {
                    if (isKeyDown) _altPressed = true;
                    else if (isKeyUp) _altPressed = false;
                    return Platform.Win32.NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
                }
                if (vkCode == 0x5B || vkCode == 0x5C)
                {
                    return Platform.Win32.NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
                }

                // 普通键按下时，尝试录制快捷键
                if (isKeyDown && _isRecording)
                {
                    // 单功能键（F1-F12、PrintScreen 等）可独立作为快捷键，无需修饰键
                    bool isFunctionKey = (vkCode >= 0x70 && vkCode <= 0x7B)  // F1-F12
                        || vkCode == 0x2C   // PrintScreen
                        || vkCode == 0x13   // Pause
                        || vkCode == 0x91   // ScrollLock
                        || vkCode == 0x90;  // NumLock

                    var parts = new List<string>();
                    if (_ctrlPressed) parts.Add("Ctrl");
                    if (_altPressed) parts.Add("Alt");
                    if (_shiftPressed) parts.Add("Shift");
                    parts.Add(GetKeyName(vkCode));

                    string hotkey = string.Join("+", parts);

                    // 允许：组合键（修饰键+普通键）或 单功能键
                    if (parts.Count >= 2 || (parts.Count == 1 && isFunctionKey))
                    {
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            if (_recordingTarget == "Ocr")
                            {
                                _currentOcrHotkey = hotkey;
                                OcrHotkeyBtn.Content = hotkey;
                            }
                            else if (_recordingTarget == "Translate")
                            {
                                _currentTranslateHotkey = hotkey;
                                TranslateHotkeyBtn.Content = hotkey;
                            }
                            StopRecording();
                        });
                        return (IntPtr)1; // 吞掉已录制的键
                    }
                    else
                    {
                        // 普通键无修饰：提示并继续等待（不停止录制，可继续按）
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            string hint = "请按 组合键 或 功能键(F1-F12)";
                            if (_recordingTarget == "Ocr")
                                OcrHotkeyBtn.Content = hint;
                            else if (_recordingTarget == "Translate")
                                TranslateHotkeyBtn.Content = hint;
                        });
                        // 重置修饰键状态，避免残留导致后续判断异常
                        _shiftPressed = false;
                        _ctrlPressed = false;
                        _altPressed = false;
                        return Platform.Win32.NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
                    }
                }
            }
            return Platform.Win32.NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        private void UnhookKeyboard()
        {
            if (_hookId != IntPtr.Zero)
            {
                Platform.Win32.NativeMethods.UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
            }
        }

        private string GetKeyName(int vkCode)
        {
            return vkCode switch
            {
                0x08 => "Backspace", 0x09 => "Tab", 0x0D => "Enter", 0x13 => "Pause",
                0x14 => "CapsLock", 0x1B => "Esc", 0x20 => "Space",
                0x21 => "PageUp", 0x22 => "PageDown", 0x23 => "End", 0x24 => "Home",
                0x25 => "Left", 0x26 => "Up", 0x27 => "Right", 0x28 => "Down",
                0x2C => "PrintScreen", 0x2D => "Insert", 0x2E => "Delete",
                0x5B => "LWin", 0x5C => "RWin",
                0x60 => "Num0", 0x61 => "Num1", 0x62 => "Num2", 0x63 => "Num3",
                0x64 => "Num4", 0x65 => "Num5", 0x66 => "Num6", 0x67 => "Num7",
                0x68 => "Num8", 0x69 => "Num9",
                0x6A => "Num*", 0x6B => "Num+", 0x6D => "Num-", 0x6E => "Num.", 0x6F => "Num/",
                0x70 => "F1", 0x71 => "F2", 0x72 => "F3", 0x73 => "F4",
                0x74 => "F5", 0x75 => "F6", 0x76 => "F7", 0x77 => "F8",
                0x78 => "F9", 0x79 => "F10", 0x7A => "F11", 0x7B => "F12",
                0x90 => "NumLock", 0x91 => "ScrollLock",
                0xBA => ";", 0xBB => "=", 0xBC => ",", 0xBD => "-",
                0xBE => ".", 0xBF => "/", 0xC0 => "`",
                0xDB => "[", 0xDC => "\\", 0xDD => "]", 0xDE => "'",
                _ => ((char)vkCode).ToString().ToUpper()
            };
        }

        private void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            Logger.Info("Settings", "保存设置...");
            AppSettings.BeginBatch(); // 批量模式：避免大量 Set 频繁写盘导致卡顿

            _settings.OcrApiKey = OcrApiKeyBox.Text;
            _settings.OcrSecretKey = OcrSecretKeyBox.Text;
            _settings.VoiceApiKey = VoiceApiKeyBox.Text;
            _settings.VoiceSecretKey = VoiceSecretKeyBox.Text;
            _settings.TransAppId = TransAppIdBox.Text;
            _settings.TransSecretKey = TransSecretKeyBox.Text;

            _settings.AutoTranslate = AutoTranslateSwitch.IsOn;

            // 保存默认 OCR 模式
            if (DefaultOcrModeComboBox.SelectedItem is ComboBoxItem selectedMode)
                _settings.DefaultOcrMode = selectedMode.Content.ToString();

            SaveToggleSettings(OcrModesContainer, "mode");
            SaveToggleSettings(FontsContainer, "font");

            _settings.OcrHotkey = _currentOcrHotkey;
            _settings.TranslateHotkey = _currentTranslateHotkey;
            Logger.Info("Settings", $"保存快捷键: OCR={_currentOcrHotkey}, Translate={_currentTranslateHotkey}");

            AppSettings.EndBatch(); // 一次性写盘
            Logger.Info("Settings", "设置保存完成");
            this.Close();
        }

    }
}
