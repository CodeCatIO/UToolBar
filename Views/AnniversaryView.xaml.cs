using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EdgeBar.Models;
using EdgeBar.Services;

namespace EdgeBar.Views;

public partial class AnniversaryView : UserControl
{
    private static readonly int[] RemindDays = { 0, 1, 3, 7, 15, 30 };

    private AnniversaryItem _editing;
    private bool _updatingCombos;

    public AnniversaryView()
    {
        InitializeComponent();

        RemindCombo.ItemsSource = new[]
        {
            "当天提醒", "提前 1 天", "提前 3 天", "提前 7 天", "提前 15 天", "提前 30 天"
        };

        if (App.State != null)
            App.State.Anniversaries.CollectionChanged += OnChanged;

        Loaded += (_, _) => Refresh();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) Refresh();
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && EditOverlay.Visibility == Visibility.Visible)
            {
                CloseEdit();
                e.Handled = true;
            }
        };
    }

    private void OnChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        if (IsVisible) Refresh();
    }

    private void Refresh()
    {
        var items = App.State.Anniversaries
            .OrderBy(a => a.DaysLeft)
            .ThenBy(a => a.Title, StringComparer.CurrentCulture)
            .ToList();

        foreach (var item in items) item.Refresh();

        ItemList.ItemsSource = items;
        EmptyHint.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CountText.Text = items.Count == 0
            ? "生日、纪念日、还款日都放这里"
            : $"{items.Count} 个日子 · 最近的是「{items[0].Title}」";
    }

    // ================= 新增 / 编辑 =================

    private void OnAddClick(object sender, RoutedEventArgs e)
    {
        _editing = null;
        EditHeader.Text = "记一个重要的日子";
        DeleteButton.Visibility = Visibility.Collapsed;
        NameBox.Text = "";
        LunarSwitch.IsChecked = false;
        RemindCombo.SelectedIndex = 2;

        BuildMonthOptions(false);
        MonthCombo.SelectedIndex = 0;
        BuildDayOptions(1, false, 1);

        ShowEdit();
    }

    private void OnEditClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: AnniversaryItem item }) return;

        _editing = item;
        EditHeader.Text = "编辑日子";
        DeleteButton.Visibility = Visibility.Visible;
        NameBox.Text = item.Title;
        LunarSwitch.IsChecked = item.IsLunar;
        RemindCombo.SelectedIndex = Math.Max(0, Array.IndexOf(RemindDays, item.RemindDaysBefore));

        BuildMonthOptions(item.IsLunar);
        MonthCombo.SelectedIndex = Math.Clamp(item.Month, 1, 12) - 1;
        BuildDayOptions(item.Month, item.IsLunar, item.Day);

        ShowEdit();
    }

    private void ShowEdit()
    {
        EditOverlay.Visibility = Visibility.Visible;
        NameBox.Focus();
        NameBox.CaretIndex = NameBox.Text.Length;
    }

    private void CloseEdit()
    {
        EditOverlay.Visibility = Visibility.Collapsed;
        _editing = null;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => CloseEdit();

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var title = NameBox.Text?.Trim() ?? "";
        if (title.Length == 0)
        {
            App.ShowToast("还差一个名字", "比如「妈妈生日」「结婚纪念日」");
            return;
        }
        if (MonthCombo.SelectedIndex < 0 || DayCombo.SelectedIndex < 0) return;

        int month = MonthCombo.SelectedIndex + 1;
        int day = DayCombo.SelectedIndex + 1;
        bool lunar = LunarSwitch.IsChecked == true;
        int remind = RemindDays[Math.Clamp(RemindCombo.SelectedIndex, 0, RemindDays.Length - 1)];

        if (_editing == null)
        {
            _editing = new AnniversaryItem();
            App.State.Anniversaries.Add(_editing);
        }

        _editing.Title = title;
        _editing.IsLunar = lunar;
        _editing.Month = month;
        _editing.Day = day;
        _editing.RemindDaysBefore = remind;
        _editing.LastNotifiedYear = 0;
        _editing.LastAdvanceYear = 0;
        _editing.Refresh();

        App.State.Notify();
        CloseEdit();
        Refresh();
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: AnniversaryItem item })
        {
            App.State.Anniversaries.Remove(item);
            Refresh();
        }
    }

    private void OnDeleteEditClick(object sender, RoutedEventArgs e)
    {
        if (_editing == null) return;
        App.State.Anniversaries.Remove(_editing);
        CloseEdit();
        Refresh();
    }

    // ================= 农历 / 公历切换 =================

    private void OnLunarChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        bool lunar = LunarSwitch.IsChecked == true;
        int month = Math.Max(1, MonthCombo.SelectedIndex + 1);
        int day = Math.Max(1, DayCombo.SelectedIndex + 1);

        BuildMonthOptions(lunar);
        MonthCombo.SelectedIndex = Math.Clamp(month, 1, 12) - 1;
        BuildDayOptions(month, lunar, day);
    }

    private void OnDatePartChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _updatingCombos) return;
        int month = Math.Max(1, MonthCombo.SelectedIndex + 1);
        int day = Math.Max(1, DayCombo.SelectedIndex + 1);
        BuildDayOptions(month, LunarSwitch.IsChecked == true, day);
    }

    private void BuildMonthOptions(bool lunar)
    {
        _updatingCombos = true;
        var months = Enumerable.Range(1, 12)
            .Select(m => lunar ? LunarService.LunarMonthName(m) : $"{m} 月")
            .ToList();
        MonthCombo.ItemsSource = months;
        _updatingCombos = false;
    }

    private void BuildDayOptions(int month, bool lunar, int selectedDay)
    {
        _updatingCombos = true;
        int max = lunar ? 30 : DateTime.DaysInMonth(2024, Math.Clamp(month, 1, 12));
        var days = Enumerable.Range(1, max)
            .Select(d => lunar ? LunarService.LunarDayName(d) : $"{d} 日")
            .ToList();
        DayCombo.ItemsSource = days;
        DayCombo.SelectedIndex = Math.Clamp(selectedDay, 1, max) - 1;
        _updatingCombos = false;
    }
}
