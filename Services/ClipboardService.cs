using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using UToolBar.Models;

namespace UToolBar.Services;

/// <summary>监听剪贴板变化并保存历史，自动跳过密码管理器标记的内容</summary>
public sealed class ClipboardService : IDisposable
{
    private const int WmClipboardUpdate = 0x031D;
    private const int MaxItems = 60;

    private HwndSource _source;
    private DateTime _ignoreUntil = DateTime.MinValue;
    private int _retry;

    public static ClipboardService Current { get; } = new();

    public static string ImageFolder => Path.Combine(StorageService.Folder, "clips");

    public void Attach(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(handle);
        _source?.AddHook(Hook);
        AddClipboardFormatListener(handle);
        LoadStoredImages();
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmClipboardUpdate)
        {
            Application.Current?.Dispatcher.BeginInvoke(new Action(ReadClipboard), DispatcherPriority.Background);
        }
        return IntPtr.Zero;
    }

    private void ReadClipboard()
    {
        if (DateTime.Now < _ignoreUntil) return;
        if (App.State == null) return;

        try
        {
            var data = Clipboard.GetDataObject();
            if (data == null) return;

            if (data.GetDataPresent("ExcludeClipboardContentFromMonitorProcessing") ||
                data.GetDataPresent("Clipboard Viewer Ignore") ||
                data.GetDataPresent("ExcludeClipboardContentFromMonitorProcessing"))
                return;

            if (data.GetDataPresent("CanIncludeInClipboardHistory"))
            {
                var flag = data.GetData("CanIncludeInClipboardHistory");
                if (flag is int i && i == 0) return;
                if (flag is byte[] bytes && bytes.Length > 0 && bytes[0] == 0) return;
            }

            if (Clipboard.ContainsText())
            {
                var text = Clipboard.GetText();
                if (string.IsNullOrWhiteSpace(text)) return;
                if (text.Length > 50000) text = text[..50000];

                var last = App.State.Clips.FirstOrDefault();
                if (last != null && last.Kind == "text" && last.Text == text) return;

                App.State.Clips.Insert(0, new ClipItem
                {
                    Kind = "text",
                    Text = text,
                    CreatedAt = DateTime.Now
                });
                Trim();
                return;
            }

            if (Clipboard.ContainsImage())
            {
                var image = Clipboard.GetImage();
                if (image == null) return;
                image.Freeze();

                var item = new ClipItem
                {
                    Kind = "image",
                    Image = image,
                    ImageWidth = image.PixelWidth,
                    ImageHeight = image.PixelHeight,
                    ImagePath = $"{Guid.NewGuid():N}.png",
                    CreatedAt = DateTime.Now
                };
                SaveImage(item, image);

                // 内存里只留缩略图，原图按需从文件加载
                try
                {
                    item.Image = LoadThumbnail(Path.Combine(ImageFolder, item.ImagePath));
                }
                catch
                {
                }

                App.State.Clips.Insert(0, item);
                Trim();
            }
        }
        catch
        {
            // 剪贴板可能被其他程序短暂占用，延迟重试（不能把 TimeSpan 当成委托参数传进去）
            if (_retry++ < 4)
            {
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    ReadClipboard();
                };
                timer.Start();
            }
            else
            {
                _retry = 0;
            }
        }
    }

    private void Trim()
    {
        while (App.State.Clips.Count > MaxItems)
        {
            var last = App.State.Clips[^1];
            App.State.Clips.RemoveAt(App.State.Clips.Count - 1);
            DeleteImage(last);
        }
        App.State.Notify();
    }

    public static void CopyToClipboard(ClipItem item)
    {
        if (item == null) return;
        Current._ignoreUntil = DateTime.Now.AddMilliseconds(700);
        try
        {
            if (item.Kind == "image")
            {
                var image = LoadFullImage(item);
                if (image != null) Clipboard.SetImage(image);
            }
            else
            {
                Clipboard.SetText(item.Text ?? "");
            }
        }
        catch
        {
        }
    }

    /// <summary>按需加载原图（复制、贴桌面、再次编辑时用）</summary>
    public static BitmapSource LoadFullImage(ClipItem item)
    {
        if (item == null) return null;
        if (item.Kind != "image" || string.IsNullOrEmpty(item.ImagePath)) return item.Image;

        try
        {
            var path = Path.Combine(ImageFolder, item.ImagePath);
            if (!File.Exists(path)) return item.Image;

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return item.Image;
        }
    }

    private const int ThumbnailWidth = 480;

    private static BitmapSource LoadThumbnail(string path)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.DecodePixelWidth = ThumbnailWidth;
        bitmap.UriSource = new Uri(path, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>只读 PNG 头拿原图尺寸，不解码整张图</summary>
    private static bool TryReadPngSize(string path, out int width, out int height)
    {
        width = height = 0;
        try
        {
            using var stream = File.OpenRead(path);
            var header = new byte[24];
            if (stream.Read(header, 0, header.Length) < header.Length) return false;
            if (header[0] != 0x89 || header[1] != 0x50 || header[2] != 0x4E || header[3] != 0x47) return false;

            width = (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
            height = (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23];
            return width > 0 && height > 0;
        }
        catch
        {
            return false;
        }
    }

    public static void Remove(ClipItem item)
    {
        if (item == null || App.State == null) return;
        App.State.Clips.Remove(item);
        DeleteImage(item);
        App.State.Notify();
    }

    public static void ClearAll()
    {
        if (App.State == null) return;
        foreach (var item in App.State.Clips.ToList()) DeleteImage(item);
        App.State.Clips.Clear();
        App.State.Notify();
    }

    private static void SaveImage(ClipItem item, BitmapSource image)
    {
        try
        {
            Directory.CreateDirectory(ImageFolder);
            var path = Path.Combine(ImageFolder, item.ImagePath);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using var stream = File.Create(path);
            encoder.Save(stream);
        }
        catch
        {
        }
    }

    private static void DeleteImage(ClipItem item)
    {
        try
        {
            if (string.IsNullOrEmpty(item.ImagePath)) return;
            var path = Path.Combine(ImageFolder, item.ImagePath);
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
        }
    }

    private void LoadStoredImages()
    {
        if (App.State == null) return;
        Directory.CreateDirectory(ImageFolder);

        foreach (var item in App.State.Clips)
        {
            if (item.Kind != "image" || string.IsNullOrEmpty(item.ImagePath)) continue;
            try
            {
                var path = Path.Combine(ImageFolder, item.ImagePath);
                if (!File.Exists(path)) continue;

                item.Image = LoadThumbnail(path);
                if (item.ImageWidth <= 0 && TryReadPngSize(path, out int width, out int height))
                {
                    item.ImageWidth = width;
                    item.ImageHeight = height;
                }
                item.RaiseAll();
            }
            catch
            {
            }
        }
    }

    public void Dispose()
    {
        if (_source == null) return;
        try
        {
            RemoveClipboardFormatListener(_source.Handle);
            _source.RemoveHook(Hook);
        }
        catch
        {
        }
        _source = null;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
}
