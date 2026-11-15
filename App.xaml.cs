using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using UToolBar.Models;
using UToolBar.Services;
using UToolBar.Views;

namespace UToolBar;

public partial class App : Application
{
    private const string MutexName = "UToolBar.App.SingleInstance";
    private const string ShowEventName = "UToolBar.App.Show";

    private static Mutex _mutex;
    private static EventWaitHandle _showEvent;
    private static ToastWindow _toast;
    private static ClipboardPopupWindow _clipboardPopup;
    private static SalaryWidgetWindow _salaryWidget;
    private static readonly List<NoteWindow> NoteWindowList = new();

    public static AppState State { get; private set; }
    public static MainWindow Shell { get; private set; }
    public static bool IsShuttingDown { get; private set; }
    public static bool IsDarkMode { get; private set; }

    public static void ExitApp()
    {
        IsShuttingDown = true;
        Current.Shutdown();
    }

    public static void RestartApp()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe))
            {
                var arguments = $"/c ping -n 3 127.0.0.1 >nul & start \"\" \"{exe}\"";
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = arguments,
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
            }
        }
        catch
        {
        }
        ExitApp();
    }

    private static void LoadThemeResources()
    {
        IsDarkMode = State?.Settings?.DarkMode == true;
        var resources = Current.Resources;
        resources.MergedDictionaries.Clear();

        void Add(string relativePath)
        {
            resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri(relativePath, UriKind.Relative)
            });
        }

        Add(IsDarkMode ? "Themes/DarkColors.xaml" : "Themes/LightColors.xaml");
        Add("Themes/Styles.xaml");
    }

    /// <summary>实时切换明暗外观：重载颜色资源并重建主面板，不需要重启程序</summary>
    public static void SetDarkMode(bool dark)
    {
        if (State == null || State.Settings.DarkMode == dark) return;

        State.Settings.DarkMode = dark;
        LoadThemeResources();

        var old = Shell;
        Shell = new MainWindow();
        Shell.Show();
        Shell.ShowPanel();
        Shell.Dispatcher.BeginInvoke(new Action(() => Shell.ShowSettings()),
            System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        old?.CloseForRebuild();

        ClipboardService.Current.Attach(Shell);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Trace("OnStartup");
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Trace("AppDomain unhandled: " + args.ExceptionObject);
            LogError(args.ExceptionObject as Exception);
        };

        if (!EnsureSingleInstance())
        {
            Trace("single instance -> shutdown");
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            Trace("Dispatcher unhandled: " + args.Exception);
            LogError(args.Exception);
            args.Handled = true;
            ShowToast("出了点小状况", "已写入日志，可以继续使用");
        };

        State = StorageService.Load();
        State.Attach();
        State.Changed += () => StorageService.SaveLater(State);
        Trace("data " + StorageService.Folder);

        // 2.2 起使用插画背景主题，旧主题名统一迁移到奶油蓝
        var legacyThemes = new[] { "Plain", "ClearBlue", "FarMountains", "Waves", "Leaves", "Cyber", "Anime" };
        if (legacyThemes.Contains(State.Settings.Theme)) State.Settings.Theme = "Cream";

        LoadThemeResources();
        Trace("theme loaded");

        Shell = new MainWindow();
        Trace("window created");
        Shell.Loaded += (_, _) => RestoreNotes();
        Shell.Show();
        Trace("window shown");

        ClipboardService.Current.Attach(Shell);

        AutoStartService.Migrate();

        var tray = new TrayService();
        tray.OpenRequested += () => Shell.ShowPanel();
        tray.NewNoteRequested += () => CreateNote();
        tray.RecordRequested += () => RecordingUI.StartSelection();
        tray.ExitRequested += ExitApp;
        tray.SetAutoStartChecked(AutoStartService.IsEnabled());
        Tray = tray;
        Trace("tray ready");

        // 注册表可能被外部修改，启动时以实际状态为准
        State.Settings.AutoStart = AutoStartService.IsEnabled();
        if (State.Settings.AutoStart) AutoStartService.Apply(true);

        var reminderTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        reminderTimer.Tick += (_, _) => CheckReminders();
        reminderTimer.Start();
        ReminderTimer = reminderTimer;

        if (State.Settings.SalaryWidgetVisible) ShowSalaryWidget(false);
    }

    private TrayService Tray { get; set; }
    private DispatcherTimer ReminderTimer { get; set; }

    // ================= 单实例 =================

    private static bool EnsureSingleInstance()
    {
        _mutex = new Mutex(true, MutexName, out bool isNew);
        if (!isNew)
        {
            try
            {
                using var ev = EventWaitHandle.OpenExisting(ShowEventName);
                ev.Set();
            }
            catch
            {
            }
            return false;
        }

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        var thread = new Thread(() =>
        {
            while (_showEvent.WaitOne())
            {
                Current.Dispatcher.BeginInvoke(new Action(() => Shell?.ShowPanel()));
            }
        })
        {
            IsBackground = true,
            Name = "UToolBar.SingleInstance"
        };
        thread.Start();
        return true;
    }

    // ================= 便签 =================

    public static void RestoreNotes()
    {
        foreach (var note in State.Notes.ToList())
        {
            CreateNote(note, restored: true);
        }
    }

    public static void CreateNote(NoteData data = null, bool restored = false)
    {
        if (data == null)
        {
            if (State.Notes.Count >= 12)
            {
                ShowToast("便签贴满了", "最多同时贴 12 张，删掉一张再新建吧");
                return;
            }

            var area = SystemParameters.WorkArea;
            double left = Math.Max(area.Left + 40, area.Right - 600);
            double top = Math.Min(area.Bottom - 300, area.Top + 120 + NoteWindowList.Count % 8 * 28);
            data = new NoteData { Left = left, Top = top };
            State.Notes.Add(data);
        }

        var existing = NoteWindowList.FirstOrDefault(w => w.NoteId == data.Id);
        if (existing != null)
        {
            existing.Activate();
            return;
        }

        var window = new NoteWindow(data) { ShowActivated = !restored };
        NoteWindowList.Add(window);
        window.Closed += (_, _) =>
        {
            NoteWindowList.Remove(window);
            if (State.Notes.Contains(data)) State.Notes.Remove(data);
        };
        window.Show();
        if (!restored) window.Activate();
    }

    public static void DeleteNote(NoteData data)
    {
        var window = NoteWindowList.FirstOrDefault(w => w.NoteId == data.Id);
        if (window != null)
        {
            window.Close(); // 关闭后由 Closed 事件从数据里移除
            return;
        }
        if (State.Notes.Contains(data)) State.Notes.Remove(data);
    }

    // ================= 提醒 =================

    private static void CheckReminders()
    {
        if (State == null) return;
        var now = DateTime.Now;
        bool changed = false;

        foreach (var todo in State.Todos)
        {
            if (todo.Done || !todo.RemindAt.HasValue || todo.Notified) continue;
            var remind = todo.RemindAt.Value;
            if (remind > now || remind.Date != now.Date) continue;

            todo.Notified = true;
            ShowToast("待办提醒", $"{todo.DisplayTitle} · {remind:HH:mm}");
            changed = true;
        }

        foreach (var day in State.Anniversaries)
        {
            int days = day.DaysLeft;
            if (days == 0 && day.LastNotifiedYear != day.NextDate.Year)
            {
                day.LastNotifiedYear = day.NextDate.Year;
                ShowToast("今天是个好日子", $"{day.Title} · 就是今天");
                changed = true;
            }
            else if (days > 0 && days <= day.RemindDaysBefore && day.LastAdvanceYear != day.NextDate.Year)
            {
                day.LastAdvanceYear = day.NextDate.Year;
                ShowToast("纪念日提醒", $"{day.Title} · 还有 {days} 天");
                changed = true;
            }
        }

        if (changed) State.Notify();
    }

    // ================= 剪贴板浮窗 =================

    public static void ShowClipboardPopup()
    {
        try
        {
            if (_clipboardPopup == null || !_clipboardPopup.IsLoaded)
            {
                _clipboardPopup = new ClipboardPopupWindow();
                _clipboardPopup.Closed += (_, _) => _clipboardPopup = null;
            }

            if (_clipboardPopup.IsVisible)
            {
                _clipboardPopup.Hide();
                return;
            }

            _clipboardPopup.Open();
        }
        catch
        {
        }
    }

    // ================= 计薪桌面组件 =================

    public static void ShowSalaryWidget(bool activate = true)
    {
        try
        {
            if (_salaryWidget == null || !_salaryWidget.IsLoaded)
            {
                _salaryWidget = new SalaryWidgetWindow();
            }

            if (!_salaryWidget.IsVisible) _salaryWidget.Show();
            if (activate) _salaryWidget.Activate();
            State.Settings.SalaryWidgetVisible = true;
        }
        catch
        {
        }
    }

    public static void ClearSalaryWidget() => _salaryWidget = null;

    public static void HideSalaryWidget()
    {
        _salaryWidget?.Hide();
        if (State != null) State.Settings.SalaryWidgetVisible = false;
    }

    // ================= 提示弹窗 =================

    public static void ShowToast(string title, string message)
    {
        try
        {
            _toast ??= new ToastWindow();
            _toast.ShowToast(title, message);
            if (State?.Settings.SoundEnabled == true)
            {
                try { System.Media.SystemSounds.Asterisk.Play(); } catch { }
            }
        }
        catch
        {
        }
    }

    // ================= 退出 =================

    protected override void OnExit(ExitEventArgs e)
    {
        Trace("OnExit " + e.ApplicationExitCode);
        IsShuttingDown = true;
        if (State != null) StorageService.SaveNow(State);
        ReminderTimer?.Stop();
        ClipboardService.Current.Dispose();
        Tray?.Dispose();
        _showEvent?.Dispose();
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }

    public static void Trace(string message)
    {
        try
        {
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "UToolBar_startup.log"),
                $"[{DateTime.Now:HH:mm:ss}] {message}\r\n");
        }
        catch
        {
        }
    }

    private static void LogError(Exception ex)
    {        try
        {
            Directory.CreateDirectory(StorageService.Folder);
            File.AppendAllText(
                Path.Combine(StorageService.Folder, "error.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\r\n\r\n");
        }
        catch
        {
        }
    }
}
