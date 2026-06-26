using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using OcrTranslator.Abstractions;
using OcrTranslator.Models;
using OcrTranslator.Platform;
using OcrTranslator.Services;
using OcrTranslator.ViewModels;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage.Streams;
using OcrTranslator.Platform.Win32;

namespace OcrTranslator.Views
{
    public sealed partial class MainWindow : Window
    {
        // ── 注入服务 ──────────────────────────────────────────
        private readonly SettingsService _settings;
        private readonly ThemeService _theme;
        private readonly IOcrEngine _ocrEngine;
        private readonly ITranslator _translator;
        private readonly ISpeechSynthesizer _speech;
        private readonly ScreenCaptureService _screenCapture;
        private readonly ClipboardService _clipboard;
        private readonly WindowPlacementService _windowPlacement;
        private readonly IBaiduTokenProvider _tokenProvider;

        // ── ViewModel（x:Bind 目标，须在 InitializeComponent 前赋值） ──
        public MainViewModel Vm { get; }

        // ── 热键系统 ──────────────────────────────────────────
        private HotkeyService _hotkeyManager;

        // ── 截图状态 ──────────────────────────────────────────
        private bool _isCapturing;
        private CaptureWindow? _captureWindow;

        // ── TTS ───────────────────────────────────────────────
        private CancellationTokenSource? _ttsCts;
        private bool _isTtsCoolingDown;
        private readonly MediaPlayer _mediaPlayer = new();
        private readonly Queue<string> _ttsQueue = new();
        private int _currentTtsVoice;
        private string _currentTtsLan = "zh";
        private bool _currentTtsIsWebSocket;
        private InMemoryRandomAccessStream? _previousAudioStream;
        private byte[]? _lastAudioBytes;

        // ── UI ────────────────────────────────────────────────
        private ScrollViewer? _originalScroll, _translatedScroll;
        private bool _isSyncing;
        private bool _initialFocusSet; // 启动后是否已设置初始焦点
        private bool _isForceExiting;
        private SettingsWindow? _settingsWindow;

        // ── 托盘命令 ──────────────────────────────────────────
        public ICommand ShowWindowCommand { get; }
        public ICommand MenuExitCommand { get; }
        public ICommand MenuSettingsCommand { get; }

