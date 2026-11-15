using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace UToolBar.Models;

public enum RepeatMode
{
    None = 0,
    Daily = 1,
    Weekly = 2,
    Monthly = 3
}

public class Subtask : ObservableObject
{
    private string _title = "";
    private bool _done;

    public string Title
    {
        get => _title;
        set => Set(ref _title, value);
    }

    public bool Done
    {
        get => _done;
        set => Set(ref _done, value);
    }

    /// <summary>所属待办，便于子项操作，不参与序列化</summary>
    [JsonIgnore]
    public TodoItem Owner { get; set; }
}

public class TodoItem : ObservableObject
{
    private string _title = "";
    private DateTime _date = DateTime.Today;
    private bool _done;
    private bool _starred;
    private DateTime? _remindAt;
    private RepeatMode _repeat;
    private bool _skipHoliday;
    private bool _notified;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Title
    {
        get => _title;
        set
        {
            if (Set(ref _title, value))
            {
                OnPropertyChanged(nameof(DisplayTitle));
            }
        }
    }

    /// <summary>待办所属日期（只取日期部分）</summary>
    public DateTime Date
    {
        get => _date;
        set
        {
            if (Set(ref _date, value.Date))
            {
                OnPropertyChanged(nameof(DateText));
                OnPropertyChanged(nameof(ShortDate));
            }
        }
    }

    public bool Done
    {
        get => _done;
        set
        {
            if (Set(ref _done, value))
            {
                OnPropertyChanged(nameof(DisplayTitle));
                OnPropertyChanged(nameof(MetaText));
            }
        }
    }

    public bool Starred
    {
        get => _starred;
        set => Set(ref _starred, value);
    }

    public DateTime? RemindAt
    {
        get => _remindAt;
        set
        {
            if (Set(ref _remindAt, value))
            {
                OnPropertyChanged(nameof(MetaText));
                OnPropertyChanged(nameof(HasMeta));
            }
        }
    }

    public RepeatMode Repeat
    {
        get => _repeat;
        set
        {
            if (Set(ref _repeat, value))
            {
                OnPropertyChanged(nameof(MetaText));
                OnPropertyChanged(nameof(HasMeta));
            }
        }
    }

    public bool SkipHoliday
    {
        get => _skipHoliday;
        set => Set(ref _skipHoliday, value);
    }

    /// <summary>是否已经弹过提醒，避免重复打扰</summary>
    public bool Notified
    {
        get => _notified;
        set => Set(ref _notified, value);
    }

    public TodoItem()
    {
        Subtasks = new ObservableCollection<Subtask>();
    }

    private ObservableCollection<Subtask> _subtasks;

    public ObservableCollection<Subtask> Subtasks
    {
        get => _subtasks;
        set
        {
            if (_subtasks != null) _subtasks.CollectionChanged -= OnSubtasksChanged;
            _subtasks = value ?? new ObservableCollection<Subtask>();
            _subtasks.CollectionChanged += OnSubtasksChanged;
            foreach (var item in _subtasks)
            {
                item.Owner = this;
                item.PropertyChanged -= OnSubtaskChanged;
                item.PropertyChanged += OnSubtaskChanged;
            }
            RefreshMeta();
        }
    }

    private void OnSubtasksChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (Subtask item in e.NewItems)
            {
                item.Owner = this;
                item.PropertyChanged -= OnSubtaskChanged;
                item.PropertyChanged += OnSubtaskChanged;
            }
        }
        if (e.OldItems != null)
        {
            foreach (Subtask item in e.OldItems)
            {
                item.PropertyChanged -= OnSubtaskChanged;
            }
        }
        RefreshMeta();
    }

    private void OnSubtaskChanged(object sender, PropertyChangedEventArgs e) => RefreshMeta();

    [JsonIgnore]
    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? "未命名待办" : Title;

    [JsonIgnore]
    public string DateText
    {
        get
        {
            if (Date == DateTime.Today) return "今天";
            if (Date == DateTime.Today.AddDays(1)) return "明天";
            if (Date == DateTime.Today.AddDays(-1)) return "昨天";
            return $"{Date:M月d日} {WeekdayCn(Date)}";
        }
    }

    [JsonIgnore]
    public string ShortDate => Date.ToString("M/d");

    [JsonIgnore]
    public bool HasSubtasks => Subtasks.Count > 0;

    [JsonIgnore]
    public string SubtaskSummary
    {
        get
        {
            if (Subtasks.Count == 0) return "";
            int done = Subtasks.Count(s => s.Done);
            return $"{done}/{Subtasks.Count} 子项";
        }
    }

    [JsonIgnore]
    public string MetaText
    {
        get
        {
            var parts = new List<string>();
            if (RemindAt.HasValue)
            {
                var prefix = RemindAt.Value.Date < DateTime.Today ? "已过期 " : "提醒 ";
                parts.Add(prefix + RemindAt.Value.ToString("HH:mm"));
            }
            switch (Repeat)
            {
                case RepeatMode.Daily: parts.Add("每天"); break;
                case RepeatMode.Weekly: parts.Add("每周"); break;
                case RepeatMode.Monthly: parts.Add("每月"); break;
            }
            if (SkipHoliday && Repeat != RepeatMode.None) parts.Add("跳过休息日");
            if (Subtasks.Count > 0) parts.Add(SubtaskSummary);
            return string.Join(" · ", parts);
        }
    }

    [JsonIgnore]
    public bool HasMeta => MetaText.Length > 0;

    public static string WeekdayCn(DateTime d) => d.DayOfWeek switch
    {
        DayOfWeek.Monday => "周一",
        DayOfWeek.Tuesday => "周二",
        DayOfWeek.Wednesday => "周三",
        DayOfWeek.Thursday => "周四",
        DayOfWeek.Friday => "周五",
        DayOfWeek.Saturday => "周六",
        _ => "周日"
    };

    public void RefreshMeta()
    {
        OnPropertyChanged(nameof(MetaText));
        OnPropertyChanged(nameof(HasMeta));
        OnPropertyChanged(nameof(SubtaskSummary));
        OnPropertyChanged(nameof(HasSubtasks));
    }
}
