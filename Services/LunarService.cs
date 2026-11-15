using System.Globalization;

namespace UToolBar.Services;

public sealed record LunarDate(int Year, int Month, int Day, bool IsLeap)
{
    public string MonthName => LunarService.LunarMonthName(Month, IsLeap);
    public string DayName => LunarService.LunarDayName(Day);
    public string FullText => $"{MonthName}{DayName}";
}

/// <summary>
/// 农历、节气、节日、黄历宜忌的轻量实现。
/// 农历使用 .NET 内置 ChineseLunisolarCalendar，节气用太阳黄经近似计算。
/// </summary>
public static class LunarService
{
    private static readonly ChineseLunisolarCalendar Cal = new();

    private static readonly string[] MonthNames =
    {
        "正月", "二月", "三月", "四月", "五月", "六月",
        "七月", "八月", "九月", "十月", "冬月", "腊月"
    };

    private static readonly string[] DayNames =
    {
        "初一", "初二", "初三", "初四", "初五", "初六", "初七", "初八", "初九", "初十",
        "十一", "十二", "十三", "十四", "十五", "十六", "十七", "十八", "十九", "二十",
        "廿一", "廿二", "廿三", "廿四", "廿五", "廿六", "廿七", "廿八", "廿九", "三十"
    };

    private static readonly string[] TermNames =
    {
        "小寒", "大寒", "立春", "雨水", "惊蛰", "春分",
        "清明", "谷雨", "立夏", "小满", "芒种", "夏至",
        "小暑", "大暑", "立秋", "处暑", "白露", "秋分",
        "寒露", "霜降", "立冬", "小雪", "大雪", "冬至"
    };

    private static readonly string[] GanNames = { "甲", "乙", "丙", "丁", "戊", "己", "庚", "辛", "壬", "癸" };
    private static readonly string[] ZhiNames = { "子", "丑", "寅", "卯", "辰", "巳", "午", "未", "申", "酉", "戌", "亥" };
    private static readonly string[] JianChuNames = { "建", "除", "满", "平", "定", "执", "破", "危", "成", "收", "开", "闭" };

    private static readonly string[] JianChuYi =
    {
        "出行 · 上任 · 会友", "扫舍 · 求医 · 沐浴", "祈福 · 开市 · 纳财", "修饰 · 平治 · 嫁娶",
        "嫁娶 · 签约 · 安床", "修造 · 捕捉 · 栽种", "破屋 · 求医 · 解约", "祭祀 · 安床 · 祈福",
        "嫁娶 · 开市 · 出行", "纳财 · 收账 · 入仓", "开市 · 出行 · 交易", "筑堤 · 收纳 · 安葬"
    };

    private static readonly string[] JianChuJi =
    {
        "动土 · 开仓", "嫁娶 · 安葬", "动土 · 诉讼", "栽种 · 远行",
        "诉讼 · 破土", "开市 · 搬家", "嫁娶 · 签约", "登高 · 远行",
        "诉讼 · 破土", "出行 · 借贷", "安葬 · 动土", "开市 · 求医"
    };

    private static readonly Dictionary<(int Month, int Day), string> SolarFestivals = new()
    {
        [(1, 1)] = "元旦",
        [(2, 14)] = "情人节",
        [(3, 8)] = "妇女节",
        [(3, 12)] = "植树节",
        [(4, 1)] = "愚人节",
        [(5, 1)] = "劳动节",
        [(5, 4)] = "青年节",
        [(6, 1)] = "儿童节",
        [(7, 1)] = "建党节",
        [(8, 1)] = "建军节",
        [(9, 10)] = "教师节",
        [(10, 1)] = "国庆节",
        [(10, 31)] = "万圣夜",
        [(11, 11)] = "光棍节",
        [(12, 24)] = "平安夜",
        [(12, 25)] = "圣诞节"
    };

    private static readonly Dictionary<(int Month, int Day), string> LunarFestivals = new()
    {
        [(1, 1)] = "春节",
        [(1, 15)] = "元宵节",
        [(2, 2)] = "龙抬头",
        [(5, 5)] = "端午节",
        [(7, 7)] = "七夕节",
        [(7, 15)] = "中元节",
        [(8, 15)] = "中秋节",
        [(9, 9)] = "重阳节",
        [(12, 8)] = "腊八节",
        [(12, 23)] = "小年"
    };

