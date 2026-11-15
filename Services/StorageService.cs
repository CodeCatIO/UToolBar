using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows.Threading;
using UToolBar.Models;

namespace UToolBar.Services;

public static class StorageService
{
    private static DispatcherTimer _timer;
    private static AppState _pending;
    private static readonly object Gate = new();
    private static string _folder;

    private const string LocationFileName = "location.txt";
    private const string PendingDeleteFileName = "pending-delete.txt";

    public static string DefaultFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UToolBar");

    // 位置记录放在 LocalAppData，避免删除旧数据目录时把记录一起删掉
    private static string AnchorFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UToolBar");

    private static string LocationFile => Path.Combine(AnchorFolder, LocationFileName);
    private static string LegacyLocationFile => Path.Combine(DefaultFolder, LocationFileName);
    private static string PendingDeleteFile => Path.Combine(AnchorFolder, PendingDeleteFileName);

    // 从旧名字 EdgeBar 升级时用于迁移
    private static string OldDefaultFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EdgeBar");

    private static string OldAnchorFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EdgeBar");

    /// <summary>数据目录：默认在 %APPDATA%\UToolBar，可在设置里改到别的盘</summary>
    public static string Folder
    {
        get
        {
            if (_folder != null) return _folder;

            MigrateFromEdgeBar();
            _folder = DefaultFolder;
            try
            {
                string custom = null;
                if (File.Exists(LocationFile))
                {
                    custom = File.ReadAllText(LocationFile).Trim();
                }
                else if (File.Exists(LegacyLocationFile))
                {
                    // 兼容旧版放在数据目录里的位置记录
                    custom = File.ReadAllText(LegacyLocationFile).Trim();
                    if (!string.IsNullOrWhiteSpace(custom))
                    {
                        Directory.CreateDirectory(AnchorFolder);
                        File.WriteAllText(LocationFile, custom);
                    }
                }

                if (!string.IsNullOrWhiteSpace(custom))
                {
                    Directory.CreateDirectory(custom);
                    _folder = custom;
                }
            }
            catch
            {
                _folder = DefaultFolder;
            }
            return _folder;
        }
    }

    /// <summary>从 2.2 及更早版本的 EdgeBar 数据目录迁移一次，旧目录保留作备份</summary>
    private static void MigrateFromEdgeBar()
    {
        try
        {
            if (!File.Exists(LocationFile))
            {
                var oldLocation = Path.Combine(OldAnchorFolder, LocationFileName);
                if (!File.Exists(oldLocation))
                    oldLocation = Path.Combine(OldDefaultFolder, LocationFileName);

                if (File.Exists(oldLocation))
                {
                    var custom = File.ReadAllText(oldLocation).Trim();
                    if (!string.IsNullOrWhiteSpace(custom))
                    {
                        Directory.CreateDirectory(AnchorFolder);
                        File.WriteAllText(LocationFile, custom);
                    }
                }
            }

            // 用户把数据放在自定义目录时，不用复制默认目录
            if (File.Exists(LocationFile)) return;

            var oldData = Path.Combine(OldDefaultFolder, "data.json");
            if (!File.Exists(Path.Combine(DefaultFolder, "data.json")) && File.Exists(oldData))
            {
                Directory.CreateDirectory(DefaultFolder);
                File.Copy(oldData, Path.Combine(DefaultFolder, "data.json"));

                var oldBackup = oldData + ".bak";
                if (File.Exists(oldBackup))
                    File.Copy(oldBackup, Path.Combine(DefaultFolder, "data.json.bak"));

                CopyDirectory(Path.Combine(OldDefaultFolder, "clips"),
                    Path.Combine(DefaultFolder, "clips"));
            }
        }
        catch
        {
            // 迁移失败不影响新版本启动，用户仍可手动把 data.json 复制过来
        }
    }

