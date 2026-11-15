using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using UToolBar.Models;
using UToolBar.Services;
using Microsoft.Win32;

namespace UToolBar.Views;

public partial class ScreenshotEditorWindow : Window
{
    private enum Tool { Select, Line, Pen, Rect, Text, Mosaic, Eraser }
    private enum EraseMode { Edit, Original }
    private enum LineMode { Arrow, Straight }
    private enum RectStyle { Outline, Fill }
    private enum StrokeKind { None, BaseErase, EditErase, Mosaic }

    private sealed class HistoryEntry
    {
        public Action Undo;
        public Action Redo;
    }

    private static readonly double[] Sizes = { 2, 3.5, 5, 7, 10, 14 };

    private static readonly Color[] PaletteColors =
    {
        Color.FromRgb(0xE0, 0x5A, 0x55),
        Color.FromRgb(0xF0, 0x8A, 0x3C),
        Color.FromRgb(0xF2, 0xC0, 0x4C),
        Color.FromRgb(0x3F, 0xA8, 0x6A),
        Color.FromRgb(0x36, 0xA8, 0xC8),
        Color.FromRgb(0x42, 0x7B, 0xD0),
        Color.FromRgb(0x8B, 0x6B, 0xD6),
        Color.FromRgb(0xE0, 0x6B, 0xA8),
        Color.FromRgb(0x33, 0x33, 0x33),
        Color.FromRgb(0x80, 0x80, 0x80),
        Color.FromRgb(0xD8, 0xD8, 0xD8),
        Colors.White
    };

    private readonly BitmapSource _source;
    private readonly BitmapSource _bgra;
    private readonly List<HistoryEntry> _undo = new();
    private readonly List<HistoryEntry> _redo = new();

    private Tool _tool = Tool.Line;
    private EraseMode _eraseMode = EraseMode.Edit;
    private LineMode _lineMode = LineMode.Arrow;
    private RectStyle _rectStyle = RectStyle.Outline;
    private Color _rgb = Color.FromRgb(0xE0, 0x5A, 0x55);
    private byte _alpha = 255;
    private double _size = Sizes[2];
    private int _sizeIndex = 2;
    private readonly List<Button> _sizeButtons = new();

    private double _hue;
    private double _sat;
    private double _val;
    private bool _updatingPicker = true;

    private double _zoom = 1;
    private double _fitScale = 1;
    private bool _manualZoom;

    private Point _start;
    private bool _drawing;
    private UIElement _current;
    private Point _lastPoint;
    private bool _hasLastPoint;

    private StrokeKind _strokeKind;
    private List<Point> _strokePoints;
    private int _strokeRadius;
    private byte[] _strokeBaseBefore;
    private byte[] _strokeMaskBefore;
    private byte[] _strokeMosaicBefore;
    private bool[] _strokeMosaicDoneBefore;
    private bool _strokeChanged;

    private TextBox _textEditor;
    private Border _editingText;
    private bool _committingText;
    private Border _dragText;
    private Point _dragStart;
    private double _dragOriginLeft;
    private double _dragOriginTop;

    private WriteableBitmap _baseBitmap;
    private Image _baseImage;
    private byte[] _basePixels;
    private byte[] _originalPixels;

    private WriteableBitmap _mosaicBitmap;
    private Image _mosaicImage;
    private byte[] _mosaicPixels;
    private bool[] _mosaicDone;
    private const int MosaicBlock = 12;
    private int _mosaicCols;
    private int _mosaicRows;

    private WriteableBitmap _maskBitmap;
    private byte[] _maskPixels;
    private ImageBrush _maskBrush;
    private Canvas _annotationLayer;

    private UIElement _selected;
    private TranslateTransform _selectedTransform;
    private bool _selecting;
    private Point _selectStart;
    private double _selectOriginX;
    private double _selectOriginY;

    private bool _panning;
    private Point _panStart;
    private double _panOffsetX;
    private double _panOffsetY;

    private int _pixelWidth;
    private int _pixelHeight;
    private int _stride;

