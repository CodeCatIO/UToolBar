using System.Drawing;
using System.Windows.Forms;

namespace UToolBar.Services;

/// <summary>系统托盘图标与右键菜单</summary>
public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _autoStartItem;

    public event Action OpenRequested;
    public event Action NewNoteRequested;
    public event Action RecordRequested;
    public event Action ExitRequested;

    public TrayService()
    {
        var menu = new ContextMenuStrip
        {
            ShowImageMargin = false,
            Font = new Font("Microsoft YaHei UI", 9f)
        };

        var open = new ToolStripMenuItem("打开 UToolBar 面板") { Font = new Font(menu.Font, FontStyle.Bold) };
        open.Click += (_, _) => OpenRequested?.Invoke();

        var note = new ToolStripMenuItem("新建便签");
        note.Click += (_, _) => NewNoteRequested?.Invoke();

        var record = new ToolStripMenuItem("开始录屏");
        record.Click += (_, _) => RecordRequested?.Invoke();

        _autoStartItem = new ToolStripMenuItem("开机自启") { CheckOnClick = true };
        _autoStartItem.Click += (_, _) => AutoStartService.Apply(_autoStartItem.Checked);

        var exit = new ToolStripMenuItem("退出 UToolBar");
        exit.Click += (_, _) => ExitRequested?.Invoke();

        menu.Items.Add(open);
        menu.Items.Add(note);
        menu.Items.Add(record);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_autoStartItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exit);

        _icon = new NotifyIcon
        {
            Text = "UToolBar · 统一工具条",
            Visible = true,
            Icon = LoadIcon(),
            ContextMenuStrip = menu
        };
        _icon.DoubleClick += (_, _) => OpenRequested?.Invoke();
    }

    public void SetAutoStartChecked(bool value) => _autoStartItem.Checked = value;

    private static Icon LoadIcon()
    {
        try
        {
            var path = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(path))
            {
                var icon = Icon.ExtractAssociatedIcon(path);
                if (icon != null) return icon;
            }
        }
        catch
        {
        }
        return SystemIcons.Application;
    }

    public void ShowBalloon(string title, string text)
    {
        try
        {
            _icon.ShowBalloonTip(3000, title, text, ToolTipIcon.None);
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        try
        {
            _icon.Visible = false;
            _icon.Dispose();
        }
        catch
        {
        }
    }
}
