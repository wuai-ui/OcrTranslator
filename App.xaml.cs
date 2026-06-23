using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using OcrTranslator.Abstractions;
using OcrTranslator.Services;
using OcrTranslator.ViewModels;
using OcrTranslator.Views;

namespace OcrTranslator;

/// <summary>
/// 应用入口：构造 DI 容器，注册全部服务/引擎/VM，启动主窗口。
/// </summary>
public partial class App : Application
{
    /// <summary>全局 DI 容器（视图与服务通过它解析依赖）。</summary>
    public static IServiceProvider Services { get; private set; } = null!;

    /// <summary>主窗体实例指针（供需要全局访问的场合）。</summary>
    public static MainWindow? MainWindowInstance { get; private set; }

    private Window? m_window;

    public App()
    {
        Logger.Info("App", "========================================");
        Logger.Info("App", "极客 OCR v4.0 应用启动");
        Logger.Info("App", $"日志目录: {Logger.GetLogDirectory()}");
        Logger.Info("App", "========================================");

        // 捕获所有未处理异常并写入日志
        this.UnhandledException += (s, e) =>
        {
            Logger.Error("App", $"未处理异常: {e.Exception?.GetType().FullName}", e.Exception);
            e.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            Logger.Error("App", $"AppDomain 未处理异常: {(e.ExceptionObject as Exception)?.GetType().FullName}", e.ExceptionObject as Exception);
        };

        try
        {
            this.InitializeComponent();
        }
        catch (Exception ex)
        {
            Logger.Error("App", "InitializeComponent 失败", ex);
            throw;
        }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // 先构建 DI 容器（MainWindow 构造时即从 App.Services 解析依赖）
        Services = ConfigureServices();

        m_window = new MainWindow();
        MainWindowInstance = m_window as MainWindow;
        m_window.Activate();
        Logger.Info("App", "MainWindow 已激活");
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // 基础设施
        services.AddSingleton<SettingsService>();
        services.AddSingleton<ThemeService>();

        // 百度引擎（接口 → 实现，未来可替换为其他引擎）
        services.AddSingleton<IBaiduTokenProvider, BaiduTokenProvider>();
        services.AddSingleton<IOcrEngine, BaiduOcrEngine>();
        services.AddSingleton<ITranslator, BaiduTranslator>();
        services.AddSingleton<ISpeechSynthesizer, BaiduSpeechSynthesizer>();

        // 平台服务
        services.AddSingleton<ScreenCaptureService>();
        services.AddSingleton<ClipboardService>();
        services.AddSingleton<WindowPlacementService>();
        services.AddSingleton<StartupService>();

        // ViewModels
        services.AddSingleton<MainViewModel>();

        return services.BuildServiceProvider();
    }
}
