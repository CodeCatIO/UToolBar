using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using UToolBar.Services;

namespace UToolBar.Views;

public partial class PinWindow : Window
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmSysKeyDown = 0x0104;

    private const int VkLeft = 0x25;
    private const int VkUp = 0x26;
    private const int VkRight = 0x27;
    private const int VkDown = 0x28;
    private const int VkAdd = 0x6B;
    private const int VkSubtract = 0x6D;
    private const int VkOemPlus = 0xBB;
    private const int VkOemMinus = 0xBD;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;

    private int _historyIndex = -1;
    private double _aspect;
    private IntPtr _hook;
    private NativeMethods.HookProc _hookProc;

    public PinWindow(BitmapSource image) : this(image, -1)
    {
    }

    /// <summary>从最近截图历史里贴图，可用 - / + 翻看前后几张</summary>
    public PinWindow(int historyIndex) : this(ScreenshotHistory.Load(historyIndex), historyIndex)
    {
    }

    public PinWindow(BitmapSource image, int historyIndex)
    {
        InitializeComponent();
        if (image == null) throw new InvalidOperationException("最近一张截图已经不在了");

        _historyIndex = historyIndex;
        ApplyImage(image);

        var area = SystemParameters.WorkArea;
        Left = Math.Max(area.Left + 40, area.Right - Width - 120);
        Top = Math.Max(area.Top + 60, area.Top + 120);

        Loaded += (_, _) => InstallHook();
        Closed += (_, _) => UninstallHook();
    }

    private void ApplyImage(BitmapSource image)
    {
        Pic.Source = image;
        _aspect = image.PixelHeight / (double)image.PixelWidth;

        double scale = Math.Min(1.0, Math.Min(520.0 / image.PixelWidth, 420.0 / image.PixelHeight));
        Width = Math.Max(120, image.PixelWidth * scale);
        Height = Math.Max(80, image.PixelHeight * scale);
    }

    // ================= 键盘：方向键微调，- / + 翻历史 =================

    private void InstallHook()
    {
        if (_hook != IntPtr.Zero) return;
        _hookProc = OnKeyboardHook;
        _hook = NativeMethods.SetWindowsHookEx(WhKeyboardLl, _hookProc, NativeMethods.GetModuleHandle(null), 0);
    }

    private void UninstallHook()
    {
        if (_hook == IntPtr.Zero) return;
        NativeMethods.UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
        _hookProc = null;
    }

    private IntPtr OnKeyboardHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == (IntPtr)WmKeyDown || wParam == (IntPtr)WmSysKeyDown))
        {
            try
            {
                bool hovered = IsMouseOver;
                if (hovered && HandleKey(Marshal.PtrToStructure<NativeMethods.KbdLlHookStruct>(lParam).vkCode))
                {
                    return (IntPtr)1;
                }
            }
            catch
            {
            }
        }

        return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private bool HandleKey(int vk)
    {
        // 带 Ctrl / Alt 的组合键交给系统和其他程序
        if (NativeMethods.IsKeyDown(VkControl) || NativeMethods.IsKeyDown(VkMenu)) return false;

        var area = ScreenBounds();
        switch (vk)
        {
            case VkLeft:
                Left = Math.Max(area.Left, Left - 1);
                return true;
            case VkRight:
                Left = Math.Min(area.Right - Width, Left + 1);
                return true;
            case VkUp:
                Top = Math.Max(area.Top, Top - 1);
                return true;
            case VkDown:
                Top = Math.Min(area.Bottom - Height, Top + 1);
                return true;
            case VkOemMinus:
            case VkSubtract:
                Switch(-1);
                return true;
            case VkOemPlus:
            case VkAdd:
                Switch(1);
                return true;
            default:
                return false;
        }
    }

    private Rect ScreenBounds()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var screen = System.Windows.Forms.SystemInformation.VirtualScreen;
        return new Rect(
            screen.Left / dpi.DpiScaleX,
            screen.Top / dpi.DpiScaleY,
            screen.Width / dpi.DpiScaleX,
            screen.Height / dpi.DpiScaleY);
    }

    private void Switch(int delta)
    {
        int count = ScreenshotHistory.Count;
        if (_historyIndex < 0 || count < 2) return;

        int next = Math.Clamp(_historyIndex + delta, 0, count - 1);
        if (next == _historyIndex) return;

        var image = ScreenshotHistory.Load(next);
        if (image == null) return;

        _historyIndex = next;
        ApplyImage(image);
    }

    // ================= 鼠标 =================

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

    private static class NativeMethods
    {
        public delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct KbdLlHookStruct
        {
            public int vkCode;
            public int scanCode;
            public int flags;
            public int time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        private static extern short GetKeyState(int nVirtKey);

        public static bool IsKeyDown(int vk) => (GetKeyState(vk) & 0x8000) != 0;
    }
}
