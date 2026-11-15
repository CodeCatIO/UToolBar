using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using EdgeBar.Services;
using Rectangle = System.Drawing.Rectangle;

namespace EdgeBar.Views;

/// <summary>录屏选区旁的悬浮工具条，本身不会被录进画面</summary>
public partial class RecordToolbarWindow : Window
{
    private readonly List<MenuItem> _qualityItems = new();
    private readonly List<MenuItem> _speakerItems = new();
    private readonly List<MenuItem> _micItems = new();
    private int _quality;

    public event Action StartRequested;
    public event Action CancelRequested;

    public RecordFormat Format => FormatGif.IsChecked == true ? RecordFormat.Gif : RecordFormat.Mp4;
    public RecordQuality Quality => _quality == 1 ? RecordQuality.Hd : RecordQuality.Sd;
    public bool SystemAudio { get; private set; }
    public bool Microphone { get; private set; }

    public RecordToolbarWindow()
    {
        InitializeComponent();

        var settings = App.State?.Settings;
        _quality = settings?.RecordQuality ?? 0;
        SystemAudio = settings?.RecordSystemAudio ?? true;
        Microphone = settings?.RecordMicrophone ?? false;

        bool gif = (settings?.RecordFormat ?? 0) == 1;
        FormatGif.IsChecked = gif;
        FormatMp4.IsChecked = !gif;

        QualityText.Text = _quality == 1 ? "高清" : "标清";
        BuildMenus();
        UpdateAudioVisual();
    }

    private void BuildMenus()
    {
        var menuStyle = new Style(typeof(MenuItem));
        menuStyle.Setters.Add(new Setter(FontSizeProperty, 12.5));
        menuStyle.Setters.Add(new Setter(FontFamilyProperty, new FontFamily("Microsoft YaHei UI")));

        var qualityMenu = new ContextMenu { FontSize = 12.5 };
        qualityMenu.Resources.Add(typeof(MenuItem), menuStyle);

        foreach (var (label, value) in new[] { ("标清", 0), ("高清", 1) })
        {
            var item = new MenuItem { Header = label, IsCheckable = true, IsChecked = _quality == value };
            item.Click += (_, _) =>
            {
                _quality = value;
                QualityText.Text = label;
                foreach (var other in _qualityItems) other.IsChecked = other == item;
                SaveSetting(s => s.RecordQuality = value);
            };
            _qualityItems.Add(item);
            qualityMenu.Items.Add(item);
        }
        QualityButton.ContextMenu = qualityMenu;

        var speakerMenu = CreateAudioMenu("录制电脑声音", true, value =>
        {
            SystemAudio = value;
            foreach (var item in _speakerItems) item.IsChecked = (bool)item.Tag == value;
            SaveSetting(s => s.RecordSystemAudio = value);
            UpdateAudioVisual();
        }, _speakerItems);
        SpeakerButton.ContextMenu = speakerMenu;
        SpeakerMenuButton.ContextMenu = speakerMenu;

        var micMenu = CreateAudioMenu("录制麦克风", false, value =>
        {
            Microphone = value;
            foreach (var item in _micItems) item.IsChecked = (bool)item.Tag == value;
            SaveSetting(s => s.RecordMicrophone = value);
            UpdateAudioVisual();
        }, _micItems);
        MicButton.ContextMenu = micMenu;
        MicMenuButton.ContextMenu = micMenu;
    }

    private static ContextMenu CreateAudioMenu(string onLabel, bool onByDefault, Action<bool> changed, List<MenuItem> items)
    {
        var menu = new ContextMenu { FontSize = 12.5 };
        foreach (var (label, value) in new[] { ("不" + onLabel, false), (onLabel, true) })
        {
            var item = new MenuItem
            {
                Header = label,
                IsCheckable = true,
                IsChecked = value == onByDefault,
                Tag = value
            };
            item.Click += (_, _) => changed(value);
            items.Add(item);
            menu.Items.Add(item);
        }
        return menu;
    }

