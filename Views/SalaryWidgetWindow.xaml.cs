using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using UToolBar.Services;

namespace UToolBar.Views;

public partial class SalaryWidgetWindow : Window
{
    private readonly DispatcherTimer _timer;
    private bool _positioning;

    public SalaryWidgetWindow()
    {
        InitializeComponent();

        var settings = App.State.Settings;
        _positioning = true;
        if (settings.WidgetLeft > 0 && settings.WidgetTop > 0)
        {
            Left = settings.WidgetLeft;
            Top = settings.WidgetTop;
        }
        else
        {
            var area = SystemParameters.WorkArea;
            Left = area.Right - Width - 80;
            Top = area.Top + 120;
        }
        _positioning = false;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => UpdateSalary();
        Loaded += (_, _) => UpdateSalary();
        Closed += OnClosed;
        LocationChanged += (_, _) =>
        {
            if (_positioning) return;
            App.State.Settings.WidgetLeft = Left;
            App.State.Settings.WidgetTop = Top;
        };

        if (App.State != null)
            App.State.Salary.PropertyChanged += (_, _) => UpdateSalary();

        _timer.Start();
    }

    private void UpdateSalary()
    {
        if (App.State?.Salary == null) return;

        var salary = App.State.Salary;
        var now = DateTime.Now;
        var today = now.Date;
        var start = TimeSpan.TryParse(salary.WorkStart, out var s) ? s : new TimeSpan(9, 0, 0);
        var end = TimeSpan.TryParse(salary.WorkEnd, out var e) && e > start ? e : new TimeSpan(18, 0, 0);

        double daily = salary.MonthlySalary / 21.75;
        bool offDay = HolidayService.IsOffDay(today);
        double earned;
        string status;

        if (offDay)
        {
            earned = 0;
            status = "休息日";
        }
        else if (now.TimeOfDay < start)
        {
            earned = 0;
            status = "还没上班";
        }
        else if (now.TimeOfDay >= end)
        {
            earned = daily;
            status = "下班啦";
        }
        else
        {
            double ratio = (now.TimeOfDay - start).TotalSeconds / (end - start).TotalSeconds;
            earned = daily * Math.Clamp(ratio, 0, 1);
            status = now.TimeOfDay >= end - TimeSpan.FromHours(1) ? "冲刺下班" : "搬砖中";
        }

        EarnedText.Text = earned.ToString("N2");
        StatusText.Text = status;

        if (offDay)
            OffText.Text = "下班 今日休息";
        else if (now.TimeOfDay >= end)
            OffText.Text = "下班 00:00:00";
        else
            OffText.Text = $"下班 {(int)(end - now.TimeOfDay).TotalHours:00}:{(end - now.TimeOfDay).Minutes:00}:{(end - now.TimeOfDay).Seconds:00}";

        int payDay = Math.Clamp(salary.PayDay, 1, 31);
        int day = Math.Min(payDay, DateTime.DaysInMonth(today.Year, today.Month));
        var payDate = new DateTime(today.Year, today.Month, day);
        if (today > payDate)
        {
            var next = today.AddMonths(1);
            payDate = new DateTime(next.Year, next.Month, Math.Min(payDay, DateTime.DaysInMonth(next.Year, next.Month)));
        }
        int days = (payDate - today).Days;
        PaydayText.Text = days == 0 ? "发薪 今天" : $"发薪 {days} 天";
    }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
        try { DragMove(); } catch { }
    }

    private void OnMouseEnter(object sender, MouseEventArgs e) => CloseButton.Opacity = 1;

    private void OnMouseLeave(object sender, MouseEventArgs e) => CloseButton.Opacity = 0;

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnClosed(object sender, EventArgs e)
    {
        _timer.Stop();
        if (App.State != null) App.State.Settings.SalaryWidgetVisible = false;
        App.ClearSalaryWidget();
    }
}