        public MainWindow()
        {
            // 取 DI 服务（须先于 InitializeComponent，x:Bind Vm 依赖）
            _settings = App.Services.GetService<SettingsService>()!;
            _theme = App.Services.GetService<ThemeService>()!;
            _ocrEngine = App.Services.GetService<IOcrEngine>()!;
            _translator = App.Services.GetService<ITranslator>()!;
            _speech = App.Services.GetService<ISpeechSynthesizer>()!;
            _screenCapture = App.Services.GetService<ScreenCaptureService>()!;
            _clipboard = App.Services.GetService<ClipboardService>()!;
            _windowPlacement = App.Services.GetService<WindowPlacementService>()!;
            _tokenProvider = App.Services.GetService<IBaiduTokenProvider>()!;
            Vm = App.Services.GetService<MainViewModel>()!;

            // 托盘命令
            ShowWindowCommand = new RelayCommand(ForceForeground);
            MenuSettingsCommand = new RelayCommand(OpenSettingsWindow);
            MenuExitCommand = new RelayCommand(() =>
            {
                _isForceExiting = true;
                _hotkeyManager?.Dispose();
                if (TrayIcon != null) { TrayIcon.Visibility = Visibility.Collapsed; TrayIcon.Dispose(); }
                _previousAudioStream?.Dispose();
                _mediaPlayer.Dispose();
                _ttsCts?.Cancel();
                _ttsCts?.Dispose();
                Process.GetCurrentProcess().Kill();
            });

            this.InitializeComponent();
            this.Bindings.Update();

            // Mica 云母材质背景
            SystemBackdrop = new MicaBackdrop();

            // 托盘图标 + 窗口图标（用 v4 生成的 app.ico）
            try
            {
                var icoPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
                TrayIcon.Icon = new System.Drawing.Icon(icoPath);
                this.AppWindow.SetIcon(icoPath);
            }
            catch (Exception ex) { Logger.Error("MainWindow", "图标加载失败", ex); }

            // 主题应用到根元素 + 同步菜单选中态
            if (this.Content is FrameworkElement root)
                _theme.ApplyTo(root);
            UpdateThemeMenu();

            PopulateVoiceList();
            ReloadSettingsFromLocal();
            RestoreComboBoxState();

            // 深色模式下 TextBox 悬浮/聚焦不变色（覆盖控件模板 PointerOver 状态）
            var cardBg = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundBrush"];
            OriginalTextBox.PointerEntered += (s, e) => OriginalTextBox.Background = cardBg;
            OriginalTextBox.PointerExited += (s, e) => OriginalTextBox.Background = cardBg;
            TranslatedTextBox.PointerEntered += (s, e) => TranslatedTextBox.Background = cardBg;
            TranslatedTextBox.PointerExited += (s, e) => TranslatedTextBox.Background = cardBg;

            // 自定义标题栏
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
            if (AppWindowTitleBar.IsCustomizationSupported())
            {
                var titleBar = this.AppWindow.TitleBar;
                titleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
                titleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
                titleBar.ButtonForegroundColor = Microsoft.UI.ColorHelper.FromArgb(255, 120, 120, 120);
            }

            _windowPlacement.Restore(this, "MainWindow", 1200, 750);

            this.AppWindow.Closing += (s, e) =>
            {
                if (!_isForceExiting)
                {
                    e.Cancel = true;
                    SaveComboBoxState();
                    _windowPlacement.Save(this, "MainWindow");
                    this.AppWindow.Hide();
                }
            };
            this.Activated += MainWindow_Activated;

            // 初始化热键系统
            Logger.Info("MainWindow", "正在初始化热键系统...");
            IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            _hotkeyManager = new HotkeyService(hWnd);
            _hotkeyManager.HotkeyPressed += OnHotkeyPressed;
            RegisterCustomHotkeys();
            Logger.Info("MainWindow", "热键系统初始化完成");

            _mediaPlayer.MediaEnded += (s, e) => { DispatcherQueue.TryEnqueue(PlayNextChunk); };

            Logger.Info("MainWindow", "MainWindow 初始化完成");
        }

        // ── 主题切换 ──────────────────────────────────────────

        private void ThemeMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (ReferenceEquals(sender, ThemeLightItem)) _theme.Apply(AppTheme.Light);
            else if (ReferenceEquals(sender, ThemeDarkItem)) _theme.Apply(AppTheme.Dark);
            else _theme.Apply(AppTheme.System);
            UpdateThemeMenu();
        }

        private void UpdateThemeMenu()
        {
            ThemeLightItem.IsChecked = _theme.Current == AppTheme.Light;
            ThemeDarkItem.IsChecked = _theme.Current == AppTheme.Dark;
            ThemeSystemItem.IsChecked = _theme.Current == AppTheme.System;
        }

        // ── 热键管理 ──────────────────────────────────────────

        public void RegisterCustomHotkeys()
        {
            _hotkeyManager.ClearAll();

            string ocrHotkey = _settings.OcrHotkey;
            if (_hotkeyManager.RegisterHotkey(ocrHotkey, "OCR"))
                Logger.Info("MainWindow", $"注册 OCR 热键: {ocrHotkey}");
            else
                Logger.Warn("MainWindow", $"OCR 热键注册失败: {ocrHotkey}");

            string translateHotkey = _settings.TranslateHotkey;
            if (_hotkeyManager.RegisterHotkey(translateHotkey, "Translate"))
                Logger.Info("MainWindow", $"注册翻译热键: {translateHotkey}");
            else
                Logger.Warn("MainWindow", $"翻译热键注册失败: {translateHotkey}");
        }

        private void OnHotkeyPressed(string name)
        {
            Logger.Info("MainWindow", $"热键触发: {name}");
            switch (name)
            {
                case "OCR":
                    DispatcherQueue.TryEnqueue(StartOCR);
                    break;
                case "Translate":
                    DispatcherQueue.TryEnqueue(QuickTranslate);
                    break;
            }
        }

