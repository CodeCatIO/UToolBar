using System.Windows;
using System.Windows.Controls;
using UToolBar.Services;

namespace UToolBar.Views;

public partial class ScreenshotView : UserControl
{
    public ScreenshotView()
    {
        InitializeComponent();
    }

    private void OnRegionClick(object sender, RoutedEventArgs e) => ScreenshotService.StartRegionCapture();

    private void OnFullScreenClick(object sender, RoutedEventArgs e) => ScreenshotService.CaptureFullScreen();

    private void OnWindowClick(object sender, RoutedEventArgs e) => ScreenshotService.CaptureWindow();

    private void OnRecordClick(object sender, RoutedEventArgs e) => RecordingUI.StartSelection();
}
