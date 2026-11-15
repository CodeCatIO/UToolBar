using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace EdgeBar.Models;

public class AppSettings : ObservableObject
{
    private bool _autoStart;
    private bool _soundEnabled = true;
    private int _lastModule;
    private string _screenDevice = "";
    private bool _startExpanded = true;
    private bool _autoHide = true;
    private bool _salaryWidgetVisible;
    private double _widgetLeft;
    private double _widgetTop;
    private bool _darkMode;
    private string _theme = "Cream";
    private double _panelWidth = 364;
    private double _handleWidth = 40;
    private double _handleHeight = 84;
    private double _handleVertical = 50;
    private double _panelRadius = 18;
    private double _panelOpacity = 0.91;
    private bool _showCompleted = true;
    private double _taskRowHeight = 44;
    private double _taskSpacing = 7;
    private double _taskFontSize = 12.5;
    private string _taskFont = "Microsoft YaHei UI";
    private string _topTools = "6,5,7,2";
    private string _giteeOwner = "";
    private string _giteeRepo = "";
    private string _giteeToken = "";
    private string _giteePath = "edgebar/data.json";
    private int _recordFormat;
    private int _recordQuality;
    private bool _recordSystemAudio = true;
    private bool _recordMicrophone;
    private string _giteeBranch = "master";
    private string _screenshotFolder = "";

    public bool AutoStart
    {
        get => _autoStart;
        set => Set(ref _autoStart, value);
    }

    public bool SoundEnabled
    {
        get => _soundEnabled;
        set => Set(ref _soundEnabled, value);
    }

    /// <summary>上次停留的模块索引 0-7</summary>
    public int LastModule
    {
        get => _lastModule;
        set => Set(ref _lastModule, Math.Clamp(value, 0, 7));
    }

    /// <summary>贴靠的屏幕设备名，多显示器下记住位置</summary>
    public string ScreenDevice
    {
        get => _screenDevice;
        set => Set(ref _screenDevice, value ?? "");
    }

    public bool StartExpanded
    {
        get => _startExpanded;
        set => Set(ref _startExpanded, value);
    }

    /// <summary>鼠标离开后是否自动收起</summary>
    public bool AutoHide
    {
        get => _autoHide;
        set => Set(ref _autoHide, value);
    }

    public bool SalaryWidgetVisible
    {
        get => _salaryWidgetVisible;
        set => Set(ref _salaryWidgetVisible, value);
    }

    public double WidgetLeft
    {
        get => _widgetLeft;
        set => Set(ref _widgetLeft, value);
    }

    public double WidgetTop
    {
        get => _widgetTop;
        set => Set(ref _widgetTop, value);
    }

    /// <summary>深色外观</summary>
    public bool DarkMode
    {
        get => _darkMode;
        set => Set(ref _darkMode, value);
    }

    /// <summary>背景主题：Cream / Sea / Sakura / Forest / Starry / Aurora</summary>
    public string Theme
    {
        get => _theme;
        set => Set(ref _theme, string.IsNullOrWhiteSpace(value) ? "Cream" : value);
    }

    /// <summary>面板宽度（不含把手）</summary>
    public double PanelWidth
    {
        get => _panelWidth;
        set => Set(ref _panelWidth, Math.Clamp(value, 320, 440));
    }

    public double HandleWidth
    {
        get => _handleWidth;
        set => Set(ref _handleWidth, Math.Clamp(value, 32, 52));
    }

    public double HandleHeight
    {
        get => _handleHeight;
        set => Set(ref _handleHeight, Math.Clamp(value, 64, 120));
    }

    /// <summary>把手的垂直位置，0-100</summary>
    public double HandleVertical
    {
        get => _handleVertical;
        set => Set(ref _handleVertical, Math.Clamp(value, 10, 90));
    }

    public double PanelRadius
    {
        get => _panelRadius;
        set => Set(ref _panelRadius, Math.Clamp(value, 0, 30));
    }

    /// <summary>背景层不透明度，0.5-1</summary>
    public double PanelOpacity
    {
        get => _panelOpacity;
        set => Set(ref _panelOpacity, Math.Clamp(value, 0.5, 1));
    }

    public bool ShowCompleted
    {
        get => _showCompleted;
        set => Set(ref _showCompleted, value);
    }

    public double TaskRowHeight
    {
        get => _taskRowHeight;
        set => Set(ref _taskRowHeight, Math.Clamp(value, 36, 60));
    }

    public double TaskSpacing
    {
        get => _taskSpacing;
        set => Set(ref _taskSpacing, Math.Clamp(value, 0, 16));
    }

    public double TaskFontSize
    {
        get => _taskFontSize;
        set => Set(ref _taskFontSize, Math.Clamp(value, 11, 18));
    }

    public string TaskFont
    {
        get => _taskFont;
        set => Set(ref _taskFont, string.IsNullOrWhiteSpace(value) ? "Microsoft YaHei UI" : value);
    }

    /// <summary>顶栏显示的工具模块索引，逗号分隔，前 4 个生效</summary>
    public string TopTools
    {
        get => _topTools;
        set => Set(ref _topTools, value ?? "");
    }

    public string GiteeOwner
    {
        get => _giteeOwner;
        set => Set(ref _giteeOwner, value ?? "");
    }

    public string GiteeRepo
    {
        get => _giteeRepo;
        set => Set(ref _giteeRepo, value ?? "");
    }