        // ── OCR 截图流程 ──────────────────────────────────────

        private void StartOCR()
        {
            Logger.Info("MainWindow", $"StartOCR, _isCapturing={_isCapturing}");
            if (_isCapturing) { Logger.Warn("MainWindow", "正在截图中，忽略"); return; }
            _isCapturing = true;

            try
            {
                var screen = _screenCapture.CaptureAtCursor();

                // 隐藏主窗口
                this.AppWindow.Hide();

                // 创建选区窗口
                _captureWindow = new CaptureWindow(screen.PngBytes, screen.Left, screen.Top, screen.Width, screen.Height);
                _captureWindow.ImageCaptured += OnImageCaptured;
                _captureWindow.Cancelled += () =>
                {
                    _isCapturing = false;
                    _captureWindow = null;
                    Logger.Info("MainWindow", "截图已取消，静默退出");
                };
                _captureWindow.Show();
                Logger.Info("MainWindow", "选区窗口已显示");
            }
            catch (Exception ex)
            {
                Logger.Error("MainWindow", "截图过程出错", ex);
                _isCapturing = false;
                this.AppWindow.Show();
                this.Activate();
            }
        }

        private void OnImageCaptured(byte[] imageBytes)
        {
            Logger.Info("MainWindow", $"收到图片: {imageBytes.Length} bytes");

            DispatcherQueue.TryEnqueue(async () =>
            {
                var loadingWin = new LoadingWindow();
                loadingWin.Activate();

                try
                {
                    string modeName = ModeComboBox.SelectedItem != null
                        ? ((ComboBoxItem)ModeComboBox.SelectedItem).Content!.ToString()!
                        : "通用-标准";
                    OcrMode mode = OcrModes.ByDisplayName(modeName);
                    Logger.Info("MainWindow", $"OCR模式: {modeName} -> {mode.ApiPath}");

                    Stopwatch sw = Stopwatch.StartNew();
                    Logger.Info("MainWindow", "调用 OCR API...");
                    string ocrResult = await Task.Run(() => _ocrEngine.RecognizeAsync(imageBytes, mode));
                    sw.Stop();
                    Logger.Info("MainWindow", $"OCR完成, 长度={ocrResult?.Length ?? 0}, 耗时={sw.ElapsedMilliseconds}ms");

                    // 显示 OCR 结果（纯文本 + 单独耗时标签）
                    Vm.OriginalText = string.IsNullOrEmpty(ocrResult) ? "(未检测到有效字符)" : ocrResult;
                    Vm.OcrElapsed = $"[OCR耗时: {sw.ElapsedMilliseconds} ms]";
                    Vm.TranslatedText = "";
                    Vm.TranslateElapsed = "";
                    loadingWin.Close();
                    _captureWindow = null;
                    _isCapturing = false;
                    ForceForeground();

                    // 异步翻译（不阻塞 UI）：结构化结果（银行卡/身份证）不翻译
                    bool autoTrans = _settings.AutoTranslate;
                    if (autoTrans && !string.IsNullOrEmpty(ocrResult)
                        && !ocrResult.StartsWith("[银行卡") && !ocrResult.StartsWith("[身份证"))
                    {
                        Vm.TranslatedText = "翻译中...";
                        Logger.Info("MainWindow", "异步翻译开始...");
                        await Vm.TranslateTextAsync(ocrResult);
                    }
                    return;
                }
                catch (Exception ex)
                {
                    Logger.Error("MainWindow", "OCR过程出错", ex);
                    Vm.OriginalText = "请求失败：";
                    Vm.OcrElapsed = "";
                    Vm.TranslatedText = ex.Message;
                }

                loadingWin.Close();
                _captureWindow = null;
                _isCapturing = false;
                ForceForeground();
                Logger.Info("MainWindow", "主窗口已恢复");
            });
        }

        // ── 快捷翻译（Ctrl+C 读取 → 翻译 → Ctrl+V 原地替换） ──