    private static readonly Dictionary<int, DateTime[]> TermCache = new();
    private static readonly Dictionary<DateTime, string> TermByDate = new();

    // ================= 农历 =================

    public static LunarDate GetLunar(DateTime date)
    {
        try
        {
            var d = date.Date;
            if (d < Cal.MinSupportedDateTime || d > Cal.MaxSupportedDateTime) return null;
            int ly = Cal.GetYear(d);
            int lm = Cal.GetMonth(d);
            int ld = Cal.GetDayOfMonth(d);
            int leap = Cal.GetLeapMonth(ly);
            bool isLeap = leap > 0 && lm == leap;
            int realMonth = leap > 0 && lm >= leap ? lm - 1 : lm;
            return new LunarDate(ly, realMonth, ld, isLeap);
        }
        catch
        {
            return null;
        }
    }

    public static string LunarMonthName(int month, bool isLeap = false)
    {
        int i = Math.Clamp(month, 1, 12) - 1;
        return (isLeap ? "闰" : "") + MonthNames[i];
    }

    public static string LunarDayName(int day)
    {
        int i = Math.Clamp(day, 1, 30) - 1;
        return DayNames[i];
    }

    /// <summary>日历格子里的农历短文本：初一显示月份，其余显示日期</summary>
    public static string GetLunarShortText(DateTime date)
    {
        var lunar = GetLunar(date);
        if (lunar == null) return "";
        return lunar.Day == 1 ? lunar.MonthName : lunar.DayName;
    }

    public static string GetLunarFullText(DateTime date)
    {
        var lunar = GetLunar(date);
        if (lunar == null) return "";
        return $"{GanZhi(lunar.Year - 1984)}年 {lunar.FullText}";
    }

