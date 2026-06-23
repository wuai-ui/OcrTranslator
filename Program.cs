using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using OcrTranslator.Services;

namespace OcrTranslator;

public static class Program
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    private static extern int Win32MessageBox(IntPtr hWnd, string lpText, string lpCaption, uint uType);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int ShellExecute(IntPtr hWnd, string lpOperation, string lpFile, string? lpParameters, string? lpDirectory, int nShowCmd);

    private const int SW_SHOWNORMAL = 1;

    [STAThread]
    static void Main(string[] args)
    {
        try
        {
            // Step 1: 初始化 COM 互操作（WinUI 3 必需）
            WinRT.ComWrappersSupport.InitializeComWrappers();

            // Step 2: Bootstrap 初始化
            // 版本号编码: (major << 16) | minor → WinAppSDK 1.7 = 0x00010007
            bool initialized = false;
            int hr = 0;
            try
            {
                initialized = Microsoft.Windows.ApplicationModel.DynamicDependency
                    .Bootstrap.TryInitialize(0x00010007, out hr);
            }
            catch (Exception ex)
            {
                Logger.Error("Program", "Bootstrap 异常", ex);
            }

            if (!initialized)
            {
                string errorMsg = hr switch
                {
                    unchecked((int)0x80670000) => "未找到 Windows App SDK 框架",
                    unchecked((int)0x80670016) => "未安装 Windows App SDK 1.7 运行时",
                    _ => $"未知错误 (HRESULT=0x{hr:X8})"
                };
                Win32MessageBox(IntPtr.Zero,
                    $"启动失败：{errorMsg}\n\n" +
                    "本程序运行需要以下依赖（点击「确定」后将自动打开下载页）：\n" +
                    "  1. Windows App SDK 1.7 Runtime\n" +
                    "  2. .NET 10.0 Desktop Runtime\n\n" +
                    $"日志: {Logger.GetLogDirectory()}",
                    "极客 OCR v4 - 缺少运行时依赖", 0x30); // MB_ICONWARNING
                // 自动打开 Windows App SDK 下载页
                ShellExecute(IntPtr.Zero, "open", "https://aka.ms/windowsappsdk/1.7/latest", null, null, SW_SHOWNORMAL);
                return;
            }

            // Step 3: 启动 WinUI 应用
            Application.Start((p) =>
            {
                try
                {
                    var dq = DispatcherQueue.GetForCurrentThread();
                    SynchronizationContext.SetSynchronizationContext(
                        new DispatcherQueueSynchronizationContext(dq));
                    new App();
                }
                catch (Exception ex)
                {
                    Logger.Error("Program", "App 初始化失败", ex);
                    Win32MessageBox(IntPtr.Zero,
                        $"应用初始化失败:\n{ex.GetType().Name}: {ex.Message}\n\n日志:\n{Logger.GetLogDirectory()}",
                        "极客 OCR v4 - 错误", 0x10);
                }
            });
        }
        catch (Exception ex)
        {
            Logger.Error("Program", "启动失败", ex);
            Win32MessageBox(IntPtr.Zero,
                $"启动失败:\n{ex.GetType().Name}\n{ex.Message}\n\n日志:\n{Logger.GetLogDirectory()}",
                "极客 OCR v4 - 致命错误", 0x10);
        }
    }
}
