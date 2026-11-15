using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using UToolBar.Models;
using UToolBar.Services;
using UToolBar.Views;
using WinForms = System.Windows.Forms;

namespace UToolBar;

public partial class MainWindow : Window
{
    private static readonly (int Index, string Name, string Icon)[] Tools =
    {
        (0, "待办", "IconTodo"),
        (1, "纪念日", "IconGift"),
        (2, "计算器", "IconCalc"),
        (3, "万年历", "IconCalendar"),
        (4, "计薪", "IconWallet"),
        (5, "便签", "IconNote"),
        (6, "截图", "IconCamera"),
        (7, "剪贴板", "IconClipboard")
    };

    private readonly Dictionary<int, UIElement> _pages = new();
    private readonly Type[] _pageTypes =
    {
        typeof(TodoView), typeof(AnniversaryView), typeof(CalculatorView), typeof(CalendarView),
        typeof(SalaryView), typeof(NotesView), typeof(ScreenshotView), typeof(ClipboardView)
    };
    private readonly string[] _moduleTitles =
    {
        "UTB", "纪念日", "计算器", "万年历", "牛马计薪", "便利签", "截图贴图", "剪贴板"
    };

    private HotkeyService _hotkeys;

    private bool _isExpanded;
    private bool _settingsOpen;
    private bool _allowClose;
    private int _activeModule;
    private Rect _workArea;

