using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace OcrTranslator.Views;

/// <summary>
/// OCR 识别等待弹窗 - 无边框置顶居中，Acrylic 玻璃背景（Glassmorphism 2.0）。
/// </summary>
public sealed partial class LoadingWindow : Window
{
    public LoadingWindow()
    {
        InitializeComponent();

        // Acrylic 系统背景
        SystemBackdrop = new DesktopAcrylicBackdrop();

        ExtendsContentIntoTitleBar = true;

        this.AppWindow.Resize(new SizeInt32(400, 340));
        CenterWindow();

        var presenter = this.AppWindow.Presenter as OverlappedPresenter;
        if (presenter != null)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsResizable = false;
        }
    }

    private void CenterWindow()
    {
        var displayArea = DisplayArea.GetFromPoint(new PointInt32(0, 0), DisplayAreaFallback.Primary);
        if (displayArea != null)
        {
            var workArea = displayArea.WorkArea;
            var windowSize = this.AppWindow.Size;
            int x = (workArea.Width - windowSize.Width) / 2 + workArea.X;
            int y = (workArea.Height - windowSize.Height) / 2 + workArea.Y;
            this.AppWindow.Move(new PointInt32(x, y));
        }
    }
}