    public string GiteeToken
    {
        get => _giteeToken;
        set => Set(ref _giteeToken, value ?? "");
    }

    public string GiteePath
    {
        get => _giteePath;
        set => Set(ref _giteePath, string.IsNullOrWhiteSpace(value) ? "edgebar/data.json" : value);
    }

    public string GiteeBranch
    {
        get => _giteeBranch;
        set => Set(ref _giteeBranch, string.IsNullOrWhiteSpace(value) ? "master" : value);
    }

    /// <summary>截图保存目录，空表示默认的“图片\贴边截图”</summary>
    public string ScreenshotFolder
    {
        get => _screenshotFolder;
        set => Set(ref _screenshotFolder, value ?? "");
    }

    /// <summary>录屏格式：0 = MP4，1 = GIF</summary>
    public int RecordFormat
    {
        get => _recordFormat;
        set => Set(ref _recordFormat, Math.Clamp(value, 0, 1));
    }

    /// <summary>录屏清晰度：0 = 标清，1 = 高清</summary>
    public int RecordQuality
    {
        get => _recordQuality;
        set => Set(ref _recordQuality, Math.Clamp(value, 0, 1));
    }

    public bool RecordSystemAudio
    {
        get => _recordSystemAudio;
        set => Set(ref _recordSystemAudio, value);
    }

    public bool RecordMicrophone
    {
        get => _recordMicrophone;
        set => Set(ref _recordMicrophone, value);
    }
}

public class SalarySettings : ObservableObject
{
    private double _monthlySalary = 10000;
    private string _workStart = "09:00";
    private string _workEnd = "18:00";
    private int _payDay = 15;

    public double MonthlySalary
    {
        get => _monthlySalary;
        set => Set(ref _monthlySalary, Math.Max(0, value));
    }

    public string WorkStart
    {
        get => _workStart;
        set => Set(ref _workStart, value);
    }

    public string WorkEnd
    {
        get => _workEnd;
        set => Set(ref _workEnd, value);
    }

    /// <summary>每月发薪日 1-31</summary>
    public int PayDay
    {
        get => _payDay;
        set => Set(ref _payDay, Math.Clamp(value, 1, 31));
    }
}

public class AppState
{
    public ObservableCollection<TodoItem> Todos { get; set; } = new();
    public ObservableCollection<AnniversaryItem> Anniversaries { get; set; } = new();
    public ObservableCollection<NoteData> Notes { get; set; } = new();
    public ObservableCollection<ClipItem> Clips { get; set; } = new();
    public SalarySettings Salary { get; set; } = new();
    public AppSettings Settings { get; set; } = new();

    public event Action Changed;

    private bool Suspended { get; set; }

    public AppState()
    {
        Todos.CollectionChanged += OnTodosChanged;
        Anniversaries.CollectionChanged += OnAnniversariesChanged;
        Notes.CollectionChanged += OnNotesChanged;
        Clips.CollectionChanged += OnClipsChanged;
        Salary.PropertyChanged += OnChildChanged;
        Settings.PropertyChanged += OnChildChanged;
    }

    /// <summary>加载完成后调用：反序列化会替换集合与子对象实例，这里重新挂上监听</summary>
    public void Attach()
    {
        Suspended = true;

        Todos.CollectionChanged -= OnTodosChanged;
        Todos.CollectionChanged += OnTodosChanged;
        Anniversaries.CollectionChanged -= OnAnniversariesChanged;
        Anniversaries.CollectionChanged += OnAnniversariesChanged;
        Notes.CollectionChanged -= OnNotesChanged;
        Notes.CollectionChanged += OnNotesChanged;
        Clips.CollectionChanged -= OnClipsChanged;
        Clips.CollectionChanged += OnClipsChanged;

        Salary.PropertyChanged -= OnChildChanged;
        Salary.PropertyChanged += OnChildChanged;
        Settings.PropertyChanged -= OnChildChanged;
        Settings.PropertyChanged += OnChildChanged;

        foreach (var t in Todos)
        {
            t.PropertyChanged -= OnChildChanged;
            t.PropertyChanged += OnChildChanged;
        }
        foreach (var a in Anniversaries)
        {
            a.PropertyChanged -= OnChildChanged;
            a.PropertyChanged += OnChildChanged;
        }
        foreach (var n in Notes)
        {
            n.PropertyChanged -= OnChildChanged;
            n.PropertyChanged += OnChildChanged;
        }
        foreach (var c in Clips)
        {
            c.PropertyChanged -= OnChildChanged;
            c.PropertyChanged += OnChildChanged;
        }

        Suspended = false;
    }

    private void OnTodosChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        Hook(e);
        Notify();
    }

    private void OnAnniversariesChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        Hook(e);
        Notify();
    }

    private void OnNotesChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        Hook(e);
        Notify();
    }

    private void OnClipsChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        Hook(e);
        Notify();
    }

    private void Hook(NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (INotifyPropertyChanged item in e.NewItems)
                item.PropertyChanged += OnChildChanged;
        }
        if (e.OldItems != null)
        {
            foreach (INotifyPropertyChanged item in e.OldItems)
                item.PropertyChanged -= OnChildChanged;
        }
    }

    private void OnChildChanged(object sender, PropertyChangedEventArgs e) => Notify();

    public void Notify()
    {
        if (Suspended) return;
        Changed?.Invoke();
    }
}
