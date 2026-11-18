using System.IO;
using System.Windows.Media.Imaging;

namespace UToolBar.Services;

/// <summary>
/// 最近截图历史：把截图按 PNG 落到本地，最多保留 8 张。
/// 存文件而不是常驻内存，避免大图把工作集撑起来。
/// </summary>
public static class ScreenshotHistory
{
    private const int MaxItems = 8;

    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "UToolBar", "shots");

    private static readonly List<string> Files = new();
    private static bool _loaded;

    /// <summary>最近一次加入截图的时间，用于和剪贴板图片比较谁更新</summary>
    public static DateTime LastAddedAt { get; private set; } = DateTime.MinValue;

    public static int Count
    {
        get
        {
            EnsureLoaded();
            return Files.Count;
        }
    }

    public static void Add(BitmapSource image)
    {
        if (image == null || image.PixelWidth <= 0 || image.PixelHeight <= 0) return;

        try
        {
            Directory.CreateDirectory(Folder);
            string file = Path.Combine(Folder, $"shot_{DateTime.Now:yyyyMMdd_HHmmssfff}.png");
            int suffix = 1;
            while (File.Exists(file))
            {
                file = Path.Combine(Folder, $"shot_{DateTime.Now:yyyyMMdd_HHmmssfff}_{suffix++}.png");
            }

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using (var stream = File.Create(file)) encoder.Save(stream);

            EnsureLoaded();
            Files.Add(file);
            LastAddedAt = DateTime.Now;
            Trim();
        }
        catch
        {
        }
    }

    public static BitmapSource Load(int index)
    {
        EnsureLoaded();
        if (index < 0 || index >= Files.Count) return null;

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(Files[index]);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;

        try
        {
            Directory.CreateDirectory(Folder);
            Files.AddRange(Directory.GetFiles(Folder, "shot_*.png").OrderBy(f => f, StringComparer.Ordinal));
            if (Files.Count > 0)
            {
                try { LastAddedAt = Files.Select(File.GetLastWriteTime).Max(); } catch { }
            }
            Trim();
        }
        catch
        {
        }
    }

    private static void Trim()
    {
        while (Files.Count > MaxItems)
        {
            string oldest = Files[0];
            Files.RemoveAt(0);
            try { File.Delete(oldest); } catch { }
        }
    }
}
