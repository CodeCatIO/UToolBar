using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using UToolBar.Services;

namespace UToolBar.Views;

public partial class CalculatorView : UserControl
{
    private static readonly Dictionary<string, string[]> UnitSets = new()
    {
        ["货币"] = new[] { "人民币 ¥", "美元 $", "欧元 €", "日元 ¥", "港币 HK$", "英镑 £", "韩元 ₩" },
        ["长度"] = new[] { "毫米", "厘米", "米", "千米", "英寸", "英尺", "英里" },
        ["重量"] = new[] { "克", "千克", "吨", "斤", "磅", "盎司" },
        ["面积"] = new[] { "平方厘米", "平方米", "公顷", "平方千米", "亩", "平方英尺" },
        ["温度"] = new[] { "摄氏度", "华氏度", "开尔文" },
        ["体积"] = new[] { "毫升", "升", "立方米", "立方厘米", "加仑", "品脱" }
    };

    private static readonly Dictionary<string, double> Factors = new()
    {
        ["人民币 ¥"] = 1,
        ["美元 $"] = 7.10,
        ["欧元 €"] = 7.75,
        ["日元 ¥"] = 0.047,
        ["港币 HK$"] = 0.912,
        ["英镑 £"] = 9.05,
        ["韩元 ₩"] = 0.0049,
        ["毫米"] = 0.001,
        ["厘米"] = 0.01,
        ["米"] = 1,
        ["千米"] = 1000,
        ["英寸"] = 0.0254,
        ["英尺"] = 0.3048,
        ["英里"] = 1609.344,
        ["克"] = 0.001,
        ["千克"] = 1,
        ["吨"] = 1000,
        ["斤"] = 0.5,
        ["磅"] = 0.45359237,
        ["盎司"] = 0.028349523125,
        ["平方厘米"] = 0.0001,
        ["平方米"] = 1,
        ["公顷"] = 10000,
        ["平方千米"] = 1000000,
        ["亩"] = 666.6667,
        ["平方英尺"] = 0.09290304,
        ["毫升"] = 0.001,
        ["升"] = 1,
        ["立方米"] = 1000,
        ["立方厘米"] = 0.001,
        ["加仑"] = 3.785411784,
        ["品脱"] = 0.473176473
    };

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private static readonly Dictionary<string, double> LiveRates = new();
    private static DateTime? _ratesUpdatedAt;

    private string _expr = "";
    private string _lastExpression = "";
    private bool _justEvaluated;
    private string _unitCategory = "货币";
    private int _dateMode;
    private bool _updatingUnits;

