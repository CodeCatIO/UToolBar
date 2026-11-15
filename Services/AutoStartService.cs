using Microsoft.Win32;

namespace UToolBar.Services;

public static class AutoStartService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "UToolBar";
    private const string OldValueName = "EdgeBar";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
            return key?.GetValue(ValueName) != null || key?.GetValue(OldValueName) != null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>把旧版 EdgeBar 的开机自启值迁移到 UToolBar，并清掉旧值</summary>
    public static void Migrate()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
            if (key == null) return;

            bool hadOld = key.GetValue(OldValueName) != null;
            if (!hadOld) return;

            if (key.GetValue(ValueName) == null)
            {
                var exe = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exe))
                    key.SetValue(ValueName, $"\"{exe}\"");
            }
            key.DeleteValue(OldValueName, false);
        }
        catch
        {
        }
    }

    public static void Apply(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
            if (key == null) return;
            if (enabled)
            {
                var exe = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exe))
                    key.SetValue(ValueName, $"\"{exe}\"");
            }
            else
            {
                key.DeleteValue(ValueName, false);
            }
            key.DeleteValue(OldValueName, false);
        }
        catch
        {
            // 权限问题时不弹窗，静默失败
        }
    }
}
