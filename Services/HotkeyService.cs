using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace UToolBar.Services;

/// <summary>注册全局快捷键：Alt+N 新建便签，Alt+T 贴出最近截图，Ctrl+Alt+B 展开/收起面板</summary>
public sealed class HotkeyService : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModNoRepeat = 0x4000;

    private HwndSource _source;
    private const int NewNoteId = 9171;
    private const int ToggleId = 9172;
    private const int ScreenshotId = 9173;
    private const int ClipboardId = 9174;
    private const int RecordId = 9175;
    private const int PinRecentId = 9176;

    public event Action NewNoteRequested;
    public event Action ToggleRequested;
    public event Action ScreenshotRequested;
    public event Action ClipboardRequested;
    public event Action RecordRequested;
    public event Action PinRecentRequested;

    public bool NewNoteRegistered { get; private set; }
    public bool ToggleRegistered { get; private set; }
    public bool ScreenshotRegistered { get; private set; }
    public bool ClipboardRegistered { get; private set; }
    public bool RecordRegistered { get; private set; }
    public bool PinRecentRegistered { get; private set; }

    public void Attach(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(handle);
        _source?.AddHook(Hook);

        NewNoteRegistered = RegisterHotKey(handle, NewNoteId, ModAlt | ModNoRepeat, 0x4E /* N */);
        PinRecentRegistered = RegisterHotKey(handle, PinRecentId, ModAlt | ModNoRepeat, 0x54 /* T */);
        ToggleRegistered = RegisterHotKey(handle, ToggleId, ModControl | ModAlt | ModNoRepeat, 0x42 /* B */);
        ScreenshotRegistered = RegisterHotKey(handle, ScreenshotId, ModControl | ModAlt | ModNoRepeat, 0x41 /* A */);
        ClipboardRegistered = RegisterHotKey(handle, ClipboardId, ModControl | ModAlt | ModNoRepeat, 0x56 /* V */);
        RecordRegistered = RegisterHotKey(handle, RecordId, ModControl | ModAlt | ModNoRepeat, 0x53 /* S */);
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey)
        {
            int id = wParam.ToInt32();
            if (id == NewNoteId)
            {
                NewNoteRequested?.Invoke();
                handled = true;
            }
            else if (id == PinRecentId)
            {
                PinRecentRequested?.Invoke();
                handled = true;
            }
            else if (id == ToggleId)
            {
                ToggleRequested?.Invoke();
                handled = true;
            }
            else if (id == ScreenshotId)
            {
                ScreenshotRequested?.Invoke();
                handled = true;
            }
            else if (id == ClipboardId)
            {
                ClipboardRequested?.Invoke();
                handled = true;
            }
            else if (id == RecordId)
            {
                RecordRequested?.Invoke();
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_source == null) return;
        var handle = _source.Handle;
        if (NewNoteRegistered) UnregisterHotKey(handle, NewNoteId);
        if (PinRecentRegistered) UnregisterHotKey(handle, PinRecentId);
        if (ToggleRegistered) UnregisterHotKey(handle, ToggleId);
        if (ScreenshotRegistered) UnregisterHotKey(handle, ScreenshotId);
        if (ClipboardRegistered) UnregisterHotKey(handle, ClipboardId);
        if (RecordRegistered) UnregisterHotKey(handle, RecordId);
        _source.RemoveHook(Hook);
        _source = null;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
