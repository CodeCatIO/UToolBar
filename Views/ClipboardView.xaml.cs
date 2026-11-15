using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EdgeBar.Services;

namespace EdgeBar.Views;

public partial class ClipboardView : UserControl
{
    public ClipboardView()
    {
        InitializeComponent();

        if (App.State != null)
            App.State.Clips.CollectionChanged += OnClipsChanged;

        Loaded += (_, _) => Refresh();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) Refresh();
        };
    }

    private void OnClipsChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        if (IsVisible) Refresh();
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => Refresh();

    private void Refresh()
    {
        if (App.State == null) return;

        var query = SearchBox.Text?.Trim() ?? "";
        var items = App.State.Clips
            .Where(c => query.Length == 0 ||
                        (c.Kind == "text" && (c.Text ?? "").Contains(query, StringComparison.CurrentCultureIgnoreCase)) ||
                        (c.Kind == "image" && c.PreviewText.Contains(query)))
            .ToList();

        ClipList.ItemsSource = items;
        CountText.Text = $"{App.State.Clips.Count} 条记录 · 最多保留 60 条";
        EmptyHint.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyHint.Children[0].SetValue(TextBlock.TextProperty,
            App.State.Clips.Count == 0 ? "还没有复制记录" : "没有找到匹配的内容");
    }

    private void OnItemClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Models.ClipItem item })
        {
            ClipboardService.CopyToClipboard(item);
            App.ShowToast("已复制", item.PreviewText);
        }
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement { DataContext: Models.ClipItem item })
        {
            ClipboardService.Remove(item);
            Refresh();
        }
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        if (App.State == null || App.State.Clips.Count == 0) return;
        ClipboardService.ClearAll();
        Refresh();
        App.ShowToast("已清空", "剪贴板历史记录已删除");
    }
}
