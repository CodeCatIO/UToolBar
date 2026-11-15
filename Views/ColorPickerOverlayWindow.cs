using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EdgeBar.Views;

/// <summary>全屏取色：鼠标移动预览颜色，点击拾取，Esc 取消</summary>
public sealed class ColorPickerOverlayWindow : Window
{
    private readonly BitmapSource _screen;
    private readonly byte[] _pixels;
    private readonly int _stride;
    private readonly Action<Color?> _callback;
    private readonly Border _preview;
    private readonly TextBlock _hex;
    private readonly Border _tip;
    private readonly TranslateTransform _tipTransform = new();
    private bool _done;

    public ColorPickerOverlayWindow(BitmapSource screen, Action<Color?> callback)
    {
        _screen = screen;
        _callback = callback;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = false;
        Background = Brushes.Black;
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Cursor = Cursors.Cross;
        Focusable = true;
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        var converted = new FormatConvertedBitmap(screen, PixelFormats.Bgra32, null, 0);
        _stride = screen.PixelWidth * 4;
        _pixels = new byte[_stride * screen.PixelHeight];
        converted.CopyPixels(_pixels, _stride, 0);

        var root = new Grid();
        root.Children.Add(new Image { Source = screen, Stretch = Stretch.Fill });

        _preview = new Border
        {
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(5),
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(1)
        };
        _hex = new TextBlock
        {
            Text = "#FFFFFF",
            Foreground = Brushes.White,
            FontSize = 11,
            FontFamily = new FontFamily("Consolas"),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(_preview);
        panel.Children.Add(_hex);

        _tip = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(210, 0, 0, 0)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 5, 8, 5),
            Child = panel,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            RenderTransform = _tipTransform
        };
        root.Children.Add(_tip);

        var hint = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 6, 12, 6),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 24),
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = "点击屏幕取色 · Esc 取消",
                Foreground = Brushes.White,
                FontSize = 11
            }
        };
        root.Children.Add(hint);

        Content = root;

        MouseMove += OnMouseMove;
        MouseLeftButtonDown += OnPick;
        PreviewKeyDown += OnKeyDown;
        Loaded += (_, _) =>
        {
            Activate();
            Focus();
        };
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var point = e.GetPosition(this);
        var color = Pick(point);
        _preview.Background = new SolidColorBrush(color);
        _hex.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";

        double x = Math.Min(point.X + 18, Math.Max(0, ActualWidth - 130));
        double y = Math.Min(point.Y + 18, Math.Max(0, ActualHeight - 50));
        _tipTransform.X = x;
        _tipTransform.Y = y;
    }

    private Color Pick(Point point)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return Colors.White;

        int x = (int)Math.Floor(point.X / ActualWidth * _screen.PixelWidth);
        int y = (int)Math.Floor(point.Y / ActualHeight * _screen.PixelHeight);
        x = Math.Clamp(x, 0, _screen.PixelWidth - 1);
        y = Math.Clamp(y, 0, _screen.PixelHeight - 1);

        int i = y * _stride + x * 4;
        return Color.FromRgb(_pixels[i + 2], _pixels[i + 1], _pixels[i]);
    }

    private void OnPick(object sender, MouseButtonEventArgs e)
    {
        Finish(Pick(e.GetPosition(this)));
        e.Handled = true;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Finish(null);
            e.Handled = true;
        }
    }

    private void Finish(Color? color)
    {
        if (_done) return;
        _done = true;
        var callback = _callback;
        Close();
        callback?.Invoke(color);
    }
}
