using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using EdgeBar.Models;

namespace EdgeBar.Views;

public partial class NoteWindow : Window
{
    private static readonly double[] OpacitySteps = { 1.0, 0.9, 0.8, 0.7, 0.55 };

    // 便签基准尺寸：最小缩到 85%，最大放大到 2 倍
    private const double BaseWidth = 264;
    private const double BaseHeight = 224;
    private const double MinScale = 0.85;
    private const double MaxScale = 2.0;

    // 字号跟着便签大小等比缩放：默认 16，最小 13.5，最大 25（内容超出时滚动查看）
    private const double BaseFontSize = 16;
    private const double MinFontSize = 13.5;
    private const double MaxFontSize = 25;

    private bool _resizing;
    private Point _resizeStart;
    private double _resizeStartWidth;
    private double _resizeStartHeight;

    public NoteData Data { get; }
    public string NoteId => Data.Id;

    public NoteWindow(NoteData data)
    {
        InitializeComponent();

        Data = data;
        DataContext = data;

        Width = Math.Clamp(data.Width <= 0 ? BaseWidth : data.Width, BaseWidth * MinScale, BaseWidth * MaxScale);
        Height = Math.Clamp(data.Height <= 0 ? BaseHeight : data.Height, BaseHeight * MinScale, BaseHeight * MaxScale);
        Left = data.Left;
        Top = data.Top;
        Topmost = data.IsPinned;
        PinBtn.IsChecked = data.IsPinned;

        data.PropertyChanged += OnDataChanged;

        Loaded += OnLoaded;
        LocationChanged += (_, _) =>
        {
            if (!IsLoaded) return;
            Data.Left = Left;
            Data.Top = Top;
        };
        SizeChanged += (_, _) =>
        {
            if (!IsLoaded) return;
            Data.Width = Width;
            Data.Height = Height;
            Dispatcher.BeginInvoke(new Action(UpdateAutoFont), System.Windows.Threading.DispatcherPriority.Loaded);
        };
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ClampToScreen();
        UpdateColorSelection();
        UpdateAutoFont();
        BodyBox.Focus();
        BodyBox.CaretIndex = BodyBox.Text?.Length ?? 0;
    }

    private void ClampToScreen()
    {
        var area = SystemParameters.WorkArea;
        bool outside = Data.Left < area.Left - 40 ||
                       Data.Left > area.Right - 80 ||
                       Data.Top < area.Top - 40 ||
                       Data.Top > area.Bottom - 80;
        if (outside)
        {
            Left = Math.Max(area.Left + 20, area.Right - Width - 70);
            Top = Math.Max(area.Top + 60, area.Top + 120);
        }
    }

    private void OnDataChanged(object sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(NoteData.IsPinned):
                Topmost = Data.IsPinned;
                break;
            case nameof(NoteData.ColorHex):
                UpdateColorSelection();
                break;
            case nameof(NoteData.Opacity):
                OpacityText.Text = Data.OpacityPercentText;
                break;
        }
    }

    /// <summary>字号只跟便签大小等比缩放：放大变大字，缩小变小字，与内容多少无关</summary>
    private void UpdateAutoFont()
    {
        double width = ActualWidth > 0 ? ActualWidth : BaseWidth;
        double height = ActualHeight > 0 ? ActualHeight : BaseHeight;
        double scale = Math.Sqrt(width * height / (BaseWidth * BaseHeight));
        double size = Math.Clamp(BaseFontSize * scale, MinFontSize, MaxFontSize);
        BodyBox.FontSize = Math.Round(size * 2) / 2;
    }

    private void OnResizeGripDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        _resizing = true;
        _resizeStart = e.GetPosition(this);
        _resizeStartWidth = Width;
        _resizeStartHeight = Height;
        ((UIElement)sender).CaptureMouse();
        e.Handled = true;
    }

    private void OnResizeGripMove(object sender, MouseEventArgs e)
    {
        if (!_resizing) return;
        var point = e.GetPosition(this);
        Width = Math.Clamp(_resizeStartWidth + (point.X - _resizeStart.X), BaseWidth * MinScale, BaseWidth * MaxScale);
        Height = Math.Clamp(_resizeStartHeight + (point.Y - _resizeStart.Y), BaseHeight * MinScale, BaseHeight * MaxScale);
    }

    private void OnResizeGripUp(object sender, MouseButtonEventArgs e)
    {
        if (!_resizing) return;
        _resizing = false;
        ((UIElement)sender).ReleaseMouseCapture();
    }

    private void OnResizeGripLostCapture(object sender, MouseEventArgs e)
    {
        _resizing = false;
    }

    private void UpdateColorSelection()
    {
        foreach (var ellipse in ColorPanel.Children.OfType<Ellipse>())
        {
            ellipse.Stroke = string.Equals(ellipse.Tag as string, Data.ColorHex, StringComparison.OrdinalIgnoreCase)
                ? new SolidColorBrush(Color.FromArgb(140, 20, 20, 20))
                : Brushes.Transparent;
        }
    }

    private void OnHeaderDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
        try
        {
            DragMove();
        }
        catch
        {
        }
    }

    private void OnColorClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is Ellipse ellipse && ellipse.Tag is string hex)
        {
            Data.ColorHex = hex;
        }
    }

    private void OnOpacityClick(object sender, RoutedEventArgs e)
    {
        int index = 0;
        double best = double.MaxValue;
        for (int i = 0; i < OpacitySteps.Length; i++)
        {
            double diff = Math.Abs(OpacitySteps[i] - Data.Opacity);
            if (diff < best)
            {
                best = diff;
                index = i;
            }
        }
        Data.Opacity = OpacitySteps[(index + 1) % OpacitySteps.Length];
        OpacityText.Text = Data.OpacityPercentText;
    }

    private void OnPinChanged(object sender, RoutedEventArgs e)
    {
        Data.IsPinned = PinBtn.IsChecked == true;
        Topmost = Data.IsPinned;
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnConvertClick(object sender, RoutedEventArgs e)
    {
        var text = (Data.Text ?? "").Trim();
        if (text.Length == 0)
        {
            App.ShowToast("便签是空的", "先写点内容再转成待办");
            return;
        }

        var firstLine = text.Split('\n')[0].Trim();
        if (firstLine.Length > 40) firstLine = firstLine[..40] + "…";

        App.State.Todos.Add(new TodoItem
        {
            Title = firstLine,
            Date = DateTime.Today
        });
        App.State.Notify();
        App.ShowToast("已加入今日待办", firstLine);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
        }
    }
}