        private async void QuickTranslate()
        {
            try
            {
                // 热键触发时焦点已切换到本应用，需恢复原应用焦点
                this.AppWindow.Hide();
                await Task.Delay(150);

                _clipboard.SendCopy(); // Ctrl+C
                await Task.Delay(300);

                string? text = _clipboard.GetText();
                Logger.Info("MainWindow", $"快捷翻译: 剪贴板=[{text?.Substring(0, Math.Min(30, text?.Length ?? 0))}]");
                if (string.IsNullOrWhiteSpace(text))
                {
                    Logger.Warn("MainWindow", "快捷翻译: 无内容");
                    this.AppWindow.Show();
                    return;
                }
                string toLang = LanguageDetector.TranslateTarget(text);
                string translated = await _translator.TranslateAsync(text, toLang);
                if (string.IsNullOrWhiteSpace(translated)) { this.AppWindow.Show(); return; }
                _clipboard.SetText(translated);
                await Task.Delay(50);
                _clipboard.SendPaste(); // Ctrl+V
                Logger.Info("MainWindow", "快捷翻译完成");
            }
            catch (Exception ex) { Logger.Error("MainWindow", "快捷翻译失败", ex); }
        }

        // ── 设置 & UI ─────────────────────────────────────────

        public void ReloadSettingsFromLocal()
        {
            if (ModeComboBox != null)
            {
                ModeComboBox.Items.Clear();
                foreach (var m in OcrModes.All)
                {
                    if (_settings.IsModeShown(m.DisplayName))
                        ModeComboBox.Items.Add(new ComboBoxItem { Content = m.DisplayName });
                }
                // 默认选择配置的默认 OCR 模式
                string defaultMode = _settings.DefaultOcrMode;
                int defaultModeIdx = 0;
                for (int i = 0; i < ModeComboBox.Items.Count; i++)
                {
                    if (((ModeComboBox.Items[i] as ComboBoxItem)?.Content?.ToString()) == defaultMode)
                    { defaultModeIdx = i; break; }
                }
                if (ModeComboBox.Items.Count > 0) ModeComboBox.SelectedIndex = defaultModeIdx;
            }

            var leftFlyout = LeftFontBtn.Flyout as MenuFlyout;
            var rightFlyout = RightFontBtn.Flyout as MenuFlyout;

            if (leftFlyout != null && rightFlyout != null)
            {
                leftFlyout.Items.Clear();
                rightFlyout.Items.Clear();
                foreach (var fontName in System.Drawing.FontFamily.Families.Select(f => f.Name).Where(n => !n.StartsWith("@")).Distinct())
                {
                    if (_settings.IsFontShown(fontName))
                    {
                        leftFlyout.Items.Add(new MenuFlyoutItem { Text = fontName });
                        rightFlyout.Items.Add(new MenuFlyoutItem { Text = fontName });
                    }
                }
            }

            ApplyFont(OriginalTextBox, "Consolas");
            ApplyFont(TranslatedTextBox, "Microsoft YaHei UI");

            // 密钥注入 Token 缓存
            _tokenProvider.UpdateCredentials(
                _settings.OcrApiKey, _settings.OcrSecretKey,
                _settings.VoiceApiKey, _settings.VoiceSecretKey);
        }

        private void ApplyFont(TextBox? target, string fontName)
        {
            if (target != null) target.FontFamily = new FontFamily($"{fontName}, Microsoft YaHei UI, sans-serif");
        }

        private void SaveComboBoxState()
        {
            AppSettings.BeginBatch();
            if (ModeComboBox != null) _settings.SelectedMode = ModeComboBox.SelectedIndex;
            if (SpeechTargetComboBox != null) _settings.SelectedSpeechTarget = SpeechTargetComboBox.SelectedIndex;
            if (TtsProtocolComboBox != null) _settings.SelectedTtsProtocol = TtsProtocolComboBox.SelectedIndex;
            if (VoiceCategoryComboBox != null) _settings.SelectedVoiceCategory = VoiceCategoryComboBox.SelectedIndex;
            if (VoiceComboBox != null) _settings.SelectedVoice = VoiceComboBox.SelectedIndex;
            AppSettings.EndBatch();
        }