    public CalculatorView()
    {
        InitializeComponent();

        Date1Box.Text = DateTime.Today.ToString("yyyy-MM-dd");
        Date2Box.Text = DateTime.Today.AddDays(30).ToString("yyyy-MM-dd");
        RecalcDates();

        LoadUnits();
        ConvertUnits();
        UpdateDisplay();

        PreviewKeyDown += OnPreviewKeyDown;
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) Focus();
        };

        _ = FetchRatesAsync();
    }

    // ================= 标准计算 =================

    private void OnKeyClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string key }) Press(key);
    }

    private void Press(string key)
    {
        switch (key)
        {
            case "C":
                _expr = "";
                _lastExpression = "";
                _justEvaluated = false;
                break;
            case "B":
                if (_expr.Length > 0) _expr = _expr[..^1];
                _justEvaluated = false;
                break;
            case "=":
                Evaluate();
                break;
            case "SQ":
                ApplyUnary(v => v * v, "平方");
                break;
            case "SQRT":
                ApplyUnary(v => v < 0 ? double.NaN : Math.Sqrt(v), "开方");
                break;
            case "INV":
                ApplyUnary(v => Math.Abs(v) < 1e-12 ? double.NaN : 1 / v, "倒数");
                break;
            case "NEG":
                ApplyUnary(v => -v, "取反");
                break;
            default:
                Append(key);
                break;
        }
        UpdateDisplay();
    }

    private void ApplyUnary(Func<double, double> operation, string label)
    {
        if (string.IsNullOrWhiteSpace(_expr)) return;
        try
        {
            double value = ExpressionEvaluator.Evaluate(_expr);
            double result = operation(value);
            if (double.IsNaN(result))
            {
                App.ShowToast("算不了", label == "开方" ? "负数没有实数平方根" : "0 不能取倒数");
                return;
            }
            _lastExpression = $"{label}({_expr})";
            _expr = Format(result);
            _justEvaluated = true;
        }
        catch
        {
            App.ShowToast("算式还没写完", "先把这一小步补完整再算");
        }
    }

    private void Append(string key)
    {
        bool isOperator = key is "+" or "-" or "*" or "/" or "%";

        if (_justEvaluated)
        {
            if (!isOperator)
            {
                _expr = "";
                _lastExpression = "";
            }
            _justEvaluated = false;
        }

        if (_expr.Length >= 40) return;

        if (isOperator && _expr.Length > 0 && _expr[^1] is '+' or '-' or '*' or '/' or '%')
        {
            _expr = _expr[..^1] + key;
            return;
        }

        if (key == ".")
        {
            int start = _expr.Length - 1;
            while (start >= 0 && (char.IsDigit(_expr[start]) || _expr[start] == '.')) start--;
            if (_expr[(start + 1)..].Contains('.')) return;
            if (_expr.Length == 0 || _expr[^1] is '+' or '-' or '*' or '/' or '%' or '(') _expr += "0";
        }

        _expr += key;
    }

    private void Evaluate()
    {
        if (string.IsNullOrWhiteSpace(_expr)) return;
        try
        {
            double value = ExpressionEvaluator.Evaluate(_expr);
            _lastExpression = _expr;
            _expr = Format(value);
            _justEvaluated = true;
        }
        catch (Exception ex)
        {
            _justEvaluated = false;
            ExprText.Text = ex.Message;
            ResultText.Text = "—";
            return;
        }
    }

    private void UpdateDisplay()
    {
        if (_justEvaluated && _lastExpression.Length > 0)
        {
            ExprText.Text = _lastExpression + " =";
            ResultText.Text = _expr;
            return;
        }

        if (_expr.Length == 0)
        {
            ExprText.Text = "按 = 计算";
            ResultText.Text = "0";
            return;
        }

        ExprText.Text = _expr;
        try
        {
            var preview = ExpressionEvaluator.Evaluate(_expr.TrimEnd('+', '-', '*', '/'));
            ResultText.Text = Format(preview);
        }
        catch
        {
            ResultText.Text = _expr;
        }
    }

    private void OnCopyResult(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(ResultText.Text);
            App.ShowToast("已复制", ResultText.Text);
        }
        catch
        {
        }
    }

    private static string Format(double value)
    {
        if (Math.Abs(value) >= 1e12 || (Math.Abs(value) > 0 && Math.Abs(value) < 1e-8))
            return value.ToString("0.######E+0", CultureInfo.InvariantCulture);
        return Math.Round(value, 8).ToString("0.########", CultureInfo.InvariantCulture);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (StandardPanel.Visibility != Visibility.Visible) return;

        string key = null;
        if (e.Key is >= Key.D0 and <= Key.D9)
            key = ((int)e.Key - (int)Key.D0).ToString();
        else if (e.Key is >= Key.NumPad0 and <= Key.NumPad9)
            key = ((int)e.Key - (int)Key.NumPad0).ToString();

        switch (e.Key)
        {
            case Key.Add:
            case Key.OemPlus when Keyboard.Modifiers == ModifierKeys.Shift:
                key = "+";
                break;
            case Key.Subtract:
            case Key.OemMinus:
                key = "-";
                break;
            case Key.Multiply:
                key = "*";
                break;
            case Key.Divide:
                key = "/";
                break;
            case Key.Decimal:
            case Key.OemPeriod:
                key = ".";
                break;
            case Key.OemPlus:
            case Key.Enter:
                key = "=";
                break;
            case Key.Back:
                key = "B";
                break;
            case Key.Escape:
                key = "C";
                break;
            case Key.D8 when Keyboard.Modifiers == ModifierKeys.Shift:
                key = "(";
                break;
            case Key.D9 when Keyboard.Modifiers == ModifierKeys.Shift:
                key = ")";
                break;
        }

        if (key != null)
        {
            Press(key);
            e.Handled = true;
        }
    }

    // ================= 标签切换 =================

    private void OnTabClick(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton button && int.TryParse(button.Tag?.ToString(), out int index))
        {
            SwitchTab(index);
        }
    }

    private void SwitchTab(int index)
    {
        TabStandard.IsChecked = index == 0;
        TabDate.IsChecked = index == 1;
        TabUnit.IsChecked = index == 2;

        StandardPanel.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
        DatePanel.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
        UnitPanel.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;

        if (index == 0) Focus();
    }

    // ================= 日期计算 =================

    private void OnDateModeClick(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { Tag: string tag } && int.TryParse(tag, out int mode))
        {
            _dateMode = mode;
            DateTabDiff.IsChecked = mode == 0;
            DateTabWork.IsChecked = mode == 1;
            DateTabAge.IsChecked = mode == 2;
            RecalcDates();
        }
    }

    private void OnDateInputChanged(object sender, TextChangedEventArgs e) => RecalcDates();

    private void RecalcDates()
    {
        if (DateResultBig == null) return;

        if (!DateTime.TryParseExact(Date1Box.Text?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d1) ||
            !DateTime.TryParseExact(Date2Box.Text?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d2))
        {
            DateResultBig.Text = "—";
            DateResultUnit.Text = "";
            DateResultSub.Text = "日期格式：2026-11-14";
            return;
        }

        var start = d1 <= d2 ? d1 : d2;
        var end = d1 <= d2 ? d2 : d1;
        int days = (end.Date - start.Date).Days;

        int workdays = 0;
        for (var d = start.Date.AddDays(1); d <= end.Date; d = d.AddDays(1))
        {
            if (HolidayService.IsWorkday(d)) workdays++;
        }

        int age = Math.Max(0, end.Year - start.Year - (end.Month < start.Month || (end.Month == start.Month && end.Day < start.Day) ? 1 : 0));

        switch (_dateMode)
        {
            case 0:
                DateResultBig.Text = days.ToString();
                DateResultUnit.Text = "天";
                DateResultSub.Text = $"≈ {days / 7} 周 {days % 7} 天";
                break;
            case 1:
                DateResultBig.Text = workdays.ToString();
                DateResultUnit.Text = "个工作日";
                DateResultSub.Text = $"共 {days} 个自然日，已扣除周末与法定假日";
                break;
            default:
                DateResultBig.Text = age.ToString();
                DateResultUnit.Text = "岁";
                DateResultSub.Text = $"出生日期到 {end:yyyy年M月d日}";
                break;
        }
    }

    // ================= 单位换算 =================

    private void OnUnitCategoryClick(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton button || button.Tag is not string category) return;

        _unitCategory = category;
        UnitCurrency.IsChecked = category == "货币";
        UnitLength.IsChecked = category == "长度";
        UnitWeight.IsChecked = category == "重量";
        UnitArea.IsChecked = category == "面积";
        UnitTemp.IsChecked = category == "温度";
        UnitVolume.IsChecked = category == "体积";

        LoadUnits();
        ConvertUnits();
    }

    private void LoadUnits()
    {
        _updatingUnits = true;

        var units = UnitSets[_unitCategory];
        UnitFromCombo.ItemsSource = units;
        UnitToCombo.ItemsSource = units;

        (string from, string to) = _unitCategory switch
        {
            "重量" => ("千克", "克"),
            "温度" => ("摄氏度", "华氏度"),
            "面积" => ("平方米", "亩"),
            "体积" => ("升", "毫升"),
            "货币" => ("人民币 ¥", "美元 $"),
            _ => ("米", "厘米")
        };

        UnitFromCombo.SelectedIndex = Math.Max(0, Array.IndexOf(units, from));
        UnitToCombo.SelectedIndex = Math.Max(0, Array.IndexOf(units, to));

        _updatingUnits = false;

        bool currency = _unitCategory == "货币";
        BtnRefreshRate.Visibility = currency ? Visibility.Visible : Visibility.Collapsed;
        UnitUpdateText.Text = currency
            ? (_ratesUpdatedAt.HasValue ? $"汇率更新于 {_ratesUpdatedAt:HH:mm}（实时）" : "正在获取实时汇率…")
            : "按国际标准单位换算";
        UnitNoteText.Text = currency
            ? "货币按国际中间价实时换算（联网获取）；与银行挂牌价存在正常小幅差异，汇率波动，结果以成交时为准。"
            : "长度、重量、面积、温度、体积按国际标准单位换算，结果保留 8 位小数。";
    }

    private void OnUnitValueChanged(object sender, TextChangedEventArgs e) => ConvertUnits();

    private void OnUnitSelectionChanged(object sender, SelectionChangedEventArgs e) => ConvertUnits();

    private void OnSwapUnits(object sender, RoutedEventArgs e)
    {
        int from = UnitFromCombo.SelectedIndex;
        int to = UnitToCombo.SelectedIndex;
        _updatingUnits = true;
        UnitFromCombo.SelectedIndex = to;
        UnitToCombo.SelectedIndex = from;
        _updatingUnits = false;
        ConvertUnits();
    }

    private void ConvertUnits()
    {
        if (_updatingUnits) return;
        if (UnitFromCombo == null || UnitToCombo == null || UnitValueBox == null || UnitResultText == null) return;
        if (UnitFromCombo.SelectedItem is not string from || UnitToCombo.SelectedItem is not string to) return;

        var text = UnitValueBox.Text?.Trim();
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) &&
            !double.TryParse(text, out value))
        {
            UnitResultText.Text = "—";
            UnitRateText.Text = "";
            return;
        }

        double result = _unitCategory == "温度"
            ? ConvertTemperature(value, from, to)
            : value * Factors[from] / Factors[to];

        UnitResultText.Text = $"{Format(result)} {UnitSymbol(to)}";
        UnitRateText.Text = BuildRateLine(from, to);
    }

    private static string UnitSymbol(string unit)
    {
        if (unit.EndsWith("¥") || unit.EndsWith("$") || unit.EndsWith("€") || unit.EndsWith("£") || unit.EndsWith("₩"))
            return "";
        if (unit.StartsWith("港币")) return "";
        return unit;
    }

    private static string BuildRateLine(string from, string to)
    {
        if (from == to) return $"1 {from} = 1 {to}";
        double one = Factors[from] / Factors[to];
        double back = Factors[to] / Factors[from];
        return $"1 {from} = {Format(one)} {to}（1 {to} ≈ {Format(back)} {from}）";
    }

    private static double ConvertTemperature(double value, string from, string to)
    {
        double celsius = from switch
        {
            "摄氏度" => value,
            "华氏度" => (value - 32) * 5 / 9,
            "开尔文" => value - 273.15,
            _ => value
        };

        return to switch
        {
            "摄氏度" => celsius,
            "华氏度" => celsius * 9 / 5 + 32,
            "开尔文" => celsius + 273.15,
            _ => celsius
        };
    }

    private async void OnRefreshRates(object sender, RoutedEventArgs e)
    {
        await FetchRatesAsync(force: true);
    }

    private async Task FetchRatesAsync(bool force = false)
    {
        if (_ratesUpdatedAt.HasValue && !force) return;
        if (_unitCategory == "货币") UnitUpdateText.Text = "正在获取实时汇率…";

        try
        {
            using var response = await Http.GetAsync("https://open.er-api.com/v6/latest/CNY");
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("rates", out var rates)) throw new FormatException("汇率格式不对");

            void Set(string code, string unit)
            {
                if (rates.TryGetProperty(code, out var rate) && rate.TryGetDouble(out double value) && value > 0)
                {
                    LiveRates[unit] = 1 / value;
                    Factors[unit] = LiveRates[unit];
                }
            }

            Set("USD", "美元 $");
            Set("EUR", "欧元 €");
            Set("JPY", "日元 ¥");
            Set("HKD", "港币 HK$");
            Set("GBP", "英镑 £");
            Set("KRW", "韩元 ₩");

            _ratesUpdatedAt = DateTime.Now;
            if (_unitCategory == "货币")
            {
                UnitUpdateText.Text = $"汇率更新于 {_ratesUpdatedAt:HH:mm}（实时）";
                ConvertUnits();
            }
        }
        catch
        {
            if (_unitCategory == "货币")
                UnitUpdateText.Text = _ratesUpdatedAt.HasValue
                    ? $"汇率更新于 {_ratesUpdatedAt:HH:mm}"
                    : "联网失败，暂用内置参考汇率";
        }
    }

    private void OnCopyUnitResult(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(UnitResultText.Text);
            App.ShowToast("已复制", UnitResultText.Text);
        }
        catch
        {
        }
    }
}