    public static bool TryLunarToDate(int lunarYear, int month, int day, out DateTime date)
    {
        date = default;
        try
        {
            int leap = Cal.GetLeapMonth(lunarYear);
            int m = month;
            if (leap > 0 && month >= leap) m = month + 1;
            int maxDay = Cal.GetDaysInMonth(lunarYear, m);
            if (day > maxDay) day = maxDay;
            date = Cal.ToDateTime(lunarYear, m, day, 0, 0, 0, 0);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static DateTime NextLunarOccurrence(int month, int day, DateTime from)
    {
        var lunar = GetLunar(from);
        int startYear = lunar?.Year ?? from.Year;
        for (int y = startYear; y <= startYear + 4; y++)
        {
            if (TryLunarToDate(y, month, day, out var date) && date.Date >= from.Date)
                return date.Date;
        }
        return from.Date;
    }

    private static string GanZhi(int offset)
    {
        int i = ((offset % 60) + 60) % 60;
        return GanNames[i % 10] + ZhiNames[i % 12];
    }

    // ================= 节气 =================

    public static string GetTermName(DateTime date)
    {
        EnsureTerms(date.Year);
        return TermByDate.TryGetValue(date.Date, out var name) ? name : "";
    }

    public static DateTime GetTermDate(int year, int index)
    {
        EnsureTerms(year);
        return TermCache.TryGetValue(year, out var arr) ? arr[Math.Clamp(index, 0, 23)] : default;
    }

    private static void EnsureTerms(int year)
    {
        if (TermCache.ContainsKey(year)) return;

        var found = new DateTime[24];
        var pending = new List<int>();
        for (int i = 0; i < 24; i++) pending.Add(i);

        var start = new DateTime(year, 1, 1, 0, 0, 0);
        double prev = SolarLongitude(start);
        int maxHours = 366 * 24;

        for (int h = 1; h <= maxHours && pending.Count > 0; h++)
        {
            var cur = start.AddHours(h);
            double curLon = SolarLongitude(cur);

            for (int k = pending.Count - 1; k >= 0; k--)
            {
                int idx = pending[k];
                double target = Normalize(285.0 + idx * 15.0);
                double d0 = Normalize180(prev - target);
                double d1 = Normalize180(curLon - target);

                if (d0 < 0 && d1 >= 0 && d1 - d0 > 0 && d1 - d0 < 2)
                {
                    double frac = d1 - d0 < 1e-9 ? 0 : -d0 / (d1 - d0);
                    found[idx] = start.AddHours(h - 1).AddMinutes(frac * 60);
                    pending.RemoveAt(k);
                }
            }
            prev = curLon;
        }

        TermCache[year] = found;
        foreach (var t in found)
        {
            if (t != default) TermByDate[t.Date] = TermNames[Array.IndexOf(found, t)];
        }
    }

    private static double SolarLongitude(DateTime beijingTime)
    {
        // 北京时间为 UTC+8，先转 UTC 再算儒略日
        double jd = beijingTime.AddHours(-8).ToOADate() + 2415018.5;
        double t = (jd - 2451545.0) / 36525.0;

        double l0 = 280.46646 + 36000.76983 * t + 0.0003032 * t * t;
        double m = (357.52911 + 35999.05029 * t - 0.0001537 * t * t) * Math.PI / 180;
        double c = (1.914602 - 0.004817 * t - 0.000014 * t * t) * Math.Sin(m)
                 + (0.019993 - 0.000101 * t) * Math.Sin(2 * m)
                 + 0.000289 * Math.Sin(3 * m);
        double omega = (125.04 - 1934.136 * t) * Math.PI / 180;
        double lambda = l0 + c - 0.00569 - 0.00478 * Math.Sin(omega);
        return Normalize(lambda);
    }

    private static double Normalize(double degrees)
    {
        double v = degrees % 360.0;
        return v < 0 ? v + 360.0 : v;
    }

    private static double Normalize180(double degrees)
    {
        double v = Normalize(degrees);
        return v >= 180 ? v - 360 : v;
    }

    // ================= 节日 =================

    public static List<string> GetFestivals(DateTime date)
    {
        var list = new List<string>();
        var d = date.Date;

        if (SolarFestivals.TryGetValue((d.Month, d.Day), out var solar))
            list.Add(solar);

        var lunar = GetLunar(d);
        if (lunar != null)
        {
            if (LunarFestivals.TryGetValue((lunar.Month, lunar.Day), out var lunarFestival))
                list.Add(lunarFestival);

            // 除夕：腊月最后一天
            if (lunar.Month == 12)
            {
                try
                {
                    if (lunar.Day == Cal.GetDaysInMonth(lunar.Year, 12)) list.Add("除夕");
                }
                catch
                {
                }
            }
        }

        if (d.Month == 5 && d.DayOfWeek == DayOfWeek.Sunday && (d.Day - 1) / 7 == 1)
            list.Add("母亲节");
        if (d.Month == 6 && d.DayOfWeek == DayOfWeek.Sunday && (d.Day - 1) / 7 == 2)
            list.Add("父亲节");
        if (d.Month == 11 && d.DayOfWeek == DayOfWeek.Thursday && (d.Day - 1) / 7 == 3)
            list.Add("感恩节");

        return list;
    }

    public static string GetFestivalText(DateTime date)
    {
        var list = GetFestivals(date);
        return list.Count == 0 ? "" : string.Join(" · ", list);
    }

    // ================= 黄历（简化建除十二神） =================

    public static (string Yi, string Ji) GetAlmanac(DateTime date)
    {
        int dayIndex = (int)(date.Date - new DateTime(1900, 1, 31)).Days + 40;
        dayIndex = ((dayIndex % 60) + 60) % 60;
        int branch = dayIndex % 12;

        var lunar = GetLunar(date);
        int month = lunar?.Month ?? date.Month;
        int monthBranch = (month + 1) % 12; // 正月建寅

        int idx = ((branch - monthBranch) % 12 + 12) % 12;
        return (JianChuYi[idx], JianChuJi[idx]);
    }

    public static string GetJianChuName(DateTime date)
    {
        int dayIndex = ((int)(date.Date - new DateTime(1900, 1, 31)).Days + 40) % 60;
        dayIndex = (dayIndex + 60) % 60;
        int branch = dayIndex % 12;
        var lunar = GetLunar(date);
        int month = lunar?.Month ?? date.Month;
        int monthBranch = (month + 1) % 12;
        int idx = ((branch - monthBranch) % 12 + 12) % 12;
        return JianChuNames[idx] + "日";
    }
}