        private void RestoreComboBoxState()
        {
            if (ModeComboBox.Items.Count > 0 && AppSettings.ContainsKey("SelectedMode"))
            {
                int idx = _settings.SelectedMode;
                if (idx >= 0 && idx < ModeComboBox.Items.Count) ModeComboBox.SelectedIndex = idx;
            }
            if (SpeechTargetComboBox != null && AppSettings.ContainsKey("SelectedSpeechTarget"))
            {
                int idx = _settings.SelectedSpeechTarget;
                if (idx >= 0 && idx < SpeechTargetComboBox.Items.Count) SpeechTargetComboBox.SelectedIndex = idx;
            }
            if (TtsProtocolComboBox != null && AppSettings.ContainsKey("SelectedTtsProtocol"))
            {
                int idx = _settings.SelectedTtsProtocol;
                if (idx >= 0 && idx < TtsProtocolComboBox.Items.Count) TtsProtocolComboBox.SelectedIndex = idx;
            }
            if (VoiceCategoryComboBox != null && AppSettings.ContainsKey("SelectedVoiceCategory"))
            {
                int idx = _settings.SelectedVoiceCategory;
                if (idx >= 0 && idx < VoiceCategoryComboBox.Items.Count) VoiceCategoryComboBox.SelectedIndex = idx;
            }
            if (VoiceComboBox.Items.Count > 0 && AppSettings.ContainsKey("SelectedVoice"))
            {
                int idx = _settings.SelectedVoice;
                if (idx >= 0 && idx < VoiceComboBox.Items.Count) VoiceComboBox.SelectedIndex = idx;
            }
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e) => OpenSettingsWindow();

        private void OpenSettingsWindow()
        {
            if (_settingsWindow == null)
            {
                _settingsWindow = new SettingsWindow();
                _settingsWindow.Closed += (s, ev) =>
                {
                    // 设置页关闭后重载主窗口（密钥/模式/字体/主题）
                    ReloadSettingsFromLocal();
                    RegisterCustomHotkeys();
                    _settingsWindow = null;
                };
                _settingsWindow.Activate();
            }
            else _settingsWindow.Activate();
        }

        private void PinButton_Click(object sender, RoutedEventArgs e)
        {
            var presenter = this.AppWindow.Presenter as OverlappedPresenter;
            if (presenter != null) presenter.IsAlwaysOnTop = PinButton.IsChecked == true;
        }

        private void PopulateVoiceList()
        {
            if (VoiceComboBox == null || VoiceCategoryComboBox == null) return;
            var category = VoiceCatalog.CategoryFromIndex(VoiceCategoryComboBox.SelectedIndex);
            var voices = VoiceCatalog.OfCategory(category);

            VoiceComboBox.Items.Clear();
            foreach (var voice in voices)
                VoiceComboBox.Items.Add(new ComboBoxItem { Content = voice.Name, Tag = voice.Id });

            // 默认选择度丫丫（基础 id=4），否则第一项
            int defaultIdx = 0;
            for (int i = 0; i < VoiceComboBox.Items.Count; i++)
            {
                if ((int)((ComboBoxItem)VoiceComboBox.Items[i]).Tag == VoiceCatalog.DefaultVoiceId)
                { defaultIdx = i; break; }
            }
            if (VoiceComboBox.Items.Count > 0) VoiceComboBox.SelectedIndex = defaultIdx;
        }

