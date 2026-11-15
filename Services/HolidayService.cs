namespace UToolBar.Services;

public sealed class HolidayInfo
{
    public string Name { get; init; } = "";
    public bool IsOff { get; init; }
    public bool IsWork { get; init; }
}

/// <summary>
/// 中国法定节假日与调休数据（依据国务院办公厅通知）。
/// 已内置 2025-2027 年安排，新一年度公布后可直接在数组里补充。
/// </summary>
public static class HolidayService
{
    private static readonly Dictionary<DateTime, HolidayInfo> Map = Build();

    public static HolidayInfo Get(DateTime date)
        => Map.TryGetValue(date.Date, out var info) ? info : null;

    public static bool IsStatutoryOff(DateTime date) => Get(date)?.IsOff == true;

    public static bool IsWorkAdjust(DateTime date) => Get(date)?.IsWork == true;

    /// <summary>是否休息日（周末或法定假日，扣除调休上班日）</summary>
    public static bool IsOffDay(DateTime date)
    {
        var info = Get(date);
        if (info != null)
        {
            if (info.IsWork) return false;
            if (info.IsOff) return true;
        }
        return date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
    }

    public static bool IsWorkday(DateTime date) => !IsOffDay(date);

    private static Dictionary<DateTime, HolidayInfo> Build()
    {
        var map = new Dictionary<DateTime, HolidayInfo>();

        void Off(int y, int m, int d, int days, string name)
        {
            for (int i = 0; i < days; i++)
            {
                var date = new DateTime(y, m, d).AddDays(i).Date;
                map[date] = new HolidayInfo { Name = name, IsOff = true };
            }
        }

        void Work(int y, int m, int d, string name)
        {
            map[new DateTime(y, m, d).Date] = new HolidayInfo { Name = name, IsWork = true };
        }

        // ---------- 2025 ----------
        Off(2025, 1, 1, 1, "元旦");
        Off(2025, 1, 28, 8, "春节");
        Work(2025, 1, 26, "春节调休");
        Work(2025, 2, 8, "春节调休");
        Off(2025, 4, 4, 3, "清明节");
        Off(2025, 5, 1, 5, "劳动节");
        Work(2025, 4, 27, "劳动节调休");
        Off(2025, 5, 31, 3, "端午节");
        Off(2025, 10, 1, 8, "国庆节·中秋节");
        Work(2025, 9, 28, "国庆节调休");
        Work(2025, 10, 11, "国庆节调休");

        // ---------- 2026 ----------
        Off(2026, 1, 1, 3, "元旦");
        Work(2026, 1, 4, "元旦调休");
        Off(2026, 2, 15, 9, "春节");
        Work(2026, 2, 14, "春节调休");
        Work(2026, 2, 28, "春节调休");
        Off(2026, 4, 4, 3, "清明节");
        Off(2026, 5, 1, 5, "劳动节");
        Work(2026, 5, 9, "劳动节调休");
        Off(2026, 6, 19, 3, "端午节");
        Off(2026, 9, 25, 3, "中秋节");
        Off(2026, 10, 1, 7, "国庆节");
        Work(2026, 9, 20, "国庆节调休");
        Work(2026, 10, 10, "国庆节调休");

        // ---------- 2027 ----------
        Off(2027, 1, 1, 3, "元旦");
        Off(2027, 2, 5, 9, "春节");
        Work(2027, 1, 31, "春节调休");
        Work(2027, 2, 14, "春节调休");
        Off(2027, 4, 3, 3, "清明节");
        Off(2027, 5, 1, 5, "劳动节");
        Work(2027, 5, 8, "劳动节调休");
        Off(2027, 6, 9, 1, "端午节");
        Off(2027, 9, 15, 1, "中秋节");
        Off(2027, 10, 1, 7, "国庆节");
        Work(2027, 9, 26, "国庆节调休");
        Work(2027, 10, 9, "国庆节调休");

        return map;
    }
}
