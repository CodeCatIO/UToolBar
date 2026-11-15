using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using UToolBar.Services;

namespace UToolBar.Views;

public partial class SalaryView : UserControl
{
    private static readonly TimeSpan DefaultStart = new(9, 0, 0);
    private static readonly TimeSpan DefaultEnd = new(18, 0, 0);

    private readonly DispatcherTimer _timer;

    public SalaryView()
    {
        InitializeComponent();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => UpdateSalary();

        if (App.State != null)
            App.State.Salary.PropertyChanged += (_, _) => UpdateSalary();

        Loaded += (_, _) => UpdateSalary();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                UpdateSalary();
                _timer.Start();
            }
            else
            {
                _timer.Stop();
            }
        };
        Unloaded += (_, _) => _timer.Stop();
    }

    private void UpdateSalary()
    {
        if (App.State?.Salary == null) return;

        var salary = App.State.Salary;
        var now = DateTime.Now;
        var today = now.Date;

        var start = TimeSpan.TryParse(salary.WorkStart, out var s) ? s : DefaultStart;
        var end = TimeSpan.TryParse(salary.WorkEnd, out var e) && e > start ? e : DefaultEnd;
        if (end <= start) end = start.Add(TimeSpan.FromHours(8));

        double daily = salary.MonthlySalary / 21.75;
        double workingSeconds = Math.Max(1800, (end - start).TotalSeconds);
        double hourly = daily / Math.Max(0.5, (end - start).TotalHours);
        double perSecond = daily / workingSeconds;

        bool offDay = HolidayService.IsOffDay(today);
        double earned;
        string status;
        string sub;

        if (offDay)
        {
            earned = 0;
            status = "休息日";
            sub = "今天不上班，把命续上";
        }
        else if (now.TimeOfDay < start)
        {
            earned = 0;
            status = "还没上班";
            sub = $"距离上班还有 {FormatSpan(start - now.TimeOfDay)}";
        }
        else if (now.TimeOfDay >= end)
        {
            earned = daily;
            status = "下班啦";
            sub = "今天的钱已经赚到手了";
        }
        else
        {
            double ratio = (now.TimeOfDay - start).TotalSeconds / (end - start).TotalSeconds;
            earned = daily * Math.Clamp(ratio, 0, 1);
            status = StatusFor(now.TimeOfDay, end);
            sub = $"已完成 {ratio * 100:0}% · 离下班 {FormatSpan(end - now.TimeOfDay)}";
        }

        EarnedText.Text = earned.ToString("N2");
        EarnedSubText.Text = sub;
        StatusText.Text = status;

        DailyText.Text = $"¥{daily:N2}";
        HourlyText.Text = $"¥{hourly:N2}";
        PerSecondText.Text = $"¥{perSecond:N4}";

        if (offDay)
            OffCountdownText.Text = "今日休息";
        else if (now.TimeOfDay >= end)
            OffCountdownText.Text = "00:00:00";
        else
            OffCountdownText.Text = FormatSpan(end - now.TimeOfDay);

        UpdatePayday(today, salary.PayDay);
    }

    private void UpdatePayday(DateTime today, int payDay)
    {
        int day = Math.Clamp(payDay, 1, 31);
        int thisMonthDay = Math.Min(day, DateTime.DaysInMonth(today.Year, today.Month));
        var payDate = new DateTime(today.Year, today.Month, thisMonthDay);

        if (today > payDate)
        {
            var nextMonth = today.AddMonths(1);
            int nextDay = Math.Min(day, DateTime.DaysInMonth(nextMonth.Year, nextMonth.Month));
            payDate = new DateTime(nextMonth.Year, nextMonth.Month, nextDay);
        }

        int days = (payDate - today).Days;
        PaydayText.Text = days == 0 ? "就是今天" : $"{days} 天";
        PaydaySubText.Text = payDate.ToString("M月d日");
    }

    private static string StatusFor(TimeSpan now, TimeSpan end)
    {
        if (now >= end - TimeSpan.FromHours(1)) return "冲刺下班";
        if (now < new TimeSpan(11, 30, 0)) return "疯狂搬砖";
        if (now < new TimeSpan(12, 0, 0)) return "等饭中";
        if (now < new TimeSpan(13, 0, 0)) return "干饭时间";
        if (now < new TimeSpan(14, 0, 0)) return "带薪发呆";
        return "继续搬砖";
    }

    private static string FormatSpan(TimeSpan span)
    {
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        return $"{(int)span.TotalHours:00}:{span.Minutes:00}:{span.Seconds:00}";
    }

    private void OnWidgetClick(object sender, RoutedEventArgs e) => App.ShowSalaryWidget();
}
