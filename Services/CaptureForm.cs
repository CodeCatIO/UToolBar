using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Media.Imaging;
using WinForms = System.Windows.Forms;

namespace UToolBar.Services;

/// <summary>用 WinForms 实现的全屏截图选区，避免 WPF 分层窗口的输入与渲染问题</summary>
public sealed class CaptureForm : WinForms.Form
{
    private readonly Bitmap _full;
    private readonly Rectangle _virtualBounds;
    private Point _start;
    private Rectangle _selection;
    private bool _dragging;

    public BitmapSource SelectedImage { get; private set; }

    public CaptureForm(Bitmap full, Rectangle virtualBounds)
    {
        _full = full;
        _virtualBounds = virtualBounds;

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

    protected override void OnPaint(WinForms.PaintEventArgs e)
    {
        base.OnPaint(e);

        var graphics = e.Graphics;
        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        graphics.DrawImage(_full, new Rectangle(0, 0, Width, Height));

        using (var dim = new SolidBrush(Color.FromArgb(120, 0, 0, 0)))
            graphics.FillRectangle(dim, ClientRectangle);

        if (_selection.Width > 0 && _selection.Height > 0)
        {
            graphics.DrawImage(_full, _selection, _selection, GraphicsUnit.Pixel);

            using var pen = new Pen(Color.FromArgb(143, 184, 220), 2);
            graphics.DrawRectangle(pen, _selection);

            var sizeText = $"{_selection.Width} × {_selection.Height}";
            using var font = new Font("Microsoft YaHei UI", 9f);
            var size = graphics.MeasureString(sizeText, font);
            var badge = new Rectangle(_selection.Left, Math.Min(Height - 30, _selection.Bottom + 6),
                (int)size.Width + 14, (int)size.Height + 6);
            using var badgeBrush = new SolidBrush(Color.FromArgb(230, 46, 55, 66));
            graphics.FillRectangle(badgeBrush, badge);
            graphics.DrawString(sizeText, font, Brushes.White, badge.Left + 7, badge.Top + 3);
        }

        DrawHint(graphics);
    }

    private void DrawHint(Graphics graphics)
    {
        const string text = "拖动鼠标选择截图区域 · 右键或 Esc 取消";
        using var font = new Font("Microsoft YaHei UI", 10f);
        var size = graphics.MeasureString(text, font);
        var width = (int)size.Width + 28;
        var height = (int)size.Height + 14;
        var left = (Width - width) / 2;
        var top = 26;

        using var path = RoundedRect(new Rectangle(left, top, width, height), 10);
        using var fill = new SolidBrush(Color.FromArgb(245, 255, 255, 255));
        using var border = new Pen(Color.FromArgb(232, 225, 215), 1);
        graphics.FillPath(fill, path);
        graphics.DrawPath(border, path);
        graphics.DrawString(text, font, new SolidBrush(Color.FromArgb(92, 102, 117)), left + 14, top + 7);
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

    protected override void OnMouseDown(WinForms.MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == WinForms.MouseButtons.Right)
        {
            Close();
            return;
        }
        if (e.Button != WinForms.MouseButtons.Left) return;

        _start = e.Location;
        _selection = new Rectangle(e.Location, Size.Empty);
        _dragging = true;
        Invalidate();
    }

    protected override void OnMouseMove(WinForms.MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging) return;

        int x = Math.Min(_start.X, e.X);
        int y = Math.Min(_start.Y, e.Y);
        int w = Math.Abs(e.X - _start.X);
        int h = Math.Abs(e.Y - _start.Y);
        _selection = new Rectangle(x, y, w, h);
        Invalidate();
    }

    protected override void OnMouseUp(WinForms.MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!_dragging) return;
        _dragging = false;

        if (_selection.Width < 6 || _selection.Height < 6)
        {
            Close();
            return;
        }

        int x = Math.Clamp(_selection.X, 0, _full.Width - 1);
        int y = Math.Clamp(_selection.Y, 0, _full.Height - 1);
        int w = Math.Clamp(_selection.Width, 1, _full.Width - x);
        int h = Math.Clamp(_selection.Height, 1, _full.Height - y);

        try
        {
            using var cropped = _full.Clone(new Rectangle(x, y, w, h), PixelFormat.Format32bppArgb);
            using var stream = new MemoryStream();
            cropped.Save(stream, ImageFormat.Png);
            stream.Position = 0;

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            SelectedImage = image;
        }
        catch
        {
        }

        Close();
    }

    protected override void OnKeyDown(WinForms.KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == WinForms.Keys.Escape) Close();
    }
}
