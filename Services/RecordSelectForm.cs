using System.Drawing;
using System.Drawing.Drawing2D;
using EdgeBar.Services;
using EdgeBar.Views;
using WinForms = System.Windows.Forms;

namespace EdgeBar.Services;

/// <summary>录屏选区：单击录制整个屏幕，拖动自定义范围，四周圆点可调整</summary>
public sealed class RecordSelectForm : WinForms.Form
{
    private enum DragMode
    {
        None,
        New,
        Move,
        Resize
    }

    private readonly Bitmap _snapshot;
    private readonly Rectangle _bounds;
    private RecordToolbarWindow _toolbar;

    private Rectangle _selection;
    private Rectangle _monitor;
    private bool _hasSelection;
    private DragMode _mode;
    private int _handle = -1;
    private Point _dragStart;
    private Rectangle _dragOrigin;
    private bool _started;

    public RecordSelectForm(Bitmap snapshot, Rectangle virtualBounds)
    {
        _snapshot = snapshot;
        _bounds = virtualBounds;
        _monitor = WinForms.Screen.FromPoint(WinForms.Cursor.Position).Bounds;

        FormBorderStyle = WinForms.FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = WinForms.FormStartPosition.Manual;
        Bounds = virtualBounds;
        BackColor = Color.Black;
        Cursor = WinForms.Cursors.Cross;
        KeyPreview = true;
        DoubleBuffered = true;
        SetStyle(WinForms.ControlStyles.OptimizedDoubleBuffer |
                 WinForms.ControlStyles.AllPaintingInWmPaint |
                 WinForms.ControlStyles.UserPaint, true);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Activate();

        try
        {
            _toolbar = new RecordToolbarWindow();
            _toolbar.StartRequested += BeginRecording;
            _toolbar.CancelRequested += Close;
        }
        catch (Exception ex)
        {
            App.ShowToast("录屏工具条加载失败", ex.Message);
        }
    }

    protected override void OnFormClosed(WinForms.FormClosedEventArgs e)
    {
        try { _toolbar?.Close(); } catch { }
        _toolbar = null;
        _snapshot.Dispose();
        base.OnFormClosed(e);
    }

    // ================= 绘制 =================

    protected override void OnPaint(WinForms.PaintEventArgs e)
    {
        base.OnPaint(e);

        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        graphics.DrawImage(_snapshot, new Rectangle(0, 0, Width, Height));

        using (var dim = new SolidBrush(Color.FromArgb(118, 0, 0, 0)))
            graphics.FillRectangle(dim, ClientRectangle);

        if (_hasSelection && _selection.Width > 0 && _selection.Height > 0)
        {
            graphics.DrawImage(_snapshot, _selection, _selection, GraphicsUnit.Pixel);

            using var border = new Pen(Color.FromArgb(59, 130, 246), 2);
            graphics.DrawRectangle(border, _selection);

            foreach (var point in HandlePoints())
            {
                var dot = new Rectangle(point.X - 5, point.Y - 5, 10, 10);
                graphics.FillEllipse(System.Drawing.Brushes.White, dot);
                graphics.DrawEllipse(border, dot);
            }

            DrawSizeBadge(graphics);
        }

        DrawHint(graphics);
    }

    private void DrawSizeBadge(Graphics graphics)
    {
        var text = $"{_selection.Width} × {_selection.Height}";
        using var font = new Font("Microsoft YaHei UI", 9f);
        var size = graphics.MeasureString(text, font);
        int width = (int)size.Width + 16;
        int height = (int)size.Height + 8;

        int left = _selection.Left;
        int top = _selection.Top - height - 8;
        if (top < 4) top = _selection.Top + 8;
        left = Math.Min(left, Width - width - 4);

        // 全屏选择时左上角会被工具条占用，尺寸标签改放右上角
        if (_selection.Left <= 4 && _selection.Top <= 4)
            left = Math.Max(4, _selection.Right - width - 8);

        using var path = RoundedRect(new Rectangle(left, top, width, height), 7);
        using var fill = new SolidBrush(Color.FromArgb(215, 30, 41, 59));
        graphics.FillPath(fill, path);
        graphics.DrawString(text, font, System.Drawing.Brushes.White, left + 8, top + 3);
    }

