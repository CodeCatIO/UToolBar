using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EdgeBar.Models;
using EdgeBar.Services;
using WinForms = System.Windows.Forms;

namespace EdgeBar.Views;

public partial class SettingsView : UserControl
{
    private static readonly (string Key, string Name)[] ThemeDefs =
    {
        ("Cream", "奶油蓝"),
        ("Sea", "海浪"),
        ("Sakura", "樱花"),
        ("Forest", "雾林"),
        ("Starry", "星夜"),
        ("Aurora", "极光")
    };

    private readonly ObservableCollection<ThemeOption> _themes = new();
    private readonly List<MonitorOption> _monitors = new();
    private List<int> _toolOrder = new();
    private bool _loading = true;

    private static readonly (int Index, string Name)[] ToolDefs =
    {
        (0, "待办"),
        (1, "纪念日"),
        (2, "计算器"),
        (3, "万年历"),
        (4, "牛马计薪"),
        (5, "便利签"),
        (6, "截图 / 贴图"),
        (7, "剪贴板")
    };

    private static readonly string[] FontChoices =
    {
        "Microsoft YaHei UI", "微软雅黑", "宋体", "黑体", "楷体", "Segoe UI"
    };

    public event EventHandler BackRequested;

    public SettingsView()
    {
        InitializeComponent();

        foreach (var (key, name) in ThemeDefs)
        {
            _themes.Add(new ThemeOption
            {
                Key = key,
                Name = name,
                Brush = LoadThemeBrush(key)
            });
        }
        ThemeList.ItemsSource = _themes;
        TaskFontCombo.ItemsSource = FontChoices;

        Loaded += (_, _) => RefreshData();
    }

    private static Brush LoadThemeBrush(string key)
    {
        try
        {
            // 设置页只显示缩略图，没必要把整张背景图解码进内存
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 240;
            image.UriSource = new Uri($"pack://application:,,,/Assets/bg_{key.ToLowerInvariant()}.jpg");
            image.EndInit();
            image.Freeze();
            return new ImageBrush(image) { Stretch = Stretch.UniformToFill };
        }
        catch
        {
            return Brushes.Transparent;
        }
    }

    public void RefreshData()
    {
        if (App.State == null) return;
        _loading = true;

        var settings = App.State.Settings;
        LightButton.IsChecked = !settings.DarkMode;
        DarkButton.IsChecked = settings.DarkMode;
        RestartHint.Visibility = Visibility.Collapsed;

        foreach (var theme in _themes)
            theme.IsSelected = string.Equals(theme.Key, settings.Theme, StringComparison.OrdinalIgnoreCase);

        BuildMonitors(settings);

        AutoStartSwitch.IsChecked = AutoStartService.IsEnabled();
        DataPathText.Text = StorageService.Folder;
        ShotFolderText.Text = ScreenshotFolderDisplay();
        VersionText.Text = "贴边 v" + (typeof(App).Assembly.GetName().Version?.ToString(3) ?? "2.0.0");

        PanelWidthSlider.Value = settings.PanelWidth;
        HandleWidthSlider.Value = settings.HandleWidth;
        HandleHeightSlider.Value = settings.HandleHeight;
        HandleVerticalSlider.Value = settings.HandleVertical;
        RowHeightSlider.Value = settings.TaskRowHeight;
        RowSpacingSlider.Value = settings.TaskSpacing;
        TaskFontSizeSlider.Value = settings.TaskFontSize;
        PanelRadiusSlider.Value = settings.PanelRadius;
        PanelOpacitySlider.Value = settings.PanelOpacity * 100;
        ShowCompletedSwitch.IsChecked = settings.ShowCompleted;
        TaskFontCombo.SelectedItem = settings.TaskFont;

        UpdateSliderLabels();
        BuildToolOrder();

        GiteeOwnerBox.Text = settings.GiteeOwner;
        GiteeRepoBox.Text = settings.GiteeRepo;
        GiteeBranchBox.Text = settings.GiteeBranch;
        GiteePathBox.Text = settings.GiteePath;
        GiteeTokenBox.Text = settings.GiteeToken;

        RefreshBackdrop();

        _loading = false;
    }

