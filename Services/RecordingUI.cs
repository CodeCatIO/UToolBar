using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;

namespace UToolBar.Services;

/// <summary>录屏入口：收起面板、冻结屏幕、弹出选区</summary>
public static class RecordingUI
{
    private static bool _active;

    public static void StartSelection()
    {
        if (RecordingService.IsRecording)
        {
            App.ShowToast("正在录制", "先点控制条上的「停止」再开始新的录制");
            return;
        }

        if (_active) return;
        _active = true;

        if (App.Shell?.IsPanelExpanded == true)
            App.Shell.CollapseExternally();

        // 等贴边面板收起，避免把自己录进去
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(280) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            try
            {
                var bounds = WinForms.SystemInformation.VirtualScreen;
                var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bitmap.Size,
                        CopyPixelOperation.SourceCopy);
                }

                var form = new RecordSelectForm(bitmap, bounds);
                form.FormClosed += (_, _) => _active = false;
                form.Show();
                form.Activate();
            }
            catch (Exception ex)
            {
                _active = false;
                App.ShowToast("无法开始录屏", ex.Message);
            }
        };
        timer.Start();
    }
}