    private void DrawHint(Graphics graphics)
    {
        const string text = "拖动选择录制区域 · 单击录制整个屏幕 · Esc 退出";
        using var font = new Font("Microsoft YaHei UI", 10f);
        var size = graphics.MeasureString(text, font);
        int width = (int)size.Width + 30;
        int height = (int)size.Height + 14;
        int left = (Width - width) / 2;
        const int top = 26;

        using var path = RoundedRect(new Rectangle(left, top, width, height), 10);
        using var fill = new SolidBrush(Color.FromArgb(246, 255, 255, 255));
        using var border = new Pen(Color.FromArgb(230, 225, 215), 1);
        graphics.FillPath(fill, path);
        graphics.DrawPath(border, path);
        using var brush = new SolidBrush(Color.FromArgb(88, 98, 112));
        graphics.DrawString(text, font, brush, left + 15, top + 7);
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

    // ================= 交互 =================

    private Point[] HandlePoints()
    {
        int cx = _selection.Left + _selection.Width / 2;
        int cy = _selection.Top + _selection.Height / 2;
        return new[]
        {
            new Point(_selection.Left, _selection.Top),
            new Point(cx, _selection.Top),
            new Point(_selection.Right, _selection.Top),
            new Point(_selection.Right, cy),
            new Point(_selection.Right, _selection.Bottom),
            new Point(cx, _selection.Bottom),
            new Point(_selection.Left, _selection.Bottom),
            new Point(_selection.Left, cy)
        };
    }

    private int HitHandle(Point point)
    {
        if (!_hasSelection) return -1;
        var points = HandlePoints();
        for (int i = 0; i < points.Length; i++)
        {
            if (Math.Abs(point.X - points[i].X) <= 8 && Math.Abs(point.Y - points[i].Y) <= 8)
                return i;
        }
        return -1;
    }

    protected override void OnMouseDown(WinForms.MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button == WinForms.MouseButtons.Right)
        {
            Close();
            return;
        }

        if (e.Button != WinForms.MouseButtons.Left) return;

        _dragStart = e.Location;
        _dragOrigin = _selection;

        int handle = HitHandle(e.Location);
        if (handle >= 0)
        {
            _mode = DragMode.Resize;
            _handle = handle;
        }
        else if (_hasSelection && _selection.Contains(e.Location))
        {
            _mode = DragMode.Move;
        }
        else
        {
            _mode = DragMode.New;
            _hasSelection = false;
            _selection = new Rectangle(e.Location, Size.Empty);
            _monitor = WinForms.Screen.FromPoint(e.Location).Bounds;
            Invalidate();
        }
    }

    protected override void OnMouseMove(WinForms.MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_mode == DragMode.None)
        {
            UpdateCursor(e.Location);
            return;
        }

        if (_mode == DragMode.New)
        {
            _selection = Normalize(_dragStart, e.Location);
            _selection = ClampToMonitor(_selection);
        }
        else if (_mode == DragMode.Move)
        {
            int dx = e.X - _dragStart.X;
            int dy = e.Y - _dragStart.Y;
            var moved = new Rectangle(_dragOrigin.X + dx, _dragOrigin.Y + dy, _dragOrigin.Width, _dragOrigin.Height);
            _selection = ClampToBounds(moved);
        }
        else if (_mode == DragMode.Resize)
        {
            _selection = ResizeSelection(_dragOrigin, e.Location, _handle);
        }

        if (_hasSelection)
            UpdateToolbar();