    public ScreenshotEditorWindow(BitmapSource source)
    {
        InitializeComponent();

        _source = source;
        SizeInfo.Text = $"{source.PixelWidth} × {source.PixelHeight}";

        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        converted.Freeze();
        _bgra = converted;

        _pixelWidth = source.PixelWidth;
        _pixelHeight = source.PixelHeight;
        _stride = _pixelWidth * 4;

        Artboard.Width = _pixelWidth;
        Artboard.Height = _pixelHeight;
        Surface.Width = _pixelWidth;
        Surface.Height = _pixelHeight;
        CursorCanvas.Width = _pixelWidth;
        CursorCanvas.Height = _pixelHeight;

        _basePixels = new byte[_stride * _pixelHeight];
        _bgra.CopyPixels(_basePixels, _stride, 0);
        for (int i = 3; i < _basePixels.Length; i += 4)
            _basePixels[i] = 255;
        _originalPixels = (byte[])_basePixels.Clone();

        _baseBitmap = new WriteableBitmap(_pixelWidth, _pixelHeight, 96, 96, PixelFormats.Bgra32, null);
        WriteFull(_baseBitmap, _basePixels);
        _baseImage = new Image
        {
            Source = _baseBitmap,
            Width = _pixelWidth,
            Height = _pixelHeight,
            Stretch = Stretch.Fill,
            IsHitTestVisible = false
        };
        Surface.Children.Add(_baseImage);

        _mosaicPixels = new byte[_stride * _pixelHeight];
        _mosaicBitmap = new WriteableBitmap(_pixelWidth, _pixelHeight, 96, 96, PixelFormats.Bgra32, null);
        _mosaicImage = new Image
        {
            Source = _mosaicBitmap,
            Width = _pixelWidth,
            Height = _pixelHeight,
            Stretch = Stretch.Fill,
            IsHitTestVisible = false
        };
        Surface.Children.Add(_mosaicImage);

        _mosaicCols = (_pixelWidth + MosaicBlock - 1) / MosaicBlock;
        _mosaicRows = (_pixelHeight + MosaicBlock - 1) / MosaicBlock;
        _mosaicDone = new bool[_mosaicCols * _mosaicRows];

        _maskPixels = new byte[_stride * _pixelHeight];
        for (int i = 0; i < _maskPixels.Length; i += 4)
        {
            _maskPixels[i] = 255;
            _maskPixels[i + 1] = 255;
            _maskPixels[i + 2] = 255;
            _maskPixels[i + 3] = 255;
        }
        _maskBitmap = new WriteableBitmap(_pixelWidth, _pixelHeight, 96, 96, PixelFormats.Bgra32, null);
        WriteFull(_maskBitmap, _maskPixels);
        _maskBrush = new ImageBrush(_maskBitmap)
        {
            Stretch = Stretch.Fill,
            AlignmentX = AlignmentX.Left,
            AlignmentY = AlignmentY.Top,
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, _pixelWidth, _pixelHeight),
            ViewportUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, _pixelWidth, _pixelHeight),
            TileMode = TileMode.None
        };

        _annotationLayer = new Canvas
        {
            Width = _pixelWidth,
            Height = _pixelHeight,
            Background = null,
            OpacityMask = _maskBrush
        };
        Surface.Children.Add(_annotationLayer);

        BuildSizePicker();
        BuildPalette();
        SetColor(_rgb, _alpha, updatePicker: true);
        SelectTool(Tool.Line);

        Loaded += (_, _) =>
        {
            UpdateFit();
            Dispatcher.BeginInvoke(new Action(UpdateFit), DispatcherPriority.Loaded);
        };
        SizeChanged += (_, _) => UpdateFit();
    }

    // ================= 缩放 / 光标 =================

    private double FitScale()
    {
        if (Viewport.ActualWidth < 20 || Viewport.ActualHeight < 20) return 1;
        double w = Math.Max(40, Viewport.ActualWidth - 26);
        double h = Math.Max(40, Viewport.ActualHeight - 26);
        return Math.Min(2.0, Math.Min(w / _pixelWidth, h / _pixelHeight));
    }

    private void UpdateFit()
    {
        _fitScale = FitScale();
        if (!_manualZoom) _zoom = _fitScale;
        ApplyZoom();
    }

    private void ApplyZoom()
    {
        Artboard.LayoutTransform = new ScaleTransform(_zoom, _zoom);
        UpdateCursorVisual();
        UpdateSelectionVisual();
    }

    private void OnViewportWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control)
        {
            e.Handled = true;
            return;
        }

        var mouseInViewport = e.GetPosition(Viewport);
        var pointInImage = e.GetPosition(Artboard);

        double factor = e.Delta > 0 ? 1.1 : 1 / 1.1;
        _zoom = Math.Clamp(_zoom * factor, 0.1, 4.0);
        _manualZoom = true;
        ApplyZoom();
        e.Handled = true;

        // 以光标为中心缩放：缩放后把光标下的图像点滚回原来的屏幕位置
        Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                var newPoint = Artboard.TranslatePoint(pointInImage, Viewport);
                Viewport.ScrollToHorizontalOffset(Viewport.HorizontalOffset + newPoint.X - mouseInViewport.X);
                Viewport.ScrollToVerticalOffset(Viewport.VerticalOffset + newPoint.Y - mouseInViewport.Y);
                UpdateSelectionVisual();
            }
            catch
            {
            }
        }), DispatcherPriority.Loaded);
    }

    private void OnPanStart(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Right) return;
        _panning = true;
        _panStart = e.GetPosition(Viewport);
        _panOffsetX = Viewport.HorizontalOffset;
        _panOffsetY = Viewport.VerticalOffset;
        Viewport.Cursor = Cursors.Hand;
        Surface.Cursor = Cursors.Hand;
        Viewport.CaptureMouse();
        e.Handled = true;
    }

    private void OnPanMove(object sender, MouseEventArgs e)
    {
        if (!_panning) return;
        var point = e.GetPosition(Viewport);
        Viewport.ScrollToHorizontalOffset(_panOffsetX - (point.X - _panStart.X));
        Viewport.ScrollToVerticalOffset(_panOffsetY - (point.Y - _panStart.Y));
        e.Handled = true;
    }

    private void OnPanEnd(object sender, MouseButtonEventArgs e)
    {
        if (!_panning) return;
        _panning = false;
        Viewport.ReleaseMouseCapture();
        Viewport.Cursor = Cursors.Arrow;
        Surface.Cursor = CursorForTool();
        e.Handled = true;
    }

    /// <summary>画笔、马赛克、橡皮只显示圆圈，不要十字准星</summary>
    private Cursor CursorForTool() => _tool switch
    {
        Tool.Text => Cursors.IBeam,
        Tool.Select => Cursors.Arrow,
        Tool.Pen or Tool.Mosaic or Tool.Eraser => Cursors.None,
        _ => Cursors.Cross
    };

    private double EraserRadius => Math.Max(3, _size * 1.8);

    private double MosaicRadius => Math.Max(8, _size * 2.6);

    private double CursorRadius => _tool switch
    {
        Tool.Pen => Math.Max(2, _size / 2 + 1),
        Tool.Mosaic => MosaicRadius,
        Tool.Eraser => EraserRadius,
        _ => 0
    };

    private void UpdateCursorVisual()
    {
        if (CursorHalo == null) return;
        if (_tool is Tool.Select or Tool.Line or Tool.Rect or Tool.Text)
        {
            CursorHalo.Visibility = Visibility.Collapsed;
            CursorRing.Visibility = Visibility.Collapsed;
            return;
        }

        double radius = CursorRadius;
        double diameter = Math.Max(4, radius * 2);
        CursorHalo.Width = CursorRing.Width = diameter;
        CursorHalo.Height = CursorRing.Height = diameter;
        Canvas.SetLeft(CursorHalo, _lastPoint.X - radius);
        Canvas.SetTop(CursorHalo, _lastPoint.Y - radius);
        Canvas.SetLeft(CursorRing, _lastPoint.X - radius);
        Canvas.SetTop(CursorRing, _lastPoint.Y - radius);
        CursorHalo.StrokeThickness = 3 / _zoom;
        CursorRing.StrokeThickness = 1 / _zoom;
        CursorHalo.Visibility = CursorRing.Visibility = _hasLastPoint ? Visibility.Visible : Visibility.Collapsed;
    }

    private void MoveCursor(Point point)
    {
        _lastPoint = point;

        double cursorRadius = CursorRadius;
        if (cursorRadius <= 0)
        {
            _hasLastPoint = false;
            CursorHalo.Visibility = CursorRing.Visibility = Visibility.Collapsed;
            return;
        }

        _hasLastPoint = true;
        CursorHalo.Visibility = CursorRing.Visibility = Visibility.Visible;
        Canvas.SetLeft(CursorHalo, point.X - cursorRadius);
        Canvas.SetTop(CursorHalo, point.Y - cursorRadius);
        Canvas.SetLeft(CursorRing, point.X - cursorRadius);
        Canvas.SetTop(CursorRing, point.Y - cursorRadius);
    }

    private void OnSurfaceMouseLeave(object sender, MouseEventArgs e)
    {
        _hasLastPoint = false;
        CursorHalo.Visibility = CursorRing.Visibility = Visibility.Collapsed;
    }

    // ================= 颜色 =================

    private void BuildSizePicker()
    {
        for (int i = 0; i < Sizes.Length; i++)
        {
            double ring = 10 + i * 4;
            var ringBorder = new Border
            {
                Width = ring,
                Height = ring,
                CornerRadius = new CornerRadius(ring / 2),
                BorderThickness = new Thickness(1.6),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x9A, 0xA7, 0xB6)),
                Background = Brushes.Transparent,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var button = new Button
            {
                Width = 32,
                Height = 32,
                Margin = new Thickness(0, 0, 2, 0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                FocusVisualStyle = null,
                Content = ringBorder,
                Tag = i,
                ToolTip = $"{Sizes[i]:0.#} px"
            };
            button.Template = BuildRingButtonTemplate();
            button.Click += OnSizePick;
            _sizeButtons.Add(button);
            SizePanel.Children.Add(button);
        }
        UpdateSizeButtons();
    }

    private static ControlTemplate BuildRingButtonTemplate()
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        template.VisualTree = border;
        return template;
    }

    private void OnSizePick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: int index }) return;
        _sizeIndex = Math.Clamp(index, 0, Sizes.Length - 1);
        _size = Sizes[_sizeIndex];
        UpdateSizeButtons();
        SizeValue.Text = $"{_size:0.#}";
        UpdateCursorVisual();
    }

    private void UpdateSizeButtons()
    {
        for (int i = 0; i < _sizeButtons.Count; i++)
        {
            if (_sizeButtons[i].Content is Border ring)
            {
                bool selected = i == _sizeIndex;
                ring.BorderBrush = new SolidColorBrush(selected ? Color.FromRgb(0x42, 0x7B, 0xD0) : Color.FromRgb(0x9A, 0xA7, 0xB6));
                ring.BorderThickness = new Thickness(selected ? 2.2 : 1.4);
            }
        }
    }

    private void BuildPalette()
    {
        foreach (var color in PaletteColors)
        {
            var swatch = new Border
            {
                Width = 22,
                Height = 22,
                Margin = new Thickness(2),
                CornerRadius = new CornerRadius(5),
                Background = new SolidColorBrush(color),
                BorderBrush = new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                Tag = color
            };
            swatch.MouseLeftButtonUp += (_, _) =>
            {
                if (swatch.Tag is Color c) SetColor(c, 255, true);
            };
            PalettePanel.Children.Add(swatch);
        }
    }

    private Brush StrokeBrush() => new SolidColorBrush(Color.FromArgb(_alpha, _rgb.R, _rgb.G, _rgb.B));

    private void SetColor(Color color, byte alpha, bool updatePicker)
    {
        _rgb = color;
        _alpha = alpha;
        var brush = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
        ColorSwatch.Background = brush;
        ColorHexText.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        AlphaGradient.Fill = new LinearGradientBrush(
            Color.FromArgb(0, color.R, color.G, color.B),
            Color.FromArgb(255, color.R, color.G, color.B),
            0);

        if (updatePicker) SyncPickerFromColor();
        if (_textEditor != null) _textEditor.Foreground = StrokeBrush();
    }

    private void UpdateColorFromPicker()
    {
        var color = HsvToRgb(_hue, _sat, _val);
        SetColor(color, (byte)Math.Round(AlphaSlider.Value), updatePicker: false);
    }

    private void SyncPickerFromColor()
    {
        _updatingPicker = true;
        var (h, s, v) = RgbToHsv(_rgb);
        _hue = h;
        _sat = s;
        _val = v;

        HueSlider.Value = h;
        AlphaSlider.Value = _alpha;
        RSlider.Value = _rgb.R;
        GSlider.Value = _rgb.G;
        BSlider.Value = _rgb.B;
        ASlider.Value = _alpha;
        SvHueLayer.Fill = new SolidColorBrush(HsvToRgb(h, 1, 1));
        SvMarker.Margin = new Thickness(Math.Clamp(s * SvArea.Width, 0, SvArea.Width) - 5,
                                        Math.Clamp((1 - v) * SvArea.Height, 0, SvArea.Height) - 5, 0, 0);
        RBox.Text = _rgb.R.ToString();
        GBox.Text = _rgb.G.ToString();
        BBox.Text = _rgb.B.ToString();
        ABox.Text = _alpha.ToString();
        HexBox.Text = $"#{_rgb.R:X2}{_rgb.G:X2}{_rgb.B:X2}";
        AlphaGradient.Fill = new LinearGradientBrush(
            Color.FromArgb(0, _rgb.R, _rgb.G, _rgb.B),
            Color.FromArgb(255, _rgb.R, _rgb.G, _rgb.B),
            0);
        _updatingPicker = false;
    }

    private void ClosePopups()
    {
        LinePopup.IsOpen = false;
        RectPopup.IsOpen = false;
        EraserPopup.IsOpen = false;
        ColorPopup.IsOpen = false;
    }

    private void OnColorButtonClick(object sender, RoutedEventArgs e)
    {
        bool open = !ColorPopup.IsOpen;
        ClosePopups();
        if (!open) return;

        SyncPickerFromColor();
        Dispatcher.BeginInvoke(new Action(() => ColorPopup.IsOpen = true), DispatcherPriority.Input);
    }

    private void OnSvDown(object sender, MouseButtonEventArgs e)
    {
        SvArea.CaptureMouse();
        SetSv(e.GetPosition(SvArea));
    }

    private void OnSvMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || !SvArea.IsMouseCaptured) return;
        SetSv(e.GetPosition(SvArea));
    }

    private void OnSvUp(object sender, MouseButtonEventArgs e)
    {
        if (SvArea.IsMouseCaptured) SvArea.ReleaseMouseCapture();
    }

    private void SetSv(Point point)
    {
        _sat = Math.Clamp(point.X / SvArea.Width, 0, 1);
        _val = 1 - Math.Clamp(point.Y / SvArea.Height, 0, 1);
        _updatingPicker = true;
        SvMarker.Margin = new Thickness(point.X - 5, point.Y - 5, 0, 0);
        var color = HsvToRgb(_hue, _sat, _val);
        SetColor(color, (byte)Math.Round(AlphaSlider.Value), updatePicker: false);
        RSlider.Value = color.R;
        GSlider.Value = color.G;
        BSlider.Value = color.B;
        RBox.Text = color.R.ToString();
        GBox.Text = color.G.ToString();
        BBox.Text = color.B.ToString();
        HexBox.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        _updatingPicker = false;
    }

    private void OnHueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingPicker || RSlider == null) return;
        _hue = HueSlider.Value;
        SvHueLayer.Fill = new SolidColorBrush(HsvToRgb(_hue, 1, 1));
        UpdateColorFromPicker();
        _updatingPicker = true;
        RSlider.Value = _rgb.R;
        GSlider.Value = _rgb.G;
        BSlider.Value = _rgb.B;
        RBox.Text = _rgb.R.ToString();
        GBox.Text = _rgb.G.ToString();
        BBox.Text = _rgb.B.ToString();
        HexBox.Text = $"#{_rgb.R:X2}{_rgb.G:X2}{_rgb.B:X2}";
        _updatingPicker = false;
    }

    private void OnAlphaChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingPicker || ABox == null) return;
        _alpha = (byte)Math.Round(AlphaSlider.Value);
        SetColor(_rgb, _alpha, updatePicker: false);
        _updatingPicker = true;
        ASlider.Value = _alpha;
        ABox.Text = _alpha.ToString();
        _updatingPicker = false;
    }

    private void OnChannelChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingPicker || RBox == null) return;
        ApplyFromRgbSliders();
    }

    private void ApplyFromRgbSliders()
    {
        _rgb = Color.FromRgb((byte)Math.Round(RSlider.Value), (byte)Math.Round(GSlider.Value), (byte)Math.Round(BSlider.Value));
        _alpha = (byte)Math.Round(ASlider.Value);
        var (h, s, v) = RgbToHsv(_rgb);
        _hue = h; _sat = s; _val = v;

        _updatingPicker = true;
        SetColor(_rgb, _alpha, updatePicker: false);
        HueSlider.Value = h;
        SvHueLayer.Fill = new SolidColorBrush(HsvToRgb(h, 1, 1));
        SvMarker.Margin = new Thickness(Math.Clamp(s * SvArea.Width, 0, SvArea.Width) - 5,
                                        Math.Clamp((1 - v) * SvArea.Height, 0, SvArea.Height) - 5, 0, 0);
        RBox.Text = _rgb.R.ToString();
        GBox.Text = _rgb.G.ToString();
        BBox.Text = _rgb.B.ToString();
        ABox.Text = _alpha.ToString();
        HexBox.Text = $"#{_rgb.R:X2}{_rgb.G:X2}{_rgb.B:X2}";
        _updatingPicker = false;
    }

    private void OnRgbaChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingPicker) return;
        if (!byte.TryParse(RBox.Text, out byte r) || !byte.TryParse(GBox.Text, out byte g) ||
            !byte.TryParse(BBox.Text, out byte b) || !byte.TryParse(ABox.Text, out byte a))
            return;

        _updatingPicker = true;
        RSlider.Value = r;
        GSlider.Value = g;
        BSlider.Value = b;
        ASlider.Value = a;
        _updatingPicker = false;
        ApplyFromRgbSliders();
    }

    private void OnHexKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) ApplyHex();
    }

    private void OnHexLostFocus(object sender, RoutedEventArgs e) => ApplyHex();

    private void ApplyHex()
    {
        if (_updatingPicker) return;
        string text = (HexBox.Text ?? "").Trim().TrimStart('#');
        try
        {
            if (text.Length == 6)
            {
                byte r = Convert.ToByte(text.Substring(0, 2), 16);
                byte g = Convert.ToByte(text.Substring(2, 2), 16);
                byte b = Convert.ToByte(text.Substring(4, 2), 16);
                SetColor(Color.FromRgb(r, g, b), _alpha, true);
            }
            else if (text.Length == 8)
            {
                byte a = Convert.ToByte(text.Substring(0, 2), 16);
                byte r = Convert.ToByte(text.Substring(2, 2), 16);
                byte g = Convert.ToByte(text.Substring(4, 2), 16);
                byte b = Convert.ToByte(text.Substring(6, 2), 16);
                SetColor(Color.FromRgb(r, g, b), a, true);
            }
        }
        catch
        {
            SyncPickerFromColor();
        }
    }

    private void OnEyedropperClick(object sender, RoutedEventArgs e)
    {
        ColorPopup.IsOpen = false;
        Hide();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(260) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            try
            {
                var screen = ScreenshotService.CaptureVirtualScreen();
                var picker = new ColorPickerOverlayWindow(screen, color =>
                {
                    if (color.HasValue) SetColor(color.Value, 255, true);
                    Show();
                    Activate();
                });
                picker.Show();
            }
            catch
            {
                Show();
                Activate();
            }
        };
        timer.Start();
    }

    private static Color HsvToRgb(double h, double s, double v)
    {
        h = ((h % 360) + 360) % 360;
        double c = v * s;
        double x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        double m = v - c;
        double r = 0, g = 0, b = 0;
        if (h < 60) { r = c; g = x; }
        else if (h < 120) { r = x; g = c; }
        else if (h < 180) { g = c; b = x; }
        else if (h < 240) { g = x; b = c; }
        else if (h < 300) { r = x; b = c; }
        else { r = c; b = x; }
        return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }

    private static (double h, double s, double v) RgbToHsv(Color color)
    {
        double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;
        double h = 0;
        if (delta > 0.0001)
        {
            if (max == r) h = 60 * (((g - b) / delta) % 6);
            else if (max == g) h = 60 * ((b - r) / delta + 2);
            else h = 60 * ((r - g) / delta + 4);
        }
        if (h < 0) h += 360;
        double s = max <= 0 ? 0 : delta / max;
        return (h, s, max);
    }

    // ================= 工具 =================

    private void OnToolClick(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { Tag: string tag }) return;
        ClosePopups();
        if (_textEditor != null) CommitTextEditor();
        SelectTool(tag switch
        {
            "select" => Tool.Select,
            "pen" => Tool.Pen,
            "text" => Tool.Text,
            "mosaic" => Tool.Mosaic,
            _ => Tool.Line
        });
    }

    private void OnLineDropClick(object sender, RoutedEventArgs e)
    {
        ClosePopups();
        if (_textEditor != null) CommitTextEditor();
        SelectTool(Tool.Line);
        Dispatcher.BeginInvoke(new Action(() => LinePopup.IsOpen = true), DispatcherPriority.Input);
    }

    private void OnRectDropClick(object sender, RoutedEventArgs e)
    {
        ClosePopups();
        if (_textEditor != null) CommitTextEditor();
        SelectTool(Tool.Rect);
        Dispatcher.BeginInvoke(new Action(() => RectPopup.IsOpen = true), DispatcherPriority.Input);
    }

    private void OnLineArrowClick(object sender, RoutedEventArgs e)
    {
        _lineMode = LineMode.Arrow;
        LineIcon.Text = "→";
        LineLabel.Text = "箭头";
        LinePopup.IsOpen = false;
        SelectTool(Tool.Line);
    }

    private void OnLineStraightClick(object sender, RoutedEventArgs e)
    {
        _lineMode = LineMode.Straight;
        LineIcon.Text = "—";
        LineLabel.Text = "直线";
        LinePopup.IsOpen = false;
        SelectTool(Tool.Line);
    }

    private void OnRectStyleClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag })
            _rectStyle = tag == "1" ? RectStyle.Fill : RectStyle.Outline;

        RectIcon.Text = _rectStyle == RectStyle.Fill ? "▬" : "▭";
        RectPopup.IsOpen = false;
        SelectTool(Tool.Rect);
    }

    private void OnEraserDropClick(object sender, RoutedEventArgs e)
    {
        ClosePopups();
        if (_textEditor != null) CommitTextEditor();
        SelectTool(Tool.Eraser);
        Dispatcher.BeginInvoke(new Action(() => EraserPopup.IsOpen = true), DispatcherPriority.Input);
    }

    private void OnEraserModeOptionClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag })
            _eraseMode = tag == "original" ? EraseMode.Original : EraseMode.Edit;

        EraserLabel.Text = _eraseMode == EraseMode.Original ? "原图橡皮" : "编辑橡皮";
        EraserPopup.IsOpen = false;
        SelectTool(Tool.Eraser);
    }

    private void SelectTool(Tool tool)
    {
        _tool = tool;
        ToolSelect.IsChecked = tool == Tool.Select;
        DropLine.IsChecked = tool == Tool.Line;
        ToolPen.IsChecked = tool == Tool.Pen;
        DropRect.IsChecked = tool == Tool.Rect;
        ToolText.IsChecked = tool == Tool.Text;
        ToolMosaic.IsChecked = tool == Tool.Mosaic;
        DropEraser.IsChecked = tool == Tool.Eraser;

        Surface.Cursor = CursorForTool();

        if (tool != Tool.Select) SelectElement(null);

        UpdateHint();
        UpdateCursorVisual();
        _hasLastPoint = false;
    }

    private void UpdateHint()
    {
        HintText.Text = _tool switch
        {
            Tool.Select => "点击箭头、直线、矩形、文字可选中并拖动；马赛克和画笔不可选。右键拖拽可平移画布",
            Tool.Line => _lineMode == LineMode.Arrow ? "拖动鼠标画箭头" : "拖动鼠标画直线",
            Tool.Pen => "按住左键自由绘制",
            Tool.Rect => _rectStyle == RectStyle.Fill ? "拖动鼠标画实心矩形" : "拖动鼠标画空心矩形",
            Tool.Text => "点一下输入文字；双击已有文字可再次编辑，拖动可移动，右下角圆点可缩放",
            Tool.Mosaic => "按住左键涂抹马赛克，圆圈就是笔刷范围",
            Tool.Eraser => _eraseMode == EraseMode.Edit
                ? "编辑橡皮：按住左键擦掉标注和马赛克"
                : "原图橡皮：按住左键擦掉原图像素，露出透明网格",
            _ => ""
        };
    }

    // ================= 选择工具 =================

    private void SelectElement(UIElement element)
    {
        _selected = element;
        UpdateSelectionVisual();
    }

    private void UpdateSelectionVisual()
    {
        if (SelectionRect == null) return;
        if (_selected == null || _tool != Tool.Select)
        {
            SelectionRect.Visibility = Visibility.Collapsed;
            return;
        }

        Rect bounds;
        if (_selected is Path path && path.Data != null)
        {
            var transform = path.TransformToAncestor(_annotationLayer);
            bounds = transform.TransformBounds(path.Data.Bounds);
        }
        else if (_selected is FrameworkElement element)
        {
            var transform = element.TransformToAncestor(_annotationLayer);
            bounds = transform.TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        }
        else
        {
            SelectionRect.Visibility = Visibility.Collapsed;
            return;
        }

        bounds.Inflate(4, 4);
        Canvas.SetLeft(SelectionRect, bounds.X);
        Canvas.SetTop(SelectionRect, bounds.Y);
        SelectionRect.Width = Math.Max(1, bounds.Width);
        SelectionRect.Height = Math.Max(1, bounds.Height);
        SelectionRect.StrokeThickness = 1.2 / _zoom;
        SelectionRect.Visibility = Visibility.Visible;
    }

    private UIElement FindSelectable(Point point)
    {
        for (int i = _annotationLayer.Children.Count - 1; i >= 0; i--)
        {
            var element = _annotationLayer.Children[i];
            if (element is Polyline) continue; // 画笔不可选

            if (element is Border || element is Rectangle)
            {
                if (element is FrameworkElement fe)
                {
                    var transform = fe.TransformToAncestor(_annotationLayer);
                    var bounds = transform.TransformBounds(new Rect(0, 0, fe.ActualWidth, fe.ActualHeight));
                    if (bounds.Contains(point)) return element;
                }
                continue;
            }

            if (element is Path path && path.Data != null)
            {
                var transform = path.TransformToAncestor(_annotationLayer);
                var inverse = transform.Inverse;
                Point local = inverse != null ? inverse.Transform(point) : point;
                if (path.Data.FillContains(local)) return path;
                var pen = new Pen(Brushes.Black, Math.Max(8, path.StrokeThickness + 8));
                if (path.Data.StrokeContains(pen, local)) return path;
            }
        }
        return null;
    }

    // ================= 绘制 =================

    private void OnSurfaceMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_textEditor != null)
        {
            CommitTextEditor();
            return;
        }

        var point = e.GetPosition(Surface);
        MoveCursor(point);

        if (_tool == Tool.Select)
        {
            var hit = FindSelectable(point);
            SelectElement(hit);
            if (hit != null)
            {
                _selecting = true;
                _selectStart = point;
                _selectedTransform = hit.RenderTransform as TranslateTransform;
                if (_selectedTransform == null || _selectedTransform.IsFrozen)
                {
                    _selectedTransform = new TranslateTransform();
                    hit.RenderTransform = _selectedTransform;
                }
                _selectOriginX = _selectedTransform.X;
                _selectOriginY = _selectedTransform.Y;
                Surface.CaptureMouse();
            }
            return;
        }

        if (_tool == Tool.Text)
        {
            ShowTextEditor(point, null);
            return;
        }

        _start = point;
        _drawing = true;
        _hasLastPoint = true;
        _lastPoint = point;
        Surface.CaptureMouse();

        switch (_tool)
        {
            case Tool.Mosaic:
                BeginRasterStroke(StrokeKind.Mosaic, point);
                return;

            case Tool.Eraser:
                BeginRasterStroke(_eraseMode == EraseMode.Original ? StrokeKind.BaseErase : StrokeKind.EditErase, point);
                return;

            case Tool.Line:
            {
                var path = new Path
                {
                    Stroke = StrokeBrush(),
                    Fill = _lineMode == LineMode.Arrow ? StrokeBrush() : Brushes.Transparent,
                    StrokeThickness = _size,
                    StrokeLineJoin = PenLineJoin.Round,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    IsHitTestVisible = false
                };
                _current = path;
                _annotationLayer.Children.Add(path);
                break;
            }

            case Tool.Pen:
            {
                var line = new Polyline
                {
                    Stroke = StrokeBrush(),
                    StrokeThickness = _size,
                    StrokeLineJoin = PenLineJoin.Round,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    IsHitTestVisible = false
                };
                line.Points.Add(point);
                _current = line;
                _annotationLayer.Children.Add(line);
                break;
            }

            case Tool.Rect:
            {
                var rect = new Rectangle
                {
                    Stroke = StrokeBrush(),
                    StrokeThickness = Math.Max(1, _size),
                    Fill = IsFill ? StrokeBrush() : Brushes.Transparent,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(rect, point.X);
                Canvas.SetTop(rect, point.Y);
                _current = rect;
                _annotationLayer.Children.Add(rect);
                break;
            }
        }
    }

    private bool IsFill => _rectStyle == RectStyle.Fill;

    private void OnSurfaceMouseMove(object sender, MouseEventArgs e)
    {
        var point = e.GetPosition(Surface);
        MoveCursor(point);

        if (_tool == Tool.Select)
        {
            if (_selecting && _selected != null && _selectedTransform != null)
            {
                var delta = point - _selectStart;
                _selectedTransform.X = _selectOriginX + delta.X;
                _selectedTransform.Y = _selectOriginY + delta.Y;
                UpdateSelectionVisual();
            }
            return;
        }

        if (!_drawing) return;

        if (_strokeKind != StrokeKind.None)
        {
            ExtendRasterStroke(point);
            return;
        }

        if (_current == null) return;

        switch (_tool)
        {
            case Tool.Line when _current is Path path:
                UpdateLine(path, _start, point);
                break;

            case Tool.Pen when _current is Polyline line:
                line.Points.Add(point);
                break;

            case Tool.Rect when _current is Rectangle rect:
                double x = Math.Min(_start.X, point.X);
                double y = Math.Min(_start.Y, point.Y);
                Canvas.SetLeft(rect, x);
                Canvas.SetTop(rect, y);
                rect.Width = Math.Abs(point.X - _start.X);
                rect.Height = Math.Abs(point.Y - _start.Y);
                break;
        }
    }

    private void OnSurfaceMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_tool == Tool.Select)
        {
            if (_selecting)
            {
                _selecting = false;
                Surface.ReleaseMouseCapture();
            }
            return;
        }

        if (!_drawing) return;
        _drawing = false;
        Surface.ReleaseMouseCapture();

        if (_strokeKind != StrokeKind.None)
        {
            EndRasterStroke();
            return;
        }

        var element = _current;
        _current = null;
        if (element == null) return;

        bool tooSmall = element switch
        {
            Rectangle r => r.Width < 3 || r.Height < 3,
            _ => false
        };
        if (tooSmall)
        {
            _annotationLayer.Children.Remove(element);
            return;
        }

        int index = _annotationLayer.Children.IndexOf(element);
        PushHistory(
            () => _annotationLayer.Children.Remove(element),
            () => { if (!_annotationLayer.Children.Contains(element)) _annotationLayer.Children.Insert(Math.Min(index, _annotationLayer.Children.Count), element); });
    }

    private static void UpdateLine(Path path, Point from, Point to)
    {
        var delta = to - from;
        double length = delta.Length;
        if (length < 1) return;

        double thickness = path.StrokeThickness;
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(from, false, false);
            context.LineTo(to, true, false);

            if (path.Fill != Brushes.Transparent && path.Fill != null)
            {
                double ux = delta.X / length;
                double uy = delta.Y / length;
                double headLength = Math.Min(Math.Max(10, thickness * 5), Math.Max(12, length * 0.45));
                double headWidth = Math.Max(4, thickness * 2.2);
                var back = new Point(to.X - ux * headLength, to.Y - uy * headLength);
                var left = new Point(back.X - uy * headWidth, back.Y + ux * headWidth);
                var right = new Point(back.X + uy * headWidth, back.Y - ux * headWidth);
                context.BeginFigure(to, true, true);
                context.LineTo(left, true, false);
                context.LineTo(right, true, false);
            }
        }
        geometry.Freeze();
        path.Data = geometry;
    }

    // ================= 位图笔刷 =================

    private void BeginRasterStroke(StrokeKind kind, Point point)
    {
        _strokeKind = kind;
        _strokePoints = new List<Point> { point };
        _strokeRadius = (int)Math.Round(kind == StrokeKind.Mosaic ? MosaicRadius : EraserRadius);
        _strokeChanged = false;

        switch (kind)
        {
            case StrokeKind.BaseErase:
                _strokeBaseBefore = (byte[])_basePixels.Clone();
                break;
            case StrokeKind.EditErase:
                _strokeMaskBefore = (byte[])_maskPixels.Clone();
                _strokeMosaicBefore = (byte[])_mosaicPixels.Clone();
                break;
            case StrokeKind.Mosaic:
                _strokeMosaicBefore = (byte[])_mosaicPixels.Clone();
                _strokeMosaicDoneBefore = (bool[])_mosaicDone.Clone();
                break;
        }

        ApplyRasterAt(point);
        _lastPoint = point;
    }

    private void ExtendRasterStroke(Point point)
    {
        if (_strokeKind == StrokeKind.None) return;
        var from = _lastPoint;
        double distance = (point - from).Length;
        int steps = Math.Max(1, (int)Math.Ceiling(distance / Math.Max(2, _strokeRadius * 0.55)));
        for (int i = 1; i <= steps; i++)
        {
            var p = from + (point - from) * (i / (double)steps);
            _strokePoints.Add(p);
            ApplyRasterAt(p);
        }
        _lastPoint = point;
    }

    private void ApplyRasterAt(Point point)
    {
        switch (_strokeKind)
        {
            case StrokeKind.BaseErase:
                EraseBaseCircle(point, _strokeRadius);
                break;
            case StrokeKind.EditErase:
                EraseEditCircle(point, _strokeRadius);
                break;
            case StrokeKind.Mosaic:
                ApplyMosaicCircle(point, _strokeRadius);
                break;
        }
    }

    private void EndRasterStroke()
    {
        var kind = _strokeKind;
        var points = _strokePoints ?? new List<Point>();
        int radius = _strokeRadius;
        _strokeKind = StrokeKind.None;
        _strokePoints = null;

        if (!_strokeChanged)
        {
            _strokeBaseBefore = null;
            _strokeMaskBefore = null;
            _strokeMosaicBefore = null;
            _strokeMosaicDoneBefore = null;
            return;
        }

        switch (kind)
        {
            case StrokeKind.BaseErase:
            {
                var before = _strokeBaseBefore;
                PushHistory(
                    () => { _basePixels = before; WriteFull(_baseBitmap, _basePixels); },
                    () => ReplayRaster(StrokeKind.BaseErase, points, radius));
                break;
            }
            case StrokeKind.EditErase:
            {
                var maskBefore = _strokeMaskBefore;
                var mosaicBefore = _strokeMosaicBefore;
                PushHistory(
                    () =>
                    {
                        _maskPixels = maskBefore;
                        _mosaicPixels = mosaicBefore;
                        WriteFull(_maskBitmap, _maskPixels);
                        WriteFull(_mosaicBitmap, _mosaicPixels);
                    },
                    () => ReplayRaster(StrokeKind.EditErase, points, radius));
                break;
            }
            case StrokeKind.Mosaic:
            {
                var mosaicBefore = _strokeMosaicBefore;
                var doneBefore = _strokeMosaicDoneBefore;
                PushHistory(
                    () =>
                    {
                        _mosaicPixels = mosaicBefore;
                        _mosaicDone = doneBefore;
                        WriteFull(_mosaicBitmap, _mosaicPixels);
                    },
                    () => ReplayRaster(StrokeKind.Mosaic, points, radius));
                break;
            }
        }

        _strokeBaseBefore = null;
        _strokeMaskBefore = null;
        _strokeMosaicBefore = null;
        _strokeMosaicDoneBefore = null;
    }

    private void ReplayRaster(StrokeKind kind, List<Point> points, int radius)
    {
        _strokeRadius = radius;
        foreach (var point in points)
        {
            switch (kind)
            {
                case StrokeKind.BaseErase: EraseBaseCircle(point, radius); break;
                case StrokeKind.EditErase: EraseEditCircle(point, radius); break;
                case StrokeKind.Mosaic: ApplyMosaicCircle(point, radius); break;
            }
        }
    }

    private void EraseBaseCircle(Point center, int radius)
    {
        if (!TryCircleBounds(center, radius, out int x0, out int y0, out int x1, out int y1)) return;
        int r2 = radius * radius;
        bool changed = false;

        for (int y = y0; y <= y1; y++)
        {
            int row = y * _stride;
            int dy = y - (int)center.Y;
            for (int x = x0; x <= x1; x++)
            {
                int dx = x - (int)center.X;
                if (dx * dx + dy * dy > r2) continue;
                int i = row + x * 4;
                if (_basePixels[i + 3] == 0) continue;
                _basePixels[i] = _basePixels[i + 1] = _basePixels[i + 2] = _basePixels[i + 3] = 0;
                changed = true;
            }
        }

        if (!changed) return;
        WriteRegion(_baseBitmap, _basePixels, x0, y0, x1, y1);
        _strokeChanged = true;
    }

    private void EraseEditCircle(Point center, int radius)
    {
        if (!TryCircleBounds(center, radius, out int x0, out int y0, out int x1, out int y1)) return;
        int r2 = radius * radius;
        bool changed = false;

        for (int y = y0; y <= y1; y++)
        {
            int row = y * _stride;
            int dy = y - (int)center.Y;
            for (int x = x0; x <= x1; x++)
            {
                int dx = x - (int)center.X;
                if (dx * dx + dy * dy > r2) continue;
                int i = row + x * 4;
                if (_maskPixels[i + 3] != 0)
                {
                    _maskPixels[i + 3] = 0;
                    changed = true;
                }
                if (_mosaicPixels[i + 3] != 0)
                {
                    _mosaicPixels[i] = _mosaicPixels[i + 1] = _mosaicPixels[i + 2] = _mosaicPixels[i + 3] = 0;
                    changed = true;
                }
            }
        }

        if (!changed) return;
        WriteRegion(_maskBitmap, _maskPixels, x0, y0, x1, y1);
        WriteRegion(_mosaicBitmap, _mosaicPixels, x0, y0, x1, y1);
        _strokeChanged = true;
    }

    private void ApplyMosaicCircle(Point center, int radius)
    {
        int block = MosaicBlock;
        int reach = radius + block;
        int bx0 = Math.Max(0, ((int)center.X - reach) / block);
        int by0 = Math.Max(0, ((int)center.Y - reach) / block);
        int bx1 = Math.Min(_mosaicCols - 1, ((int)center.X + reach) / block);
        int by1 = Math.Min(_mosaicRows - 1, ((int)center.Y + reach) / block);
        bool changed = false;

        for (int by = by0; by <= by1; by++)
        {
            for (int bx = bx0; bx <= bx1; bx++)
            {
                int index = by * _mosaicCols + bx;
                if (_mosaicDone[index]) continue;

                double cx = bx * block + block / 2.0;
                double cy = by * block + block / 2.0;
                double dx = cx - center.X;
                double dy = cy - center.Y;
                if (dx * dx + dy * dy > (radius + block * 0.7) * (radius + block * 0.7)) continue;

                int x0 = bx * block;
                int y0 = by * block;
                int x1 = Math.Min(x0 + block, _pixelWidth);
                int y1 = Math.Min(y0 + block, _pixelHeight);
                FillMosaicBlock(x0, y0, x1, y1);
                _mosaicDone[index] = true;
                changed = true;
            }
        }

        if (!changed) return;
        int rx0 = Math.Max(0, bx0 * block);
        int ry0 = Math.Max(0, by0 * block);
        int rx1 = Math.Min(_pixelWidth - 1, (bx1 + 1) * block - 1);
        int ry1 = Math.Min(_pixelHeight - 1, (by1 + 1) * block - 1);
        WriteRegion(_mosaicBitmap, _mosaicPixels, rx0, ry0, rx1, ry1);
        _strokeChanged = true;
    }

    private void FillMosaicBlock(int x0, int y0, int x1, int y1)
    {
        long b = 0, g = 0, r = 0;
        int count = 0;
        for (int y = y0; y < y1; y++)
        {
            int row = y * _stride;
            for (int x = x0; x < x1; x++)
            {
                int i = row + x * 4;
                b += _originalPixels[i];
                g += _originalPixels[i + 1];
                r += _originalPixels[i + 2];
                count++;
            }
        }
        if (count == 0) return;

        byte ab = (byte)(b / count);
        byte ag = (byte)(g / count);
        byte ar = (byte)(r / count);

        for (int y = y0; y < y1; y++)
        {
            int row = y * _stride;
            for (int x = x0; x < x1; x++)
            {
                int i = row + x * 4;
                _mosaicPixels[i] = ab;
                _mosaicPixels[i + 1] = ag;
                _mosaicPixels[i + 2] = ar;
                _mosaicPixels[i + 3] = 255;
            }
        }
    }

    private bool TryCircleBounds(Point center, int radius, out int x0, out int y0, out int x1, out int y1)
    {
        int cx = (int)Math.Round(center.X);
        int cy = (int)Math.Round(center.Y);
        x0 = Math.Max(0, cx - radius);
        y0 = Math.Max(0, cy - radius);
        x1 = Math.Min(_pixelWidth - 1, cx + radius);
        y1 = Math.Min(_pixelHeight - 1, cy + radius);
        return x1 >= x0 && y1 >= y0;
    }

    private void WriteRegion(WriteableBitmap bitmap, byte[] pixels, int x0, int y0, int x1, int y1)
    {
        int w = x1 - x0 + 1;
        int h = y1 - y0 + 1;
        if (w <= 0 || h <= 0) return;
        var buffer = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            Buffer.BlockCopy(pixels, (y0 + y) * _stride + x0 * 4, buffer, y * w * 4, w * 4);
        bitmap.WritePixels(new Int32Rect(x0, y0, w, h), buffer, w * 4, 0);
    }

    private void WriteFull(WriteableBitmap bitmap, byte[] pixels)
    {
        bitmap.WritePixels(new Int32Rect(0, 0, _pixelWidth, _pixelHeight), pixels, _stride, 0);
    }

    // ================= 文字标注 =================

    private void ShowTextEditor(Point point, Border existing)
    {
        if (_textEditor != null) CommitTextEditor();

        _editingText = existing;
        double left = point.X;
        double top = point.Y;
        double fontSize = Math.Clamp(10 + _size * 2.2, 14, 42);
        string text = "";

        if (existing != null)
        {
            left = Canvas.GetLeft(existing);
            top = Canvas.GetTop(existing);
            if (double.IsNaN(left)) left = 0;
            if (double.IsNaN(top)) top = 0;
            var block = FindTextBlock(existing);
            if (block != null)
            {
                fontSize = block.FontSize;
                text = block.Text;
            }
            existing.Visibility = Visibility.Collapsed;
        }

        var box = new TextBox
        {
            Text = text,
            MinWidth = 140,
            MaxWidth = 480,
            FontSize = fontSize,
            FontWeight = FontWeights.SemiBold,
            Foreground = StrokeBrush(),
            Background = new SolidColorBrush(Color.FromArgb(238, 255, 255, 255)),
            BorderBrush = StrokeBrush(),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(3),
            AcceptsReturn = false
        };
        Canvas.SetLeft(box, left);
        Canvas.SetTop(box, top);
        Surface.Children.Add(box);
        _textEditor = box;

        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                CommitTextEditor();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                CancelTextEditor();
                e.Handled = true;
            }
        };
        box.LostKeyboardFocus += (_, _) =>
        {
            if (_textEditor == box) CommitTextEditor();
        };

        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_textEditor != box) return;
            box.Focus();
            Keyboard.Focus(box);
            box.CaretIndex = box.Text.Length;
        }), DispatcherPriority.Input);
    }

    private void CommitTextEditor()
    {
        if (_textEditor == null || _committingText) return;
        _committingText = true;

        var box = _textEditor;
        _textEditor = null;
        string text = box.Text;
        double left = Canvas.GetLeft(box);
        double top = Canvas.GetTop(box);
        double fontSize = box.FontSize;
        Surface.Children.Remove(box);

        if (_editingText != null)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                _annotationLayer.Children.Remove(_editingText);
            }
            else
            {
                var block = FindTextBlock(_editingText);
                if (block != null)
                {
                    block.Text = text;
                    block.FontSize = fontSize;
                }
                _editingText.Visibility = Visibility.Visible;
            }
            _editingText = null;
        }
        else if (!string.IsNullOrWhiteSpace(text))
        {
            var annotation = CreateTextAnnotation(text, fontSize, left, top);
            int index = _annotationLayer.Children.IndexOf(annotation);
            PushHistory(
                () => _annotationLayer.Children.Remove(annotation),
                () => { if (!_annotationLayer.Children.Contains(annotation)) _annotationLayer.Children.Insert(Math.Min(index, _annotationLayer.Children.Count), annotation); });
        }

        _committingText = false;
    }

    private void CancelTextEditor()
    {
        if (_textEditor != null)
        {
            Surface.Children.Remove(_textEditor);
            _textEditor = null;
        }
        if (_editingText != null)
        {
            _editingText.Visibility = Visibility.Visible;
            _editingText = null;
        }
    }

    private Border CreateTextAnnotation(string text, double fontSize, double left, double top)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            FontWeight = FontWeights.SemiBold,
            Foreground = StrokeBrush(),
            TextWrapping = TextWrapping.NoWrap
        };

        var thumb = new Thumb
        {
            Width = 12,
            Height = 12,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Cursor = Cursors.SizeNWSE,
            Margin = new Thickness(0, 0, 0, 0),
            Template = BuildThumbTemplate()
        };
        thumb.DragDelta += (_, e) =>
        {
            block.FontSize = Math.Clamp(block.FontSize + (e.VerticalChange + e.HorizontalChange) * 0.35, 8, 96);
            e.Handled = true;
        };

        var grid = new Grid();
        grid.Children.Add(block);
        grid.Children.Add(thumb);

        var container = new Border
        {
            Child = grid,
            Background = Brushes.Transparent,
            BorderBrush = new SolidColorBrush(Color.FromArgb(80, 0x42, 0x7B, 0xD0)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(3),
            Cursor = Cursors.SizeAll,
            Tag = "text"
        };
        Canvas.SetLeft(container, left);
        Canvas.SetTop(container, top);

        container.MouseLeftButtonDown += (_, e) =>
        {
            if (_tool == Tool.Select) SelectElement(container);

            if (e.ClickCount == 2)
            {
                ShowTextEditor(new Point(Canvas.GetLeft(container), Canvas.GetTop(container)), container);
                e.Handled = true;
                return;
            }

            _dragText = container;
            _dragStart = e.GetPosition(Surface);
            _dragOriginLeft = Canvas.GetLeft(container);
            _dragOriginTop = Canvas.GetTop(container);
            container.CaptureMouse();
            e.Handled = true;
        };
        container.MouseMove += (_, e) =>
        {
            if (_dragText != container || e.LeftButton != MouseButtonState.Pressed) return;
            var point = e.GetPosition(Surface);
            Canvas.SetLeft(container, _dragOriginLeft + point.X - _dragStart.X);
            Canvas.SetTop(container, _dragOriginTop + point.Y - _dragStart.Y);
            UpdateSelectionVisual();
            e.Handled = true;
        };
        container.MouseLeftButtonUp += (_, e) =>
        {
            if (_dragText == container)
            {
                _dragText = null;
                container.ReleaseMouseCapture();
                UpdateSelectionVisual();
                e.Handled = true;
            }
        };

        _annotationLayer.Children.Add(container);
        return container;
    }

    private static TextBlock FindTextBlock(Border container)
    {
        if (container.Child is Grid grid)
        {
            foreach (var child in grid.Children)
                if (child is TextBlock block) return block;
        }
        return null;
    }

    private static ControlTemplate BuildThumbTemplate()
    {
        var template = new ControlTemplate(typeof(Thumb));
        var factory = new FrameworkElementFactory(typeof(Ellipse));
        factory.SetValue(Shape.FillProperty, Brushes.White);
        factory.SetValue(Shape.StrokeProperty, new SolidColorBrush(Color.FromRgb(0x42, 0x7B, 0xD0)));
        factory.SetValue(Shape.StrokeThicknessProperty, 1.5);
        factory.SetValue(WidthProperty, 10.0);
        factory.SetValue(HeightProperty, 10.0);
        template.VisualTree = factory;
        return template;
    }

    // ================= 清空 / 撤销 =================

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        if (_textEditor != null) CommitTextEditor();

        var elements = new List<(UIElement Element, int Index)>();
        foreach (UIElement child in _annotationLayer.Children)
            elements.Add((child, _annotationLayer.Children.IndexOf(child)));

        byte[] maskBefore = (byte[])_maskPixels.Clone();
        byte[] mosaicBefore = (byte[])_mosaicPixels.Clone();
        bool[] doneBefore = (bool[])_mosaicDone.Clone();

        ClearDrawings();

        PushHistory(
            () =>
            {
                foreach (var (element, index) in elements.OrderBy(x => x.Index))
                {
                    if (!_annotationLayer.Children.Contains(element))
                        _annotationLayer.Children.Insert(Math.Min(index, _annotationLayer.Children.Count), element);
                }
                _maskPixels = maskBefore;
                _mosaicPixels = mosaicBefore;
                _mosaicDone = doneBefore;
                WriteFull(_maskBitmap, _maskPixels);
                WriteFull(_mosaicBitmap, _mosaicPixels);
            },
            () => ClearDrawings());
    }

    private void ClearDrawings()
    {
        var remove = new List<UIElement>();
        foreach (UIElement child in _annotationLayer.Children)
            remove.Add(child);
        foreach (var child in remove) _annotationLayer.Children.Remove(child);

        for (int i = 0; i < _maskPixels.Length; i += 4)
        {
            _maskPixels[i] = 255;
            _maskPixels[i + 1] = 255;
            _maskPixels[i + 2] = 255;
            _maskPixels[i + 3] = 255;
        }
        Array.Clear(_mosaicPixels, 0, _mosaicPixels.Length);
        Array.Clear(_mosaicDone, 0, _mosaicDone.Length);
        WriteFull(_maskBitmap, _maskPixels);
        WriteFull(_mosaicBitmap, _mosaicPixels);
    }

    private void PushHistory(Action undo, Action redo)
    {
        _undo.Add(new HistoryEntry { Undo = undo, Redo = redo });
        _redo.Clear();
        while (_undo.Count > 12) _undo.RemoveAt(0);
    }

    private void OnUndoClick(object sender, RoutedEventArgs e)
    {
        if (_textEditor != null)
        {
            CancelTextEditor();
            return;
        }
        if (_undo.Count == 0) return;

        var entry = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        entry.Undo?.Invoke();
        _redo.Add(entry);
    }

    private void OnRedoClick(object sender, RoutedEventArgs e)
    {
        if (_textEditor != null) CommitTextEditor();
        if (_redo.Count == 0) return;

        var entry = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        entry.Redo?.Invoke();
        _undo.Add(entry);
    }

    // ================= 输出 =================

    private BitmapSource RenderSurface()
    {
        if (_textEditor != null) CommitTextEditor();

        var savedTransform = Artboard.LayoutTransform;
        Artboard.LayoutTransform = Transform.Identity;
        Surface.Measure(new Size(_pixelWidth, _pixelHeight));
        Surface.Arrange(new Rect(0, 0, _pixelWidth, _pixelHeight));
        Surface.UpdateLayout();

        var bitmap = new RenderTargetBitmap(_pixelWidth, _pixelHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(Surface);
        bitmap.Freeze();

        Artboard.LayoutTransform = savedTransform;
        return bitmap;
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetImage(RenderSurface());
            App.ShowToast("已复制", "截图已复制到剪贴板");
            Close();
        }
        catch (Exception ex)
        {
            App.ShowToast("复制失败", ex.Message);
        }
    }

    private string ScreenshotDirectory()
    {
        string custom = App.State?.Settings.ScreenshotFolder;
        if (!string.IsNullOrWhiteSpace(custom)) return custom;
        return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "UTB截图");
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        try
        {
            string folder = ScreenshotDirectory();
            System.IO.Directory.CreateDirectory(folder);

            var dialog = new SaveFileDialog
            {
                Filter = "PNG 图片|*.png",
                DefaultExt = ".png",
                AddExtension = true,
                FileName = $"截图_{DateTime.Now:yyyyMMdd_HHmmss}.png",
                InitialDirectory = folder,
                Title = "保存截图"
            };
            if (dialog.ShowDialog(this) != true) return;

            var bitmap = RenderSurface();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = System.IO.File.Create(dialog.FileName);
            encoder.Save(stream);
            App.ShowToast("已保存", dialog.FileName);
        }
        catch (Exception ex)
        {
            App.ShowToast("保存失败", ex.Message);
        }
    }

    private void OnPinClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var bitmap = RenderSurface();
            var pin = new PinWindow(bitmap);
            pin.Show();
            Close();
        }
        catch (Exception ex)
        {
            App.ShowToast("贴图失败", ex.Message);
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnHeaderDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
        try { DragMove(); } catch { }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_textEditor != null && _textEditor.IsKeyboardFocusWithin) return;

        if (e.Key == Key.Escape)
        {
            Close();
            return;
        }

        if ((e.Key == Key.Delete || e.Key == Key.Back) && _selected != null)
        {
            var element = _selected;
            int index = _annotationLayer.Children.IndexOf(element);
            _annotationLayer.Children.Remove(element);
            SelectElement(null);
            PushHistory(
                () => { if (!_annotationLayer.Children.Contains(element)) _annotationLayer.Children.Insert(Math.Min(index, _annotationLayer.Children.Count), element); },
                () => _annotationLayer.Children.Remove(element));
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers != ModifierKeys.Control) return;

        switch (e.Key)
        {
            case Key.Z:
                OnUndoClick(null, null);
                e.Handled = true;
                break;
            case Key.Y:
                OnRedoClick(null, null);
                e.Handled = true;
                break;
            case Key.C:
                OnCopyClick(null, null);
                e.Handled = true;
                break;
            case Key.T:
                OnPinClick(null, null);
                e.Handled = true;
                break;
            case Key.S:
                OnSaveClick(null, null);
                e.Handled = true;
                break;
        }
    }
}