    private static void CopyDirectory(string source, string target)
    {
        if (!Directory.Exists(source)) return;
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source))
        {
            var destination = Path.Combine(target, Path.GetFileName(file));
            if (!File.Exists(destination)) File.Copy(file, destination);
        }
        foreach (var folder in Directory.GetDirectories(source))
            CopyDirectory(folder, Path.Combine(target, Path.GetFileName(folder)));
    }

    public static string DataPath => Path.Combine(Folder, "data.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static AppState Load()
    {
        ProcessPendingDelete();

        try
        {
            Directory.CreateDirectory(Folder);
            if (File.Exists(DataPath))
            {
                var json = File.ReadAllText(DataPath);
                var state = JsonSerializer.Deserialize<AppState>(json, Options);
                if (state != null) return Normalize(state);
            }
        }
        catch
        {
            // 主文件损坏时尝试备份
            try
            {
                var bak = DataPath + ".bak";
                if (File.Exists(bak))
                {
                    var state = JsonSerializer.Deserialize<AppState>(File.ReadAllText(bak), Options);
                    if (state != null) return Normalize(state);
                }
            }
            catch
            {
                // 忽略，使用全新数据
            }
        }

        return new AppState();
    }

    private static AppState Normalize(AppState s)
    {
        s.Todos ??= new();
        s.Anniversaries ??= new();
        s.Notes ??= new();
        s.Clips ??= new();
        s.Salary ??= new();
        s.Settings ??= new();

        foreach (var t in s.Todos)
        {
            t.Subtasks ??= new();
            t.Date = t.Date.Date;
        }
        return s;
    }

    /// <summary>600ms 防抖保存，避免频繁写盘</summary>
    public static void SaveLater(AppState state)
    {
        _pending = state;
        if (_timer == null)
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            _timer.Tick += (_, _) =>
            {
                _timer.Stop();
                if (_pending != null) SaveNow(_pending);
            };
        }
        _timer.Stop();
        _timer.Start();
    }

    public static void SaveNow(AppState state)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Folder);
                var json = JsonSerializer.Serialize(state, Options);
                var tmp = DataPath + ".tmp";
                File.WriteAllText(tmp, json);
                if (File.Exists(DataPath))
                {
                    try { File.Copy(DataPath, DataPath + ".bak", true); } catch { }
                }
                File.Move(tmp, DataPath, true);
            }
        }
        catch
        {
            // 保存失败不打断使用
        }
    }

    public static string Serialize(AppState state) => JsonSerializer.Serialize(state, Options);

    /// <summary>把云端拉下来的 JSON 写入本地，写入前保留一份带时间戳的备份</summary>
    public static bool Import(string json, out string error)
    {
        try
        {
            var state = JsonSerializer.Deserialize<AppState>(json, Options);
            if (state == null)
            {
                error = "云端文件不是有效的 UTB 数据";
                return false;
            }
            Normalize(state);

            Directory.CreateDirectory(Folder);
            if (File.Exists(DataPath))
            {
                var backup = Path.Combine(Folder, $"data.backup-{DateTime.Now:yyyyMMdd-HHmmss}.json");
                try { File.Copy(DataPath, backup, true); } catch { }
            }
            File.WriteAllText(DataPath, json);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>修改数据目录：先把 data.json 和剪贴板图片复制过去，再记住新位置</summary>
    public static bool SetFolder(string target, out string error)
    {
        error = null;
        try
        {
            if (string.IsNullOrWhiteSpace(target))
            {
                error = "请选择一个文件夹";
                return false;
            }

            target = Path.GetFullPath(target.Trim());
            string current = Path.GetFullPath(Folder);
            if (string.Equals(Path.TrimEndingDirectorySeparator(target),
                              Path.TrimEndingDirectorySeparator(current),
                              StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            Directory.CreateDirectory(target);
            CopyDataFiles(current, target);

            Directory.CreateDirectory(AnchorFolder);
            File.WriteAllText(LocationFile, target);
            _folder = target;

            // 迁移完成后直接清理旧数据目录
            DeleteOldFolder(current, target);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static void DeleteOldFolder(string oldFolder, string newFolder)
    {
        string oldRoot = Path.TrimEndingDirectorySeparator(oldFolder);
        string newRoot = Path.TrimEndingDirectorySeparator(newFolder);

        if (string.Equals(oldRoot, newRoot, StringComparison.OrdinalIgnoreCase)) return;
        if (newRoot.StartsWith(oldRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return;
        if (oldRoot.StartsWith(newRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return;

        try
        {
            Directory.Delete(oldRoot, true);
        }
        catch
        {
            // 有文件被占用时，记下来下次启动再删
            try
            {
                Directory.CreateDirectory(AnchorFolder);
                File.WriteAllText(PendingDeleteFile, oldRoot);
            }
            catch
            {
            }
        }
    }

    private static void ProcessPendingDelete()
    {
        try
        {
            if (!File.Exists(PendingDeleteFile)) return;

            string path = File.ReadAllText(PendingDeleteFile).Trim();
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            {
                string current = Path.TrimEndingDirectorySeparator(Folder);
                string pending = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
                bool safe = !string.Equals(current, pending, StringComparison.OrdinalIgnoreCase) &&
                            !current.StartsWith(pending + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
                if (safe) Directory.Delete(pending, true);
            }
            File.Delete(PendingDeleteFile);
        }
        catch
        {
        }
    }

    private static void CopyDataFiles(string source, string target)
    {
        string sourceRoot = Path.TrimEndingDirectorySeparator(source);
        string targetRoot = Path.TrimEndingDirectorySeparator(target);
        if (targetRoot.StartsWith(sourceRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return; // 目标在源目录内部，不递归复制

        foreach (string name in new[] { "data.json", "data.json.bak" })
        {
            string from = Path.Combine(sourceRoot, name);
            if (File.Exists(from))
                File.Copy(from, Path.Combine(targetRoot, name), true);
        }

        string clipsFrom = Path.Combine(sourceRoot, "clips");
        if (Directory.Exists(clipsFrom))
        {
            string clipsTo = Path.Combine(targetRoot, "clips");
            Directory.CreateDirectory(clipsTo);
            foreach (string file in Directory.EnumerateFiles(clipsFrom))
                File.Copy(file, Path.Combine(clipsTo, Path.GetFileName(file)), true);
        }
    }

    public static void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = Folder,
                UseShellExecute = true
            });
        }
        catch
        {
        }
    }
}