        private void VoiceCategoryComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            PopulateVoiceList();
            SaveComboBoxState();
        }

        private void ForceForeground()
        {
            this.AppWindow.Show();
            this.Activate();
            IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

            IntPtr foregroundWnd = NativeMethods.GetForegroundWindow();
            // 使用 AttachThreadInput 强制设置前台窗口
            uint foregroundThread = NativeMethods.GetWindowThreadProcessId(foregroundWnd, out _);
            uint currentThread = NativeMethods.GetCurrentThreadId();

            if (foregroundThread != currentThread)
            {
                NativeMethods.AttachThreadInput(foregroundThread, currentThread, true);
                NativeMethods.SetForegroundWindow(hWnd);
                NativeMethods.AttachThreadInput(foregroundThread, currentThread, false);
            }
            else
            {
                NativeMethods.SetForegroundWindow(hWnd);
            }

            NativeMethods.ShowWindow(hWnd, NativeMethods.SW_RESTORE);
            NativeMethods.BringWindowToTop(hWnd);
        }

        // ── 翻译按钮 ──────────────────────────────────────────

        private async void TranslateButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(Vm.OriginalText) || Vm.OriginalText.StartsWith("("))
            {
                ShowInfo(InfoBarSeverity.Warning, "原文为空，请先 OCR 识别或粘贴文本后再翻译");
                return;
            }
            await Vm.TranslateTextAsync(Vm.OriginalText);
        }

        // ── TTS 朗读 ──────────────────────────────────────────

        private async void PlaySpeechButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isTtsCoolingDown) return;
            if (Vm.IsSpeaking)
            {
                _isTtsCoolingDown = true;
                _mediaPlayer.Pause();
                _ttsCts?.Cancel();
                _ttsCts?.Dispose();
                _ttsCts = null;
                _ttsQueue.Clear();
                ResetSpeechState();
                await Task.Delay(500);
                _isTtsCoolingDown = false;
                return;
            }

            int targetIdx = SpeechTargetComboBox != null ? SpeechTargetComboBox.SelectedIndex : 0;
            string text = targetIdx == 0 ? Vm.OriginalText : Vm.TranslatedText;
            text = Regex.Replace(text, @"\n\n\[.*?耗时.*?\]", "");
            if (string.IsNullOrWhiteSpace(text) || text.StartsWith("(") || text.StartsWith("请求失败"))
            {
                ShowInfo(InfoBarSeverity.Warning, "没有可朗读的文本，请先识别或翻译");
                return;
            }

            Vm.IsSpeaking = true;
            _isTtsCoolingDown = true;
            PlayIcon.Glyph = ""; // 暂停图标
            if (PlayText != null) PlayText.Text = "停止";
            ToolTipService.SetToolTip(PlaySpeechButton, "停止朗读");

            _ttsCts = new CancellationTokenSource();
            _currentTtsLan = LanguageDetector.SpeechLanguage(text);
            _currentTtsVoice = VoiceComboBox.SelectedItem != null
                ? (int)((ComboBoxItem)VoiceComboBox.SelectedItem).Tag!
                : VoiceCatalog.DefaultVoiceId;
            _currentTtsIsWebSocket = TtsProtocolComboBox != null && TtsProtocolComboBox.SelectedIndex == 1;

            EnqueueTextChunks(text);
            PlayNextChunk();
            _isTtsCoolingDown = false;
        }

        private void EnqueueTextChunks(string text)
        {
            _ttsQueue.Clear();
            var sentences = Regex.Split(text, @"(?<=[。！？；\n\r.!?;])");
            string chunk = "";
            foreach (var s in sentences)
            {
                if (string.IsNullOrWhiteSpace(s)) continue;
                if (chunk.Length + s.Length > 300) { if (!string.IsNullOrWhiteSpace(chunk)) _ttsQueue.Enqueue(chunk); chunk = s; }
                else chunk += s;
            }
            if (!string.IsNullOrWhiteSpace(chunk)) _ttsQueue.Enqueue(chunk);
        }

        private async void PlayNextChunk()
        {
            if (_ttsQueue.Count == 0 || _ttsCts?.IsCancellationRequested == true) { ResetSpeechState(); return; }
            string chunk = _ttsQueue.Dequeue();
            try
            {
                var voice = new VoiceInfo("", _currentTtsVoice, VoiceCategory.Basic);
                byte[] audio = await Task.Run(() => _speech.SynthesizeAsync(chunk, voice, _currentTtsLan, _currentTtsIsWebSocket, _ttsCts!.Token), _ttsCts!.Token);

                if (audio.Length > 0 && !_ttsCts.Token.IsCancellationRequested)
                {
                    _lastAudioBytes = audio; // 缓存用于下载

                    // 释放上一段音频流，防止内存泄漏
                    if (_previousAudioStream != null)
                    {
                        _previousAudioStream.Dispose();
                        _previousAudioStream = null;
                    }

                    var stream = new InMemoryRandomAccessStream();
                    using (var writer = new DataWriter(stream.GetOutputStreamAt(0))) { writer.WriteBytes(audio); await writer.StoreAsync(); }
                    stream.Seek(0);
                    _previousAudioStream = stream;
                    _mediaPlayer.Source = MediaSource.CreateFromStream(stream, "audio/mp3");
                    _mediaPlayer.Play();
                }
                else ResetSpeechState();
            }
            catch (OperationCanceledException) { ResetSpeechState(); }
            catch (Exception ex) { ResetSpeechState(); ShowInfo(InfoBarSeverity.Error, $"音频错误：{ex.Message}"); }
        }

        private void ResetSpeechState()
        {
            Vm.IsSpeaking = false;
            PlayIcon.Glyph = "";
            if (PlayText != null) PlayText.Text = "朗读";
            ToolTipService.SetToolTip(PlaySpeechButton, "朗读文本");
            _ttsCts?.Dispose();
            _ttsCts = null;
        }

        /// <summary>下载朗读音频到本地 MP3 文件。</summary>
        private async void DownloadSpeechButton_Click(object sender, RoutedEventArgs e)
        {
            if (_lastAudioBytes == null || _lastAudioBytes.Length == 0)
            {
                ShowInfo(InfoBarSeverity.Warning, "没有可下载的音频，请先点击朗读生成音频");
                return;
            }

            try
            {
                var picker = new Windows.Storage.Pickers.FileSavePicker();
                WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
                picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.MusicLibrary;
                picker.FileTypeChoices.Add("MP3 音频", new List<string> { ".mp3" });
                picker.FileTypeChoices.Add("WAV 音频", new List<string> { ".wav" });
                picker.SuggestedFileName = $"极客OCR_{DateTime.Now:yyyyMMdd_HHmmss}";

                var file = await picker.PickSaveFileAsync();
                if (file != null)
                {
                    await Windows.Storage.FileIO.WriteBytesAsync(file, _lastAudioBytes);
                    ShowInfo(InfoBarSeverity.Success, $"音频已保存到：{file.Path}");
                }
            }
            catch (Exception ex)
            {
                ShowInfo(InfoBarSeverity.Error, $"保存失败：{ex.Message}");
            }
        }

        // ── 状态提示横幅（非侵入，自动消失，不覆盖译文） ──────
        private DispatcherTimer? _infoBarTimer;
        private void ShowInfo(InfoBarSeverity severity, string message)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                _infoBarTimer?.Stop();
                StatusInfoBar.Severity = severity;
                StatusInfoBar.Message = message;
                StatusInfoBar.IsOpen = true;
                _infoBarTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.5) };
                _infoBarTimer.Tick += (s, e) => { _infoBarTimer!.Stop(); StatusInfoBar.IsOpen = false; };
                _infoBarTimer.Start();
            });
        }

        // ── 滚动同步 ──────────────────────────────────────────

        private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
        {
            // 首次激活时把焦点放到原文输入框（避免默认落在标题栏主题按钮）
            if (!_initialFocusSet && args.WindowActivationState != WindowActivationState.Deactivated)
            {
                _initialFocusSet = true;
                DispatcherQueue.TryEnqueue(() => OriginalTextBox?.Focus(FocusState.Programmatic));
            }

            if (_originalScroll == null && OriginalTextBox != null)
            {
                _originalScroll = FindVisualChild<ScrollViewer>(OriginalTextBox);
                _translatedScroll = FindVisualChild<ScrollViewer>(TranslatedTextBox);
                if (_originalScroll != null && _translatedScroll != null)
                {
                    _originalScroll.ViewChanged += (s, e) => SyncScroll(_originalScroll, _translatedScroll);
                    _translatedScroll.ViewChanged += (s, e) => SyncScroll(_translatedScroll, _originalScroll);
                }
            }
        }

        private void SyncScroll(ScrollViewer source, ScrollViewer target)
        {
            if (_isSyncing) return;
            _isSyncing = true;
            target.ChangeView(null, source.VerticalOffset, null, true);
            _isSyncing = false;
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typed) return typed;
                var result = FindVisualChild<T>(child);
                if (result != null) return result;
            }
            return null;
        }
    }
}
