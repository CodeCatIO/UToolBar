using System.Text.Json.Serialization;
using UToolBar.Services;

namespace UToolBar.Models;

public class AnniversaryItem : ObservableObject
{
    private string _title = "";
    private int _month = 1;
    private int _day = 1;
    private bool _isLunar;
    private int _remindDaysBefore = 3;
    private int _lastNotifiedYear;
    private int _lastAdvanceYear;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>生日 / 纪念日</summary>
    public string Kind { get; set; } = "纪念日";

    public string Title
    {
        get => _title;
        set => Set(ref _title, value);
    }

    public int Month
    {
        get => _month;
        set { if (Set(ref _month, value)) Refresh(); }
    }

    public int Day
    {
        get => _day;
        set { if (Set(ref _day, value)) Refresh(); }
    }

    public bool IsLunar
    {
        get => _isLunar;
        set { if (Set(ref _isLunar, value)) Refresh(); }
    }

    public int RemindDaysBefore
    {
        get => _remindDaysBefore;
        set => Set(ref _remindDaysBefore, value);
    }

    /// <summary>上一次提醒的年份，用于同一年只提醒一次</summary>
    public int LastNotifiedYear
    {
        get => _lastNotifiedYear;
        set => Set(ref _lastNotifiedYear, value);
    }

    /// <summary>提前提醒的年份，避免每次启动重复提醒</summary>
    public int LastAdvanceYear
    {
        get => _lastAdvanceYear;
        set => Set(ref _lastAdvanceYear, value);
    }

    [JsonIgnore]
    public DateTime NextDate => IsLunar
        ? LunarService.NextLunarOccurrence(Month, Day, DateTime.Today)
        : NextSolarOccurrence(Month, Day, DateTime.Today);

    [JsonIgnore]
    public int DaysLeft => (NextDate.Date - DateTime.Today).Days;

    [JsonIgnore]
    public string CountdownText
    {
        get
        {
            if (DaysLeft == 0) return "就是今天";
            if (DaysLeft == 1) return "就是明天";
            return $"还有 {DaysLeft} 天";
        }
    }

    [JsonIgnore]
    public string DateText => IsLunar
        ? $"农历 {LunarService.LunarMonthName(Month)}{LunarService.LunarDayName(Day)}"
        : $"{Month} 月 {Day} 日";

    [JsonIgnore]
    public bool IsSoon => DaysLeft <= RemindDaysBefore;

    [JsonIgnore]
    public bool IsToday => DaysLeft == 0;

    [JsonIgnore]
    public string RemindText => RemindDaysBefore <= 0 ? "当天提醒" : $"提前 {RemindDaysBefore} 天提醒";

    private static DateTime NextSolarOccurrence(int month, int day, DateTime from)
    {
        for (int y = from.Year; y <= from.Year + 4; y++)
        {
            try
            {
                var d = new DateTime(y, month, Math.Min(day, DateTime.DaysInMonth(y, month)));
                if (d.Date >= from.Date) return d;
            }
            catch
            {
                // 非法日期直接跳过
            }
        }
        return from;
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(NextDate));
        OnPropertyChanged(nameof(DaysLeft));
        OnPropertyChanged(nameof(CountdownText));
        OnPropertyChanged(nameof(DateText));
        OnPropertyChanged(nameof(IsSoon));
        OnPropertyChanged(nameof(IsToday));
        OnPropertyChanged(nameof(RemindText));
    }
}
