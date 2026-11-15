using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using UToolBar.Models;
using UToolBar.Services;

namespace UToolBar.Views;

public partial class TodoView : UserControl
{
    private static readonly string[] WeekdayCn = { "一", "二", "三", "四", "五", "六", "日" };

    private readonly ObservableCollection<TodoGroup> _groups = new();
    private readonly ObservableCollection<Subtask> _editSubtasks = new();

    private int _scope;
    private int _weekOffset;
    private DateTime _selectedDate = DateTime.Today;
    private DateTime _monthCursor = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private TodoItem _editing;

    public TodoView()
    {
        InitializeComponent();
        GroupList.ItemsSource = _groups;
        EditSubtaskList.ItemsSource = _editSubtasks;

        if (App.State != null)
            App.State.Todos.CollectionChanged += OnTodosChanged;

        Loaded += (_, _) => Refresh();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) Refresh();
        };
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void OnTodosChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        if (IsVisible) Refresh();
    }

    // ================= 主刷新 =================

    public void Refresh()
    {
        if (App.State == null) return;

        TabDay.IsChecked = _scope == 0;
        TabWeek.IsChecked = _scope == 1;
        TabMonth.IsChecked = _scope == 2;
        TabFestival.IsChecked = _scope == 3;

        DateScroll.Visibility = _scope == 0 ? Visibility.Visible : Visibility.Collapsed;
        PeriodNav.Visibility = _scope is 1 or 2 ? Visibility.Visible : Visibility.Collapsed;
        ListScroll.Visibility = _scope == 3 ? Visibility.Collapsed : Visibility.Visible;
        MemorialScroll.Visibility = _scope == 3 ? Visibility.Visible : Visibility.Collapsed;
        TitlePillHost.Visibility = _scope == 3 ? Visibility.Collapsed : Visibility.Visible;
        EmptyHint.Visibility = Visibility.Collapsed;

        if (_scope == 3)
        {
            BuildMemorial();
            return;
        }

        DateTime start, end;
        if (_scope == 0)
        {
            start = end = _selectedDate.Date;
            BuildDateStrip(_selectedDate.AddDays(-3), 21, 55, 6);
        }
        else if (_scope == 1)
        {
            DateTime monday = DateTime.Today
                .AddDays(-((int)DateTime.Today.DayOfWeek + 6) % 7)
                .AddDays(_weekOffset * 7);
            start = monday;
            end = monday.AddDays(6);
            PeriodText.Text = (_weekOffset == 0 ? "本周 " : "") + $"{start:M/d} - {end:M/d}";
        }
        else
        {
            _monthCursor = new DateTime(_monthCursor.Year, _monthCursor.Month, 1);
            start = _monthCursor;
            end = _monthCursor.AddMonths(1).AddDays(-1);
            PeriodText.Text = $"{_monthCursor:yyyy年M月}";
        }

        var scopeItems = App.State.Todos
            .Where(t => t.Date.Date >= start && t.Date.Date <= end)
            .Where(t => App.State.Settings.ShowCompleted || !t.Done)
            .ToList();

        _groups.Clear();
        if (_scope == 0)
        {
            if (scopeItems.Count > 0)
            {
                _groups.Add(new TodoGroup
                {
                    Title = "",
                    ShowTitle = false,
                    Items = new ObservableCollection<TodoItem>(Order(scopeItems))
                });
            }
        }
        else
        {
            foreach (var group in scopeItems.GroupBy(t => t.Date.Date).OrderBy(g => g.Key))
            {
                _groups.Add(new TodoGroup
                {
                    Title = GroupTitle(group.Key),
                    ShowTitle = true,
                    Items = new ObservableCollection<TodoItem>(Order(group))
                });
            }
        }

        if (_scope == 0)
            ScopeTitle.Text = $"{_selectedDate:M月d日} {TodoItem.WeekdayCn(_selectedDate)}";
        else if (_scope == 1)
            ScopeTitle.Text = "本周待办";
        else
            ScopeTitle.Text = "本月待办";

        ScopeCount.Text = $"{scopeItems.Count} 项";
        EmptyText.Text = _scope switch
        {
            1 => "本周暂无待办。",
            2 => "本月暂无待办。",
            _ => "今天暂无待办。"
        };
        EmptyHint.Visibility = scopeItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static List<TodoItem> Order(IEnumerable<TodoItem> items) => items
        .OrderByDescending(t => t.Starred)
        .ThenBy(t => t.Done)
        .ThenBy(t => t.Title, StringComparer.CurrentCulture)
        .ToList();

    private static bool IsThisWeek(DateTime monday)
    {
        var today = DateTime.Today;
        var thisMonday = today.AddDays(-((int)today.DayOfWeek + 6) % 7);
        return monday.Date == thisMonday;
    }    private static string GroupTitle(DateTime date)
    {
        if (date == DateTime.Today) return $"今天 · {date:M月d日}";
        if (date == DateTime.Today.AddDays(1)) return $"明天 · {date:M月d日}";
        return $"{TodoItem.WeekdayCn(date)} · {date:M月d日}";
    }

    // ================= 日期条 =================

    private void BuildDateStrip(DateTime start, int count, double cardWidth, double gap)
    {
        DateStrip.Children.Clear();

        int selectedIndex = 0;
        for (int i = 0; i < count; i++)
        {
            var date = start.AddDays(i);
            bool isToday = date.Date == DateTime.Today;
            bool isSelected = date.Date == _selectedDate.Date;
            if (isSelected) selectedIndex = i;

            bool highlight = isToday || isSelected;
            bool dark = App.IsDarkMode;
            var card = new Border
            {
                Width = cardWidth,
                Height = 50,
                Margin = new Thickness(0, 0, gap, 0),
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(highlight
                    ? (dark ? Color.FromArgb(0xE6, 0x1F, 0x2A, 0x38) : Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF))
                    : (dark ? Color.FromArgb(0xCC, 0x1B, 0x25, 0x33) : Color.FromArgb(0xD9, 0xFF, 0xFF, 0xFF))),
                BorderBrush = highlight
                    ? new SolidColorBrush(Color.FromRgb(0x7F, 0xAF, 0xDC))
                    : Brushes.Transparent,
                BorderThickness = new Thickness(highlight ? 1.2 : 0),
                Cursor = Cursors.Hand,
                Tag = date
            };

            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(new TextBlock
            {
                Text = date.ToString("M/d"),
                FontSize = cardWidth < 50 ? 11 : 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(dark ? Color.FromRgb(0xF2, 0xF5, 0xFA) : Color.FromRgb(0x2B, 0x30, 0x37)),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            stack.Children.Add(new TextBlock
            {
                Text = isToday ? "今天" : "周" + WeekdayCn[((int)date.DayOfWeek + 6) % 7],
                FontSize = cardWidth < 50 ? 9 : 10.5,
                Foreground = new SolidColorBrush(dark ? Color.FromRgb(0x9A, 0xA7, 0xB6) : Color.FromRgb(0x6F, 0x7B, 0x87)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 1, 0, 0)
            });

            if (isToday)
            {
                stack.Children.Add(new System.Windows.Shapes.Ellipse
                {
                    Width = 4,
                    Height = 4,
                    Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0x5A, 0x52)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 2, 0, 0)
                });
            }

            card.Child = stack;
            card.MouseLeftButtonUp += OnDateCardClick;
            DateStrip.Children.Add(card);
        }

        Dispatcher.BeginInvoke(new Action(() =>
        {
            double offset = Math.Max(0, (selectedIndex - 2) * (cardWidth + gap));
            DateScroll.ScrollToHorizontalOffset(offset);
        }), DispatcherPriority.Loaded);
    }

    private void OnDateCardClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border { Tag: DateTime date })
        {
            _selectedDate = date;
            if (_scope == 2) _monthCursor = new DateTime(date.Year, date.Month, 1);
            Refresh();
        }
    }

    // ================= 周 / 月切换 =================

    private void OnPrevPeriod(object sender, RoutedEventArgs e)
    {
        if (_scope == 1) _weekOffset--;
        else _monthCursor = _monthCursor.AddMonths(-1);
        Refresh();
    }

    private void OnNextPeriod(object sender, RoutedEventArgs e)
    {
        if (_scope == 1) _weekOffset++;
        else _monthCursor = _monthCursor.AddMonths(1);
        Refresh();
    }

    // ================= 生日 / 纪念日 =================

    private string _memorialKind = "生日";

    private void BuildMemorial()
    {
        var all = App.State.Anniversaries;
        var birthdays = all.Where(a => a.Kind == "生日").OrderBy(a => a.DaysLeft).ToList();
        var memorials = all.Where(a => a.Kind != "生日").OrderBy(a => a.DaysLeft).ToList();

        BirthdayList.ItemsSource = birthdays;
        MemorialList.ItemsSource = memorials;
        BirthdayCount.Text = birthdays.Count.ToString();
        MemorialCount.Text = memorials.Count.ToString();
        BirthdayEmpty.Visibility = birthdays.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        MemorialEmpty.Visibility = memorials.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        ScopeTitle.Text = "记节日";
        ScopeCount.Text = $"{birthdays.Count + memorials.Count} 个";
    }

    private void OnAddBirthday(object sender, RoutedEventArgs e) => OpenMemorial("生日");
    private void OnAddMemorial(object sender, RoutedEventArgs e) => OpenMemorial("纪念日");

    private void OpenMemorial(string kind)
    {
        _memorialKind = kind;
        MemorialTitle.Text = kind == "生日" ? "添加生日" : "添加纪念日";
        MemorialNameBox.Text = "";
        MemorialDateBox.Text = "";
        MemorialOverlay.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(new Action(() => MemorialNameBox.Focus()), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void OnMemorialCancel(object sender, RoutedEventArgs e)
    {
        MemorialOverlay.Visibility = Visibility.Collapsed;
    }

    private void OnMemorialSave(object sender, RoutedEventArgs e)
    {
        string title = MemorialNameBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            App.ShowToast("还差个名字", "给这个日子起个称呼吧");
            return;
        }

        int month = DateTime.Today.Month;
        int day = DateTime.Today.Day;
        string text = MemorialDateBox.Text?.Trim();
        var formats = new[] { "M-d", "M/d", "M月d日", "yyyy-M-d", "yyyy/M/d", "yyyy年M月d日" };
        if (!string.IsNullOrWhiteSpace(text))
        {
            if (!DateTime.TryParseExact(text, formats, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var parsed) &&
                !DateTime.TryParse(text, out parsed))
            {
                App.ShowToast("日期没看懂", "试试 5-20 或 2026-05-20");
                return;
            }
            month = parsed.Month;
            day = parsed.Day;
        }

        App.State.Anniversaries.Add(new AnniversaryItem
        {
            Title = title,
            Month = month,
            Day = day,
            Kind = _memorialKind
        });

        MemorialOverlay.Visibility = Visibility.Collapsed;
        BuildMemorial();
    }

    // ================= 添加 / 勾选 / 删除 =================

    private void OnScopeClick(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { Tag: string tag } && int.TryParse(tag, out int scope))
        {
            _scope = scope;
            if (scope == 2) _monthCursor = new DateTime(_selectedDate.Year, _selectedDate.Month, 1);
            Refresh();
        }
    }

    private void OnShowAddBar(object sender, RoutedEventArgs e)
    {
        AddBar.Visibility = AddBar.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        if (AddBar.Visibility == Visibility.Visible)
        {
            QuickAddBox.Focus();
            Dispatcher.BeginInvoke(new Action(() => QuickAddBox.Focus()), DispatcherPriority.Input);
        }
    }

    private void OnQuickAddKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            AddQuick();
            e.Handled = true;
        }
    }

    private void OnQuickAddClick(object sender, RoutedEventArgs e) => AddQuick();

    private void AddQuick()
    {
        var title = QuickAddBox.Text?.Trim();
        if (string.IsNullOrEmpty(title)) return;

        var date = _scope == 0 ? _selectedDate.Date : DateTime.Today;
        App.State.Todos.Add(new TodoItem
        {
            Title = title,
            Date = date
        });
        QuickAddBox.Clear();
        Refresh();
        QuickAddBox.Focus();
    }

    private void OnTaskChecked(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton check || check.DataContext is not TodoItem item) return;

        item.Done = check.IsChecked == true;
        if (item.Done && item.Repeat != RepeatMode.None) CreateNextOccurrence(item);
        App.State.Notify();
        Refresh();
    }

    private void CreateNextOccurrence(TodoItem item)
    {
        DateTime next = item.Repeat switch
        {
            RepeatMode.Daily => item.Date.AddDays(1),
            RepeatMode.Weekly => item.Date.AddDays(7),
            RepeatMode.Monthly => item.Date.AddMonths(1),
            _ => item.Date
        };

        int guard = 0;
        while (item.SkipHoliday && HolidayService.IsOffDay(next) && guard++ < 400)
        {
            next = item.Repeat switch
            {
                RepeatMode.Daily => next.AddDays(1),
                RepeatMode.Weekly => next.AddDays(7),
                RepeatMode.Monthly => next.AddMonths(1),
                _ => next.AddDays(1)
            };
        }

        if (App.State.Todos.Any(t => t.Date == next.Date && t.Title == item.Title && !t.Done)) return;

        var copy = new TodoItem
        {
            Title = item.Title,
            Date = next.Date,
            Starred = item.Starred,
            Repeat = item.Repeat,
            SkipHoliday = item.SkipHoliday,
            RemindAt = item.RemindAt.HasValue ? next.Date + item.RemindAt.Value.TimeOfDay : null
        };
        foreach (var sub in item.Subtasks)
            copy.Subtasks.Add(new Subtask { Title = sub.Title });

        App.State.Todos.Add(copy);
    }

    private void OnSubtaskClick(object sender, RoutedEventArgs e)
    {
        // 子项完成状态由绑定写回，父项摘要会自动刷新
        App.State.Notify();
    }

    // ================= 编辑 =================

    private void OnAddSubtaskClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TodoItem item })
        {
            OpenEdit(item);
            Dispatcher.BeginInvoke(new Action(() => NewSubtaskBox.Focus()), DispatcherPriority.Input);
        }
    }

    private void OnEditClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TodoItem item }) OpenEdit(item);
    }

    private void OnTaskTitleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TodoItem item })
        {
            OpenEdit(item);
            e.Handled = true;
        }
    }

    private void OpenEdit(TodoItem item)
    {
        _editing = item;
        EditTitleBox.Text = item.Title;
        EditDateBox.Text = item.Date.ToString("yyyy-MM-dd");
        EditRemindBox.Text = item.RemindAt?.ToString("HH:mm") ?? "";
        SelectRepeat(item.Repeat);
        SkipHolidaySwitch.IsChecked = item.SkipHoliday;

        _editSubtasks.Clear();
        foreach (var sub in item.Subtasks)
            _editSubtasks.Add(new Subtask { Title = sub.Title, Done = sub.Done });

        EditOverlay.Visibility = Visibility.Visible;
    }

    private void CloseEdit()
    {
        EditOverlay.Visibility = Visibility.Collapsed;
        _editing = null;
        _editSubtasks.Clear();
    }

    private void OnEditCancel(object sender, RoutedEventArgs e) => CloseEdit();

    private void OnEditSave(object sender, RoutedEventArgs e)
    {
        if (_editing == null) return;

        var title = EditTitleBox.Text?.Trim() ?? "";
        if (title.Length == 0)
        {
            App.ShowToast("还差一个标题", "给这条待办写一句话吧");
            return;
        }

        if (!DateTime.TryParseExact(EditDateBox.Text?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date))
        {
            App.ShowToast("日期格式不对", "请用 2026-11-14 这样的格式");
            return;
        }

        DateTime? remindAt = null;
        var remindText = EditRemindBox.Text?.Trim() ?? "";
        if (remindText.Length > 0)
        {
            if (!DateTime.TryParseExact(remindText, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            {
                App.ShowToast("提醒时间不对", "请用 09:30 这样的格式，或留空");
                return;
            }
            remindAt = date.Date + time.TimeOfDay;
        }

        _editing.Title = title;
        _editing.Date = date.Date;
        _editing.RemindAt = remindAt;
        _editing.Notified = false;
        _editing.Repeat = SelectedRepeat();
        _editing.SkipHoliday = SkipHolidaySwitch.IsChecked == true;

        _editing.Subtasks.Clear();
        foreach (var sub in _editSubtasks)
            _editing.Subtasks.Add(new Subtask { Title = sub.Title, Done = sub.Done });

        App.State.Notify();
        CloseEdit();
        Refresh();
    }

    private void OnEditDelete(object sender, RoutedEventArgs e)
    {
        if (_editing == null) return;
        App.State.Todos.Remove(_editing);
        CloseEdit();
        Refresh();
    }

    private void OnRepeatChip(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton clicked) return;
        foreach (var chip in new[] { RepeatNone, RepeatDaily, RepeatWeekly, RepeatMonthly })
            chip.IsChecked = ReferenceEquals(chip, clicked);
    }

    private void SelectRepeat(RepeatMode mode)
    {
        RepeatNone.IsChecked = mode == RepeatMode.None;
        RepeatDaily.IsChecked = mode == RepeatMode.Daily;
        RepeatWeekly.IsChecked = mode == RepeatMode.Weekly;
        RepeatMonthly.IsChecked = mode == RepeatMode.Monthly;
    }

    private RepeatMode SelectedRepeat()
    {
        if (RepeatDaily.IsChecked == true) return RepeatMode.Daily;
        if (RepeatWeekly.IsChecked == true) return RepeatMode.Weekly;
        if (RepeatMonthly.IsChecked == true) return RepeatMode.Monthly;
        return RepeatMode.None;
    }

    private void OnNewSubtaskKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            AddEditSubtask();
            e.Handled = true;
        }
    }

    private void OnAddSubtask(object sender, RoutedEventArgs e) => AddEditSubtask();

    private void AddEditSubtask()
    {
        var text = NewSubtaskBox.Text?.Trim();
        if (string.IsNullOrEmpty(text)) return;
        if (_editSubtasks.Count >= 30)
        {
            App.ShowToast("子任务太多了", "一条待办最多 30 个子项");
            return;
        }
        _editSubtasks.Add(new Subtask { Title = text });
        NewSubtaskBox.Clear();
        NewSubtaskBox.Focus();
    }

    private void OnEditSubtaskDelete(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Subtask sub }) _editSubtasks.Remove(sub);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && EditOverlay.Visibility == Visibility.Visible)
        {
            CloseEdit();
            e.Handled = true;
        }
    }
}

public class TodoGroup
{
    public string Title { get; set; } = "";
    public bool ShowTitle { get; set; } = true;
    public ObservableCollection<TodoItem> Items { get; set; } = new();
}