    public MainWindow()
    {
        InitializeComponent();

        DataContext = App.State;

        SettingsPage.DataContext = App.State;
        SettingsPage.BackRequested += (_, _) => HideSettings();

        ApplyTheme(App.State.Settings.Theme, false);

        SourceInitialized += (_, _) =>
        {
            RefreshWorkArea();
            PositionWindow();

            var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            source?.AddHook(WndProc);

            _hotkeys = new HotkeyService();
            _hotkeys.Attach(this);
            _hotkeys.NewNoteRequested += () => App.CreateNote();
            _hotkeys.ScreenshotRequested += () => ScreenshotService.StartRegionCapture();
            _hotkeys.ClipboardRequested += () => App.ShowClipboardPopup();
            _hotkeys.RecordRequested += () => RecordingUI.StartSelection();
            _hotkeys.ToggleRequested += () =>
            {
                if (_isExpanded) Collapse(true);
                else ShowPanel();
            };
        };

        Loaded += (_, _) =>
        {
            // Show() 结束后窗口会再套用一次初始尺寸，等布局稳定后再展开/收起
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ApplyUserSettings();
                SwitchModule(App.State.Settings.LastModule);

                if (!_isExpanded)
                {
                    if (App.State.Settings.StartExpanded) Expand(false);
                    else Collapse(false);
                }
                UpdateHandlePosition();
            }), DispatcherPriority.ApplicationIdle);
        };

        SizeChanged += (_, _) => UpdateHandlePosition();
        Panel.SizeChanged += (_, _) => UpdatePanelClip();

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            if (_settingsOpen) HideSettings();
            else if (MoreHost.Visibility == Visibility.Visible) HideMore();
            else if (_isExpanded) Collapse(true);
        };
    }

    // ================= 定位与展开 =================

    private void RefreshWorkArea()
    {
        var screens = WinForms.Screen.AllScreens;
        var settings = App.State?.Settings;
        var screen = screens.FirstOrDefault(s => s.DeviceName == settings?.ScreenDevice) ?? WinForms.Screen.PrimaryScreen;

        if (settings != null && string.IsNullOrEmpty(settings.ScreenDevice))
        {
            try
            {
                screen = WinForms.Screen.FromPoint(WinForms.Cursor.Position);
                settings.ScreenDevice = screen.DeviceName;
            }
            catch
            {
            }
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        double sx = dpi.DpiScaleX <= 0 ? 1 : dpi.DpiScaleX;
        double sy = dpi.DpiScaleY <= 0 ? 1 : dpi.DpiScaleY;
        var wa = screen.WorkingArea;
        _workArea = new Rect(wa.X / sx, wa.Y / sy, wa.Width / sx, wa.Height / sy);
    }

    private AppSettings Settings => App.State?.Settings ?? new AppSettings();

    private double PanelWidth => Settings.PanelWidth;
    private double HandleWidth => Settings.HandleWidth;
    private double DrawerWidth => PanelWidth + HandleWidth;
    private double DrawerOffset => PanelWidth;

    private Rect ExpandedRect => new(_workArea.Right - DrawerWidth, _workArea.Top, DrawerWidth, _workArea.Height);

    private void PositionWindow()
    {
        // 窗口始终保持全高全宽，收起时只是把面板平移到屏幕外
        ApplyUserSettings(false);
        var rect = ExpandedRect;
        Left = rect.X;
        Top = rect.Y;
        Width = rect.Width;
        Height = rect.Height;
        UpdateHandlePosition();
    }

    public void ApplyUserSettings(bool rebuildDock = true)
    {
        if (App.State == null) return;

        Drawer.Width = DrawerWidth;
        HandleColumn.Width = new GridLength(HandleWidth);
        PanelColumn.Width = new GridLength(PanelWidth);
        HandleTab.Width = HandleWidth;
        HandleTab.Height = Settings.HandleHeight;
        HandleTab.CornerRadius = new CornerRadius(16, 0, 0, 16);

        double radius = Settings.PanelRadius;
        Panel.CornerRadius = new CornerRadius(radius, 0, 0, radius);
        UpdatePanelClip();

        BackgroundLayer.Opacity = Settings.PanelOpacity;
        UpdateHandlePosition();

        if (rebuildDock)
        {
            BuildDock();
            BuildMore();
        }

        if (_isExpanded)
        {
            Left = _workArea.Right - DrawerWidth;
            Width = DrawerWidth;
        }
    }

    private void UpdateHandlePosition()
    {
        double height = Height > 0 ? Height : _workArea.Height;
        if (height <= 0) return;
        double top = Math.Max(0, (height - Settings.HandleHeight) * Settings.HandleVertical / 100.0);
        HandleTab.Margin = new Thickness(0, top, 0, 0);
    }

    /// <summary>按圆角裁切面板左上/左下，避免背景图盖住圆角</summary>
    private void UpdatePanelClip()
    {
        double w = Panel.ActualWidth;
        double h = Panel.ActualHeight;
        if (w <= 0 || h <= 0) return;

        double r = Math.Max(0, Settings.PanelRadius);
        if (r <= 0)
        {
            Panel.Clip = null;
            return;
        }

        if (r * 2 > h) r = h / 2;
        if (r * 2 > w) r = w / 2;

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(r, 0), true, true);
            ctx.LineTo(new Point(w, 0), true, false);
            ctx.LineTo(new Point(w, h), true, false);
            ctx.LineTo(new Point(r, h), true, false);
            ctx.ArcTo(new Point(0, h - r), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
            ctx.LineTo(new Point(0, r), true, false);
            ctx.ArcTo(new Point(r, 0), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
        }
        geometry.Freeze();
        Panel.Clip = geometry;
    }

    private void BuildDock()
    {
        if (Dock == null) return;
        Dock.Children.Clear();

        var order = ParseTopTools();
        foreach (int index in order.Take(4))
        {
            var tool = Tools.FirstOrDefault(t => t.Index == index);
            if (tool.Name == null) continue;
            Dock.Children.Add(CreateDockItem(tool.Index, tool.Name, tool.Icon, OnDockClick));
        }

        Dock.Children.Add(CreateDockItem(-1, "更多", "IconDots", OnMoreClick, fillIcon: true));
        Dock.Children.Add(CreateDockItem(-2, "设置", "IconGear", OnSettingsClick));
    }

    private List<int> ParseTopTools()
    {
        var seen = new HashSet<int>();
        var list = new List<int>();
        foreach (var part in (Settings.TopTools ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(part, out int index) && index >= 0 && index < Tools.Length && seen.Add(index))
                list.Add(index);
        }
        foreach (var tool in Tools)
        {
            if (list.Count >= 4) break;
            if (seen.Add(tool.Index)) list.Add(tool.Index);
        }
        return list;
    }

    private StackPanel CreateDockItem(int index, string label, string iconKey, RoutedEventHandler handler, bool fillIcon = false)
    {
        var item = new StackPanel { Style = (Style)FindResource("DockItem") };

        var button = new Button
        {
            Style = (Style)FindResource("DockButton"),
            Tag = index >= 0 ? index.ToString() : null,
            ToolTip = label
        };
        button.Click += handler;

        if (FindResource(iconKey) is Geometry geometry)
        {
            var path = new System.Windows.Shapes.Path { Data = geometry };
            if (fillIcon)
            {
                path.Fill = (Brush)FindResource("IconInk");
                path.Stretch = Stretch.Uniform;
                path.Width = 17;
                path.Height = 17;
            }
            else
            {
                path.Style = (Style)FindResource("DockIcon");
            }
            button.Content = path;
        }

        item.Children.Add(button);
        item.Children.Add(new TextBlock { Text = label, Style = (Style)FindResource("DockLabel") });
        return item;
    }

    private void BuildMore()
    {
        if (MoreGrid == null) return;
        MoreGrid.Children.Clear();

        var top = ParseTopTools().Take(4).ToList();
        MoreTitle.Text = $"更多工具 ({Tools.Length - top.Count})";
        foreach (var tool in Tools.Where(t => !top.Contains(t.Index)))
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            if (FindResource(tool.Icon) is Geometry geometry)
                panel.Children.Add(new System.Windows.Shapes.Path
                {
                    Data = geometry,
                    Style = (Style)FindResource("DockIcon"),
                    Width = 16,
                    Height = 16
                });
            panel.Children.Add(new TextBlock { Text = tool.Name, FontSize = 12, Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });

            var button = new Button { Style = (Style)FindResource("MoreItem"), Tag = tool.Index.ToString(), Content = panel };
            button.Click += OnMoreToolClick;
            MoreGrid.Children.Add(button);
        }

        MoreGrid.Children.Add(CreateMoreAction("新建便签", "IconPlus", OnMoreNewNote));
        MoreGrid.Children.Add(CreateMoreAction("数据目录", "IconFolder", OnMoreDataFolder));
        MoreGrid.Children.Add(CreateMoreAction("侧栏设置", "IconGear", OnSettingsClick));
    }

    private Button CreateMoreAction(string label, string iconKey, RoutedEventHandler handler)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        if (FindResource(iconKey) is Geometry geometry)
            panel.Children.Add(new System.Windows.Shapes.Path
            {
                Data = geometry,
                Style = (Style)FindResource("DockIcon"),
                Width = 16,
                Height = 16
            });
        panel.Children.Add(new TextBlock { Text = label, FontSize = 12, Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });

        var button = new Button { Style = (Style)FindResource("MoreItem"), Content = panel };
        button.Click += handler;
        return button;
    }

    public void RefreshTodo()
    {
        if (_pages.TryGetValue(0, out var page) && page is TodoView todo)
            todo.Refresh();
    }

    public void ShowPanel()
    {
        if (!IsVisible) Show();
        Expand(true);
        Activate();
    }

    public bool IsPanelExpanded => _isExpanded;

    public void CollapseExternally() => Collapse(true);

    private void Expand(bool animate)
    {
        if (_isExpanded) return;
        _isExpanded = true;

        DrawerSlide.BeginAnimation(TranslateTransform.XProperty, null);
        if (animate) AnimateDrawer(0, 320, new QuinticEase { EasingMode = EasingMode.EaseOut });
        else DrawerSlide.X = 0;
    }

    private void Collapse(bool animate)
    {
        if (!_isExpanded) return;
        _isExpanded = false;
        HideSettings();
        HideMore();

        if (animate)
            AnimateDrawer(DrawerOffset, 280, new CubicEase { EasingMode = EasingMode.EaseInOut });
        else
            DrawerSlide.X = DrawerOffset;
    }

    private void AnimateDrawer(double targetX, int milliseconds, IEasingFunction easing, Action onDone = null)
    {
        var animation = new DoubleAnimation(targetX, TimeSpan.FromMilliseconds(milliseconds))
        {
            EasingFunction = easing
        };
        animation.Completed += (_, _) =>
        {
            DrawerSlide.BeginAnimation(TranslateTransform.XProperty, null);
            DrawerSlide.X = targetX;
            onDone?.Invoke();
        };
        DrawerSlide.BeginAnimation(TranslateTransform.XProperty, animation);
    }

    // 收起时窗口大部分是透明的，让鼠标穿透到桌面，只有小把手可点
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_NCHITTEST = 0x0084;
        const int HTTRANSPARENT = -1;

        if (msg != WM_NCHITTEST || _isExpanded) return IntPtr.Zero;

        long lp = lParam.ToInt64();
        int sx = (short)(lp & 0xFFFF);
        int sy = (short)((lp >> 16) & 0xFFFF);
        var point = PointFromScreen(new Point(sx, sy));

        try
        {
            var bounds = HandleTab.TransformToAncestor(this)
                .TransformBounds(new Rect(0, 0, HandleTab.ActualWidth, HandleTab.ActualHeight));
            if (bounds.Contains(point)) return IntPtr.Zero;
        }
        catch
        {
        }

        handled = true;
        return new IntPtr(HTTRANSPARENT);
    }

    private void OnHandleClick(object sender, MouseButtonEventArgs e)
    {
        if (_isExpanded) Collapse(true);
        else
        {
            Expand(true);
            Activate();
        }
    }

    private void OnWindowDeactivated(object sender, EventArgs e)
    {
        App.Trace($"Deactivated expanded={_isExpanded} settings={_settingsOpen} autoHide={App.State?.Settings.AutoHide}");
        if (!_isExpanded || _settingsOpen) return;
        if (App.State?.Settings.AutoHide != true) return;

        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!IsActive && _isExpanded) Collapse(true);
        }), DispatcherPriority.Background);
    }

    // ================= 导航 =================

    private void OnDockClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && int.TryParse(tag, out int index))
        {
            if (!_isExpanded) Expand(true);
            SwitchModule(index);
        }
    }

    private void OnBackClick(object sender, RoutedEventArgs e) => SwitchModule(0);

    private void SwitchModule(int index)
    {
        index = Math.Clamp(index, 0, _pageTypes.Length - 1);
        _activeModule = index;

        foreach (var created in _pages.Values)
            created.Visibility = Visibility.Collapsed;

        var page = GetPage(index);
        page.Visibility = Visibility.Visible;

        HomeHeader.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
        ToolHeader.Visibility = index == 0 ? Visibility.Collapsed : Visibility.Visible;
        ToolTitle.Text = _moduleTitles[index];

        App.State.Settings.LastModule = index;

        HideSettings();
        HideMore();
    }

    /// <summary>按需创建功能页，避免启动时把 8 个页面全部加载进内存</summary>
    private UIElement GetPage(int index)
    {
        if (_pages.TryGetValue(index, out var page)) return page;

        page = (UIElement)Activator.CreateInstance(_pageTypes[index]);
        page.SetValue(DataContextProperty, App.State);
        page.Visibility = Visibility.Collapsed;
        PageHost.Children.Add(page);
        _pages[index] = page;
        return page;
    }

    // ================= 更多工具 =================

    private void OnMoreClick(object sender, RoutedEventArgs e)
    {
        if (!_isExpanded) Expand(true);
        MoreHost.Visibility = MoreHost.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnMoreBackdropClick(object sender, MouseButtonEventArgs e) => HideMore();

    private void HideMore() => MoreHost.Visibility = Visibility.Collapsed;

    private void OnMoreToolClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && int.TryParse(tag, out int index))
            SwitchModule(index);
    }

    private void OnMoreNewNote(object sender, RoutedEventArgs e)
    {
        HideMore();
        App.CreateNote();
    }

    private void OnMoreDataFolder(object sender, RoutedEventArgs e)
    {
        HideMore();
        StorageService.OpenFolder();
    }

    // ================= 设置 =================

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        if (!_isExpanded) Expand(true);
        if (_settingsOpen) HideSettings();
        else ShowSettings();
    }

    public void ShowSettings()
    {
        HideMore();
        _settingsOpen = true;
        SettingsHost.Visibility = Visibility.Visible;
        SettingsHost.Opacity = 0;
        SettingsHost.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
        SettingsPage.RefreshData();
    }

    public void HideSettings()
    {
        if (!_settingsOpen)
        {
            SettingsHost.Visibility = Visibility.Collapsed;
            return;
        }

        _settingsOpen = false;
        SettingsHost.Visibility = Visibility.Collapsed;
    }

    private static readonly string[] ThemeKeys = { "Cream", "Sea", "Sakura", "Forest", "Starry", "Aurora" };

    public void ApplyTheme(string themeKey, bool save = true)
    {
        string key = ThemeKeys.Contains(themeKey) ? themeKey : "Cream";

        try
        {
            // 背景只保留一份，并按面板实际需要的宽度解码
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 600;
            image.UriSource = new Uri($"pack://application:,,,/Assets/bg_{key.ToLowerInvariant()}.jpg");
            image.EndInit();
            image.Freeze();
            ThemeBackdrop.Fill = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
            ThemeBackdrop.Visibility = Visibility.Visible;
        }
        catch
        {
            ThemeBackdrop.Fill = new SolidColorBrush(Color.FromRgb(0xF7, 0xFA, 0xFD));
            ThemeBackdrop.Visibility = Visibility.Visible;
        }

        if (save)
            App.State.Settings.Theme = key;
    }

    public void RepositionToSelectedScreen()
    {
        RefreshWorkArea();
        var rect = ExpandedRect;
        Left = rect.X;
        Top = rect.Y;
        Width = rect.Width;
        Height = rect.Height;
    }

    private void OnOpenDataFolder(object sender, RoutedEventArgs e) => StorageService.OpenFolder();

    private void OnWindowClosing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (App.IsShuttingDown || _allowClose) return;
        e.Cancel = true;
        Collapse(true);
    }

    /// <summary>切换明暗主题时重建窗口，允许真正关闭当前窗口</summary>
    public void CloseForRebuild()
    {
        _allowClose = true;
        Close();
    }
}
