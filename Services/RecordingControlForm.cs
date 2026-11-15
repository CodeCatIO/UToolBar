using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using WinForms = System.Windows.Forms;

namespace EdgeBar.Services;

/// <summary>录制中的控制条：计时与停止。通过 WDA_EXCLUDEFROMCAPTURE 排除出录屏画面。</summary>
public sealed class RecordingControlForm : WinForms.Form
{
    private const uint WdaExcludeFromCapture = 0x11;

    private readonly WinForms.Timer _timer;
    private readonly Rectangle _region;
    private readonly Rectangle _stopRect = new(126, 12, 62, 32);
    private string _elapsed = "00:00";
    private bool _hoverStop;
    private bool _stopping;

    public RecordingControlForm(Rectangle region)
    {
        _region = region;

        FormBorderStyle = WinForms.FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = WinForms.FormStartPosition.Manual;
        AutoScaleMode = WinForms.AutoScaleMode.None;
        ClientSize = new Size(200, 56);
        BackColor = Color.FromArgb(27, 30, 36);
        DoubleBuffered = true;
        SetStyle(WinForms.ControlStyles.OptimizedDoubleBuffer |
                 WinForms.ControlStyles.AllPaintingInWmPaint |
                 WinForms.ControlStyles.UserPaint, true);

        using var path = RoundedRect(new Rectangle(0, 0, Width, Height), 16);
        Region = new Region(path);

        _timer = new WinForms.Timer { Interval = 400 };
        _timer.Tick += (_, _) => UpdateElapsed();
        _timer.Start();

        RecordingService.Saved += OnSaved;
        RecordingService.Failed += OnFailed;
        FormClosed += (_, _) =>
        {
            _timer.Stop();
            _timer.Dispose();
            RecordingService.Saved -= OnSaved;
            RecordingService.Failed -= OnFailed;
        };
    }

    public static RecordingControlForm ShowFor(Rectangle region)
    {
        var form = new RecordingControlForm(region);
        form.Show();
        form.PositionNearRegion();
        form.UpdateElapsed();
        return form;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try { SetWindowDisplayAffinity(Handle, WdaExcludeFromCapture); } catch { }
    }

    private void PositionNearRegion()
    {
        var monitor = WinForms.Screen.FromRectangle(_region).Bounds;
        int left = _region.Left + _region.Width / 2 - Width / 2;
        int top = _region.Top + 18;
        if (top + Height > monitor.Bottom) top = monitor.Bottom - Height - 12;
        left = Math.Max(monitor.Left, Math.Min(left, monitor.Right - Width));
        Location = new Point(left, top);
    }

    private void UpdateElapsed()
    {
        var elapsed = DateTime.Now - RecordingService.StartedAt;
        _elapsed = elapsed.TotalHours >= 1
            ? elapsed.ToString(@"h\:mm\:ss")
            : elapsed.ToString(@"mm\:ss");
        Invalidate();
    }

    protected override void OnPaint(WinForms.PaintEventArgs e)
    {
        base.OnPaint(e);
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        using (var border = new Pen(Color.FromArgb(58, 64, 74), 1))
        {
            using var path = RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), 16);
            graphics.DrawPath(border, path);
        }

        using (var dot = new SolidBrush(Color.FromArgb(255, 77, 79)))
            graphics.FillEllipse(dot, 20, 24, 9, 9);

        using (var font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold))
        using (var text = new SolidBrush(Color.White))
            graphics.DrawString(_elapsed, font, text, 37, 17);

        using (var divider = new Pen(Color.FromArgb(60, 255, 255, 255), 1))
            graphics.DrawLine(divider, 112, 16, 112, 40);

        using (var path = RoundedRect(_stopRect, 16))
        {
            var fillColor = _stopping
                ? Color.FromArgb(120, 90, 92)
                : _hoverStop ? Color.FromArgb(214, 60, 64) : Color.FromArgb(240, 74, 79);
            using var fill = new SolidBrush(fillColor);
            graphics.FillPath(fill, path);

            var label = _stopping ? "保存中…" : "停止";
            using var font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold);
            var size = graphics.MeasureString(label, font);
            using var text = new SolidBrush(Color.White);
            graphics.DrawString(label, font, text,
                _stopRect.Left + (_stopRect.Width - size.Width) / 2,
                _stopRect.Top + (_stopRect.Height - size.Height) / 2);
        }
    }

    private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        int diameter = radius * 2;
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    protected override void OnMouseMove(WinForms.MouseEventArgs e)
    {
        base.OnMouseMove(e);
        bool hover = !_stopping && _stopRect.Contains(e.Location);
        if (hover != _hoverStop)
        {
            _hoverStop = hover;
            Cursor = hover ? WinForms.Cursors.Hand : WinForms.Cursors.Default;
            Invalidate(_stopRect);
        }
    }

    protected override void OnMouseDown(WinForms.MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != WinForms.MouseButtons.Left || _stopping) return;
        if (!_stopRect.Contains(e.Location)) return;

        _stopping = true;
        _hoverStop = false;
        Cursor = WinForms.Cursors.Default;
        Invalidate(_stopRect);
        RecordingService.Stop();
    }

    private void OnSaved(string path)
    {
        BeginInvoke(new Action(() =>
        {
            Close();
            App.ShowToast("录屏已保存", Path.GetFileName(path));
        }));
    }

    private void OnFailed(string message)
    {
        BeginInvoke(new Action(() =>
        {
            Close();
            App.ShowToast("录制失败", message);
        }));
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
}