    private void UpdateSliderLabels()
    {
        PanelWidthValue.Text = $"{PanelWidthSlider.Value:0}px";
        HandleWidthValue.Text = $"{HandleWidthSlider.Value:0}px";
        HandleHeightValue.Text = $"{HandleHeightSlider.Value:0}px";
        HandleVerticalValue.Text = $"{HandleVerticalSlider.Value:0}%";
        RowHeightValue.Text = $"{RowHeightSlider.Value:0}px";
        RowSpacingValue.Text = $"{RowSpacingSlider.Value:0}px";
        TaskFontSizeValue.Text = $"{TaskFontSizeSlider.Value:0.#}px";
        PanelRadiusValue.Text = $"{PanelRadiusSlider.Value:0}px";
        PanelOpacityValue.Text = $"{PanelOpacitySlider.Value:0}%";
        UpdateAppearancePreview();
    }

    /// <summary>观感预览：圆角与不透明度实时反映在缩略面板上</summary>
    private void UpdateAppearancePreview()
    {
        if (AppearancePreview == null || App.State == null) return;
        double radius = PanelRadiusSlider.Value;
        AppearancePreview.CornerRadius = new CornerRadius(radius, 0, 0, radius);
        AppearancePreview.Opacity = Math.Max(0.2, PanelOpacitySlider.Value / 100.0);
        AppearancePreview.Background = LoadThemeBrush(App.State.Settings.Theme);
    }

