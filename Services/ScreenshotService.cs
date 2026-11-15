using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EdgeBar.Views;

namespace EdgeBar.Services;

/// <summary>区域截图、全屏截图、窗口截图</summary>
public static class ScreenshotService
{
    private static bool _regionCapturing;

    public static void StartRegionCapture() => PrepareAndRun(() =>
    {
        if (_regionCapturing) return;
        _regionCapturing = true;

        var bounds = System.Windows.Forms.SystemInformation.VirtualScreen;
        Bitmap bitmap;
        try
        {
            bitmap = CaptureBitmap(bounds);
        }
        catch (Exception ex)
        {
            _regionCapturing = false;
            App.ShowToast("截图失败", ex.Message);
            return;
        }

        var form = new CaptureForm(bitmap, bounds);
        form.FormClosed += (_, _) =>
        {
            _regionCapturing = false;
            var image = form.SelectedImage;
            bitmap.Dispose();

            if (image == null) return;
            var editor = new ScreenshotEditorWindow(image);
            editor.Show();
            editor.Activate();
        };
        form.Show();
        form.Activate();
    });

    public static void CaptureFullScreen() => PrepareAndRun(() =>
    {
        var bitmap = CaptureVirtualScreen();
        new ScreenshotEditorWindow(bitmap).Show();
    });

    public static void CaptureWindow() => PrepareAndRun(() =>
    {
        var bitmap = CaptureForegroundWindow();
        new ScreenshotEditorWindow(bitmap).Show();
    });

    private static void PrepareAndRun(Action action)
    {
        if (App.Shell?.IsPanelExpanded == true)
            App.Shell.CollapseExternally();

        // 等贴边面板的收起动画结束，避免把自己截进去
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(280) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            try
            {
                action();
            }
            catch (Exception ex)
            {
                App.ShowToast("截图失败", ex.Message);
            }
        };
        timer.Start();
    }

    public static BitmapSource CaptureVirtualScreen()
    {
        var rect = System.Windows.Forms.SystemInformation.VirtualScreen;
        using var bitmap = CaptureBitmap(rect);
        return ToBitmapSource(bitmap);
    }

    private static Bitmap CaptureBitmap(Rectangle rect)
    {
        var bitmap = new Bitmap(rect.Width, rect.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0, bitmap.Size, CopyPixelOperation.SourceCopy);
        return bitmap;
    }

    public static BitmapSource CaptureForegroundWindow()
    {
        var handle = GetForegroundWindow();
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out var rect))
            return CaptureVirtualScreen();

        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;
        if (width < 40 || height < 40) return CaptureVirtualScreen();

        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0, bitmap.Size, CopyPixelOperation.SourceCopy);
        }
        return ToBitmapSource(bitmap);
    }

    private static BitmapSource ToBitmapSource(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        stream.Position = 0;
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