    private static void SaveSetting(Action<Models.AppSettings> apply)
    {
        if (App.State?.Settings == null) return;
        apply(App.State.Settings);
        App.State.Notify();
    }

    private void OnFormatClick(object sender, RoutedEventArgs e)
    {
        SaveSetting(s => s.RecordFormat = Format == RecordFormat.Gif ? 1 : 0);
        UpdateAudioVisual();
    }

    private void OnQualityClick(object sender, RoutedEventArgs e) => OpenMenu(QualityButton);

    private void OnSpeakerClick(object sender, RoutedEventArgs e)
    {
        if (Format == RecordFormat.Gif) return;
        OpenMenu(SpeakerButton);
    }

    private void OnMicClick(object sender, RoutedEventArgs e)
    {
        if (Format == RecordFormat.Gif) return;
        OpenMenu(MicButton);
    }

    private static void OpenMenu(Button button)
    {
        if (button.ContextMenu == null) return;
        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.Placement = PlacementMode.Bottom;
        button.ContextMenu.IsOpen = true;
    }

    private void UpdateAudioVisual()
    {
        bool gif = Format == RecordFormat.Gif;
        bool speakerOn = SystemAudio && !gif;
        bool micOn = Microphone && !gif;

        SpeakerButton.Foreground = ColorBrush(speakerOn ? "#4B5563" : "#C2C7CF");
        SpeakerMenuButton.Foreground = SpeakerButton.Foreground;
        MicButton.Foreground = ColorBrush(micOn ? "#4B5563" : "#C2C7CF");
        MicMenuButton.Foreground = MicButton.Foreground;

        SpeakerSlash.Visibility = speakerOn ? Visibility.Collapsed : Visibility.Visible;
        MicSlash.Visibility = micOn ? Visibility.Collapsed : Visibility.Visible;

        SpeakerHost.Opacity = gif ? 0.45 : 1;
        MicHost.Opacity = gif ? 0.45 : 1;
        SpeakerHost.ToolTip = gif ? "GIF 不支持声音" : null;
        MicHost.ToolTip = gif ? "GIF 不支持声音" : null;

        foreach (var item in _speakerItems) item.IsChecked = (bool)item.Tag == SystemAudio;
        foreach (var item in _micItems) item.IsChecked = (bool)item.Tag == Microphone;
    }

    private static System.Windows.Media.Brush ColorBrush(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }

    private void OnStartClick(object sender, RoutedEventArgs e) => StartRequested?.Invoke();

    private void OnExitClick(object sender, RoutedEventArgs e) => CancelRequested?.Invoke();

    /// <summary>跟随选区移动，优先放在选区下方，空间不足时放到上方</summary>
    public void MoveTo(Rectangle region, Rectangle monitor)
    {
        UpdateLayout();

        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget == null) return;

        var matrix = source.CompositionTarget.TransformFromDevice;
        var top = matrix.Transform(new System.Windows.Point(region.Left, region.Bottom + 10));
        var monitorTopLeft = matrix.Transform(new System.Windows.Point(monitor.Left, monitor.Top));
        var monitorBottomRight = matrix.Transform(new System.Windows.Point(monitor.Right, monitor.Bottom));

        double left = top.X;
        double upper = top.Y;

        if (upper + ActualHeight > monitorBottomRight.Y)
        {
            var above = matrix.Transform(new System.Windows.Point(region.Left, region.Top - 10));
            upper = above.Y - ActualHeight;
        }

        // 全屏选择时上下都没有空间，放到屏幕底部中间，避免盖住顶部手柄
        if (upper < monitorTopLeft.Y || upper + ActualHeight > monitorBottomRight.Y)
        {
            upper = monitorBottomRight.Y - ActualHeight - 16;
            left = (monitorTopLeft.X + monitorBottomRight.X - ActualWidth) / 2;
        }

        if (upper < monitorTopLeft.Y) upper = monitorTopLeft.Y;
        left = Math.Max(monitorTopLeft.X, Math.Min(left, monitorBottomRight.X - ActualWidth));

        Left = left - 14; // 外层 14 是阴影留白
        Top = upper - 14;
    }
}