        Invalidate();
    }

    protected override void OnMouseUp(WinForms.MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != WinForms.MouseButtons.Left) return;
        if (_mode == DragMode.None) return;

        var mode = _mode;
        _mode = DragMode.None;
        _handle = -1;

        if (mode == DragMode.New)
        {
            if (_selection.Width < 8 || _selection.Height < 8)
            {
                _monitor = WinForms.Screen.FromPoint(e.Location).Bounds;
                _selection = _monitor;
            }
            _selection = ClampToBounds(_selection);
        }

        if (_selection.Width < 16 || _selection.Height < 16)
        {
            _hasSelection = false;
            Invalidate();
            return;
        }

        _hasSelection = true;
        ShowToolbar();
        UpdateToolbar();
        Invalidate();
    }

    private void UpdateCursor(Point point)
    {
        int handle = HitHandle(point);
        if (handle >= 0)
        {
            Cursor = handle switch
            {
                0 or 4 => WinForms.Cursors.SizeNWSE,
                2 or 6 => WinForms.Cursors.SizeNESW,
                1 or 5 => WinForms.Cursors.SizeNS,
                3 or 7 => WinForms.Cursors.SizeWE,
                _ => WinForms.Cursors.Cross
            };
        }
        else if (_hasSelection && _selection.Contains(point))
        {
            Cursor = WinForms.Cursors.SizeAll;
        }
        else
        {
            Cursor = WinForms.Cursors.Cross;
        }
    }

    private static Rectangle Normalize(Point a, Point b)
    {
        return Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y),
            Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
    }

    private Rectangle ClampToMonitor(Rectangle rect)
    {
        var bounds = _monitor;
        int left = Math.Max(bounds.Left, Math.Min(rect.Left, bounds.Right - 1));
        int top = Math.Max(bounds.Top, Math.Min(rect.Top, bounds.Bottom - 1));
        int right = Math.Max(left + 1, Math.Min(rect.Right, bounds.Right));
        int bottom = Math.Max(top + 1, Math.Min(rect.Bottom, bounds.Bottom));
        return Rectangle.FromLTRB(left, top, right, bottom);
    }

    private Rectangle ClampToBounds(Rectangle rect)
    {
        int width = Math.Min(rect.Width, _bounds.Width);
        int height = Math.Min(rect.Height, _bounds.Height);
        int left = Math.Max(_bounds.Left, Math.Min(rect.Left, _bounds.Right - width));
        int top = Math.Max(_bounds.Top, Math.Min(rect.Top, _bounds.Bottom - height));
        return new Rectangle(left, top, width, height);
    }

    private Rectangle ResizeSelection(Rectangle origin, Point point, int handle)
    {
        int left = origin.Left;
        int top = origin.Top;
        int right = origin.Right;
        int bottom = origin.Bottom;

        if (handle is 0 or 6 or 7) left = point.X;
        if (handle is 2 or 3 or 4) right = point.X;
        if (handle is 0 or 1 or 2) top = point.Y;
        if (handle is 4 or 5 or 6) bottom = point.Y;

        if (right - left < 32) right = left + 32;
        if (bottom - top < 32) bottom = top + 32;

        return ClampToBounds(Rectangle.FromLTRB(left, top, right, bottom));
    }

    protected override void OnKeyDown(WinForms.KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == WinForms.Keys.Escape) Close();
        else if (e.KeyCode == WinForms.Keys.Enter && _hasSelection) BeginRecording();
    }

    // ================= 工具条与开始 =================

    private void ShowToolbar()
    {
        if (_toolbar == null || _toolbar.IsVisible) return;
        _toolbar.Show();
    }

    private void UpdateToolbar()
    {
        if (_toolbar == null || !_hasSelection) return;
        _toolbar.MoveTo(_selection, _monitor);
    }

    private void BeginRecording()
    {
        if (_started || !_hasSelection) return;
        if (_selection.Width < 16 || _selection.Height < 16) return;
        _started = true;

        var region = _selection;
        var format = _toolbar?.Format ?? RecordFormat.Mp4;
        var options = new RecordingOptions
        {
            Region = region,
            Format = format,
            Quality = _toolbar?.Quality ?? RecordQuality.Sd,
            SystemAudio = _toolbar?.SystemAudio ?? true,
            Microphone = _toolbar?.Microphone ?? false,
            OutputPath = RecordingService.SuggestFileName(format)
        };

        try { _toolbar?.Hide(); } catch { }
        Hide();

        // 等桌面恢复干净再开始抓屏，避免把选区和工具条录进第一帧
        var timer = new WinForms.Timer { Interval = 220 };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            timer.Dispose();
            Close();

            if (RecordingService.Start(options))
            {
                RecordingControlForm.ShowFor(region);
            }
            else
            {
                App.ShowToast("无法开始录制", "可能已有录制在进行中");
            }
        };
        timer.Start();
    }
}
