using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EdgeBar.Views;

public partial class PinWindow : Window
{
    private readonly double _aspect;

    public PinWindow(BitmapSource image)
    {
        InitializeComponent();
        Pic.Source = image;

        _aspect = image.PixelHeight / (double)image.PixelWidth;
        double scale = Math.Min(1.0, Math.Min(520.0 / image.PixelWidth, 420.0 / image.PixelHeight));
        Width = Math.Max(120, image.PixelWidth * scale);
        Height = Math.Max(80, image.PixelHeight * scale);

        var area = SystemParameters.WorkArea;
        Left = Math.Max(area.Left + 40, area.Right - Width - 120);
        Top = Math.Max(area.Top + 60, area.Top + 120);
    }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<Button>(e.OriginalSource as DependencyObject) != null) return;
        if (e.ButtonState != MouseButtonState.Pressed) return;
        try { DragMove(); } catch { }
    }

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            Opacity = Math.Clamp(Opacity + e.Delta / 2400.0, 0.25, 1.0);
        }
        else
        {
            double factor = e.Delta > 0 ? 1.1 : 0.9;
            double newWidth = Math.Clamp(Width * factor, 100, 1600);
            Width = newWidth;
            Height = newWidth * _aspect;
            if (Height < 60) Height = 60;
        }
        e.Handled = true;
    }

    private void OnMouseEnter(object sender, MouseEventArgs e) => ToolBar.Opacity = 1;

    private void OnMouseLeave(object sender, MouseEventArgs e) => ToolBar.Opacity = 0;

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (Pic.Source is BitmapSource image)
            {
                Clipboard.SetImage(image);
                App.ShowToast("已复制", "贴图已复制到剪贴板");
            }
        }
        catch
        {
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private static T FindAncestor<T>(DependencyObject current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T typed) return typed;
            current = current is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }
        return null;
    }
}