    private void OnSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading || App.State == null || sender is not Slider slider) return;

        var settings = App.State.Settings;
        switch (slider.Tag as string)
        {
            case "width": settings.PanelWidth = slider.Value; break;
            case "handleWidth": settings.HandleWidth = slider.Value; break;
            case "handleHeight": settings.HandleHeight = slider.Value; break;
            case "handleVertical": settings.HandleVertical = slider.Value; break;
            case "rowHeight": settings.TaskRowHeight = slider.Value; break;
            case "rowSpacing": settings.TaskSpacing = slider.Value; break;
            case "fontSize": settings.TaskFontSize = slider.Value; break;
            case "radius": settings.PanelRadius = slider.Value; break;
            case "opacity": settings.PanelOpacity = slider.Value / 100; break;
        }

        UpdateSliderLabels();
        App.Shell?.ApplyUserSettings(false);
        App.Shell?.RefreshTodo();
    }

    private void OnTaskSettingChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || App.State == null) return;
        App.State.Settings.ShowCompleted = ShowCompletedSwitch.IsChecked == true;
        App.Shell?.RefreshTodo();
    }

    private void OnTaskFontChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || App.State == null || TaskFontCombo.SelectedItem is not string font) return;
        App.State.Settings.TaskFont = font;
        App.Shell?.RefreshTodo();
    }

    private void BuildToolOrder()
    {
        if (App.State == null) return;

        _toolOrder = new List<int>();
        foreach (var part in (App.State.Settings.TopTools ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(part, out int index) && index >= 0 && index < ToolDefs.Length && !_toolOrder.Contains(index))
                _toolOrder.Add(index);
        }
        foreach (var tool in ToolDefs)
        {
            if (!_toolOrder.Contains(tool.Index)) _toolOrder.Add(tool.Index);
        }

        ToolOrderList.Children.Clear();
        for (int i = 0; i < _toolOrder.Count; i++)
        {
            int index = _toolOrder[i];
            string name = ToolDefs.First(t => t.Index == index).Name;

            var row = new Border { Style = (Style)FindResource("ToolOrderRow") };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            grid.Children.Add(new TextBlock
            {
                Text = (i + 1).ToString(),
                FontSize = 10,
                Foreground = (Brush)FindResource("TextMuted"),
                VerticalAlignment = VerticalAlignment.Center
            });

            var nameText = new TextBlock
            {
                Text = name,
                FontSize = 11.5,
                Foreground = (Brush)FindResource("TextPrimary"),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(nameText, 1);
            grid.Children.Add(nameText);

            var badge = new Border { Style = (Style)FindResource("ToolOrderBadge") };
            badge.Child = new TextBlock
            {
                Text = i < 4 ? "顶栏" : "更多",
                FontSize = 8.5,
                Foreground = (Brush)FindResource("AccentDeep")
            };
            Grid.SetColumn(badge, 2);
            grid.Children.Add(badge);

            var up = new Button
            {
                Style = (Style)FindResource("GhostButton"),
                Content = "↑",
                Width = 24,
                Height = 22,
                FontSize = 12,
                Padding = new Thickness(0),
                Foreground = (Brush)FindResource("TextSecondary"),
                Margin = new Thickness(5, 0, 0, 0),
                Tag = i,
                IsEnabled = i > 0
            };
            up.Click += OnToolMoveUp;
            Grid.SetColumn(up, 3);
            grid.Children.Add(up);

            var down = new Button
            {
                Style = (Style)FindResource("GhostButton"),
                Content = "↓",
                Width = 24,
                Height = 22,
                FontSize = 12,
                Padding = new Thickness(0),
                Foreground = (Brush)FindResource("TextSecondary"),
                Margin = new Thickness(4, 0, 0, 0),
                Tag = i,
                IsEnabled = i < _toolOrder.Count - 1
            };
            down.Click += OnToolMoveDown;
            Grid.SetColumn(down, 4);
            grid.Children.Add(down);

            row.Child = grid;
            ToolOrderList.Children.Add(row);
        }
    }

    private void OnToolMoveUp(object sender, RoutedEventArgs e) => MoveTool(sender, -1);
    private void OnToolMoveDown(object sender, RoutedEventArgs e) => MoveTool(sender, 1);

    private void MoveTool(object sender, int delta)
    {
        if (sender is not Button { Tag: int index }) return;
        int target = index + delta;
        if (index < 0 || target < 0 || target >= _toolOrder.Count) return;

        (_toolOrder[index], _toolOrder[target]) = (_toolOrder[target], _toolOrder[index]);
        App.State.Settings.TopTools = string.Join(",", _toolOrder);
        BuildToolOrder();
        App.Shell?.ApplyUserSettings();
    }

    private async void OnGiteeUpload(object sender, RoutedEventArgs e)
    {
        if (App.State == null) return;
        SaveSyncFields();
        SyncStatusText.Text = "正在上传…";
        try
        {
            SyncStatusText.Text = await GiteeSyncService.UploadAsync(App.State.Settings);
        }
        catch (Exception ex)
        {
            SyncStatusText.Text = ex.Message;
        }
    }

    private async void OnGiteePull(object sender, RoutedEventArgs e)
    {
        if (App.State == null) return;
        SaveSyncFields();
        SyncStatusText.Text = "正在下拉…";
        try
        {
            string message = await GiteeSyncService.PullAsync(App.State.Settings);
            SyncStatusText.Text = message + "，正在重启应用…";
            App.ShowToast("数据已下拉", "已写入本地，应用将重新启动");
            App.RestartApp();
        }
        catch (Exception ex)
        {
            SyncStatusText.Text = ex.Message;
        }
    }

    private void SaveSyncFields()
    {
        var settings = App.State.Settings;
        settings.GiteeOwner = GiteeOwnerBox.Text.Trim();
        settings.GiteeRepo = GiteeRepoBox.Text.Trim();
        settings.GiteeBranch = GiteeBranchBox.Text.Trim();
        settings.GiteePath = GiteePathBox.Text.Trim();
        settings.GiteeToken = GiteeTokenBox.Text.Trim();
        StorageService.SaveNow(App.State);
    }

    private void RefreshBackdrop()
    {
        Backdrop.Fill = Application.Current.TryFindResource("SettingsBackdrop") as Brush ?? Brushes.Transparent;
    }

    private void BuildMonitors(AppSettings settings)
    {
        _monitors.Clear();
        var screens = WinForms.Screen.AllScreens;
        for (int i = 0; i < screens.Length; i++)
        {
            var screen = screens[i];
            _monitors.Add(new MonitorOption
            {
                DeviceName = screen.DeviceName,
                DisplayName = $"显示器 {i + 1} · {screen.Bounds.Width} × {screen.Bounds.Height}" + (screen.Primary ? " · 主屏" : "")
            });
        }

        MonitorCombo.ItemsSource = _monitors;

        if (string.IsNullOrEmpty(settings.ScreenDevice))
        {
            var primary = _monitors.FirstOrDefault(m => m.DeviceName == WinForms.Screen.PrimaryScreen?.DeviceName);
            settings.ScreenDevice = primary?.DeviceName ?? _monitors.FirstOrDefault()?.DeviceName ?? "";
        }

        MonitorCombo.SelectedItem = _monitors.FirstOrDefault(m => m.DeviceName == settings.ScreenDevice) ?? _monitors.FirstOrDefault();
        MonitorHint.Text = $"当前检测到 {_monitors.Count} 个显示器" +
                           (_monitors.Count > 1
                               ? "，选中后贴边会移动到该屏幕的右边缘。"
                               : "，接入副屏后可以在这里切换。");
    }

    private void OnBackClick(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);

    private void OnAppearanceClick(object sender, RoutedEventArgs e)
    {
        if (_loading || App.State == null) return;

        bool dark = ReferenceEquals(sender, DarkButton);
        LightButton.IsChecked = !dark;
        DarkButton.IsChecked = dark;
        App.SetDarkMode(dark);
    }

    private void OnRestartClick(object sender, RoutedEventArgs e) => App.RestartApp();

    private void OnThemeClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: string key } || App.State == null) return;

        foreach (var theme in _themes)
            theme.IsSelected = theme.Key == key;

        App.State.Settings.Theme = key;
        App.Shell?.ApplyTheme(key);
        RefreshBackdrop();
        UpdateAppearancePreview();
    }

    private void OnMonitorChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || App.State == null) return;
        if (MonitorCombo.SelectedItem is not MonitorOption option) return;

        App.State.Settings.ScreenDevice = option.DeviceName;
        App.Shell?.RepositionToSelectedScreen();
    }

    private void OnAutoStartChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || App.State == null) return;
        bool enabled = AutoStartSwitch.IsChecked == true;
        App.State.Settings.AutoStart = enabled;
        AutoStartService.Apply(enabled);
    }

    private void OnOpenDataFolder(object sender, RoutedEventArgs e) => StorageService.OpenFolder();

    private string ScreenshotFolderDisplay()
    {
        string custom = App.State?.Settings.ScreenshotFolder;
        return string.IsNullOrWhiteSpace(custom)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "贴边截图")
            : custom;
    }

    private void OnChangeScreenshotFolder(object sender, RoutedEventArgs e)
    {
        using var dialog = new WinForms.FolderBrowserDialog
        {
            Description = "选择截图默认保存位置",
            UseDescriptionForTitle = true,
            SelectedPath = ScreenshotFolderDisplay(),
            ShowNewFolderButton = true
        };
        if (dialog.ShowDialog() != WinForms.DialogResult.OK) return;

        App.State.Settings.ScreenshotFolder = dialog.SelectedPath;
        App.State.Notify();
        ShotFolderText.Text = ScreenshotFolderDisplay();
    }

    private void OnChangeDataFolder(object sender, RoutedEventArgs e)
    {
        using var dialog = new WinForms.FolderBrowserDialog
        {
            Description = "选择贴边数据保存位置",
            UseDescriptionForTitle = true,
            SelectedPath = StorageService.Folder,
            ShowNewFolderButton = true
        };

        if (dialog.ShowDialog() != WinForms.DialogResult.OK) return;

        if (!StorageService.SetFolder(dialog.SelectedPath, out string error))
        {
            App.ShowToast("修改保存位置失败", error);
            return;
        }

        StorageService.SaveNow(App.State);
        App.ShowToast("数据目录已修改", "正在迁移并重启应用");
        App.RestartApp();
    }

    private void OnClearClips(object sender, RoutedEventArgs e)
    {
        ClipboardService.ClearAll();
        App.ShowToast("已清空", "剪贴板历史记录已删除");
    }

    private void OnExitClick(object sender, RoutedEventArgs e) => App.ExitApp();
}

public class ThemeOption : ObservableObject
{
    private bool _isSelected;

    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public Brush Brush { get; set; }

    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }
}

public class MonitorOption
{
    public string DeviceName { get; set; } = "";
    public string DisplayName { get; set; } = "";

    public override string ToString() => DisplayName;
}
