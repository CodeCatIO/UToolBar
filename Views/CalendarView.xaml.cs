using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EdgeBar.Models;
using EdgeBar.Services;

namespace EdgeBar.Views;

public partial class CalendarView : UserControl
{
    private DateTime _viewMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateTime _selected = DateTime.Today;

    public CalendarView()
    {
        InitializeComponent();
        Loaded += (_, _) => BuildCalendar();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) BuildCalendar();
        };
    }

    private void BuildCalendar()
    {
        var today = DateTime.Today;
        TodaySubText.Text = "点任意一天，看农历、节气与宜忌";
        MonthTitle.Text = $"{_viewMonth:yyyy 年 M 月}";

        var first = new DateTime(_viewMonth.Year, _viewMonth.Month, 1);
        int offset = ((int)first.DayOfWeek + 6) % 7; // 周一为一周开始
        var start = first.AddDays(-offset);

        var days = new List<DayInfo>(42);
        for (int i = 0; i < 42; i++)
        {
            var date = start.AddDays(i);
            var holiday = HolidayService.Get(date);
            var festival = LunarService.GetFestivalText(date);
            var term = LunarService.GetTermName(date);
            var lunar = LunarService.GetLunar(date);

            string sub = festival;
            if (string.IsNullOrEmpty(sub)) sub = term;
            if (string.IsNullOrEmpty(sub) && lunar != null)
                sub = lunar.Day == 1 ? lunar.MonthName : lunar.DayName;

            days.Add(new DayInfo
            {
                Date = date,
                Day = date.Day,
                IsCurrentMonth = date.Month == _viewMonth.Month && date.Year == _viewMonth.Year,
                IsToday = date.Date == today,
                IsSelected = date.Date == _selected.Date,
                SubText = sub ?? "",
                Badge = holiday == null ? "" : holiday.IsWork ? "班" : "休"
            });
        }

        DayGrid.ItemsSource = days;
        UpdateDetail();
    }

    private void UpdateDetail()
    {
        var date = _selected.Date;
        var lunar = LunarService.GetLunar(date);
        var holiday = HolidayService.Get(date);

        DetailDay.Text = date.Day.ToString();
        DetailWeek.Text = TodoItem.WeekdayCn(date);
        DetailLunar.Text = LunarService.GetLunarFullText(date);

        var events = new List<string>();
        var festival = LunarService.GetFestivalText(date);
        if (!string.IsNullOrEmpty(festival)) events.Add(festival);
        var term = LunarService.GetTermName(date);
        if (!string.IsNullOrEmpty(term)) events.Add(term);
        DetailEvents.Text = string.Join(" · ", events);
        DetailEvents.Visibility = events.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        if (holiday != null)
            DetailHoliday.Text = holiday.IsWork ? $"{holiday.Name} · 调休上班" : $"{holiday.Name} · 休息";
        else if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            DetailHoliday.Text = "周末 · 休息";
        else
            DetailHoliday.Text = "";

        var (yi, ji) = LunarService.GetAlmanac(date);
        DetailYi.Text = yi;
        DetailJi.Text = ji;

        if (lunar == null)
        {
            DetailLunar.Text = "";
            DetailYi.Text = "超出农历支持范围";
            DetailJi.Text = "";
        }
    }

    private void OnDayClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: DayInfo info }) return;

        _selected = info.Date.Date;
        if (_selected.Year != _viewMonth.Year || _selected.Month != _viewMonth.Month)
            _viewMonth = new DateTime(_selected.Year, _selected.Month, 1);

        BuildCalendar();
    }

    private void OnPrevMonth(object sender, RoutedEventArgs e) => GoMonth(-1);

    private void OnNextMonth(object sender, RoutedEventArgs e) => GoMonth(1);

    private void GoMonth(int delta)
    {
        _viewMonth = _viewMonth.AddMonths(delta);
        if (_selected.Year != _viewMonth.Year || _selected.Month != _viewMonth.Month)
            _selected = _viewMonth;
        BuildCalendar();
    }

    private void OnTodayClick(object sender, RoutedEventArgs e)
    {
        _viewMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        _selected = DateTime.Today;
        BuildCalendar();
    }
}

public class DayInfo
{
    public DateTime Date { get; set; }
    public int Day { get; set; }
    public bool IsCurrentMonth { get; set; }
    public bool IsToday { get; set; }
    public bool IsSelected { get; set; }
    public string SubText { get; set; } = "";
    public string Badge { get; set; } = "";
    public bool HasBadge => Badge.Length > 0;
}
