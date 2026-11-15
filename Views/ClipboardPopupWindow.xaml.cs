using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using EdgeBar.Models;
using EdgeBar.Services;

namespace EdgeBar.Views;

public partial class ClipboardPopupWindow : Window
{
    private readonly ObservableCollection<ClipRow> _rows = new();
    private readonly MenuItem _menuCopy = new() { Header = "复制" };
    private readonly MenuItem _menuEdit = new() { Header = "修改内容" };
    private readonly MenuItem _menuPin = new() { Header = "贴到桌面" };
    private readonly MenuItem _menuEditPin = new() { Header = "编辑后贴到桌面" };
    private readonly MenuItem _menuDelete = new() { Header = "删除这条" };
    private ClipItem _editing;
    private bool _menuOpen;

    public ClipboardPopupWindow()
    {
        InitializeComponent();
        ClipList.ItemsSource = _rows;
        BuildContextMenu();
    }

    private ClipItem Current => (ClipList.SelectedItem as ClipRow)?.Item;

    private void BuildContextMenu()
    {
        _menuCopy.Click += (_, _) => { if (Current != null) Copy(Current); };
        _menuEdit.Click += (_, _) => BeginEdit();
        _menuPin.Click += (_, _) => PinToDesktop();
        _menuEditPin.Click += (_, _) => EditAndPin();
        _menuDelete.Click += (_, _) =>
        {
            if (Current == null) return;
            ClipboardService.Remove(Current);
            Reload();
        };

        var menu = new ContextMenu();
        menu.Items.Add(_menuCopy);
        menu.Items.Add(_menuEdit);
        menu.Items.Add(_menuPin);
        menu.Items.Add(_menuEditPin);
        menu.Items.Add(new Separator());
        menu.Items.Add(_menuDelete);
        menu.Opened += (_, _) =>
        {
            _menuOpen = true;
            bool hasItem = Current != null;
            bool isImage = Current?.IsImage == true;
            _menuCopy.IsEnabled = hasItem;
            _menuDelete.IsEnabled = hasItem;
            _menuEdit.Visibility = hasItem && !isImage ? Visibility.Visible : Visibility.Collapsed;
            _menuPin.Visibility = isImage ? Visibility.Visible : Visibility.Collapsed;
            _menuEditPin.Visibility = isImage ? Visibility.Visible : Visibility.Collapsed;
        };
        menu.Closed += (_, _) => _menuOpen = false;
        ClipList.ContextMenu = menu;
    }

    public void Open()
    {
        SearchBox.Text = "";
        Reload();

        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Top + Math.Max(40, area.Height * 0.12);

        Show();
        Activate();
        SearchBox.Focus();
        SearchBox.CaretIndex = 0;

        if (_rows.Count > 0) ClipList.SelectedIndex = 0;
    }

    public void ClosePopup() => Hide();

    private void Reload()
    {
        if (App.State == null) return;

        var query = SearchBox.Text?.Trim() ?? "";
        var items = App.State.Clips
            .Where(c => query.Length == 0 ||
                        (c.Kind == "text" && (c.Text ?? "").Contains(query, StringComparison.CurrentCultureIgnoreCase)) ||
                        (c.Kind == "image" && c.PreviewText.Contains(query)))
            .Take(40)
            .ToList();

        _rows.Clear();
        int number = 1;
        foreach (var item in items)
        {
            _rows.Add(new ClipRow { Item = item, Number = number++ });
        }

        CountText.Text = _rows.Count == 0 ? "没有记录" : $"{_rows.Count} 条 · 数字键 1-9 快选";
        if (_rows.Count > 0) ClipList.SelectedIndex = 0;
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => Reload();

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (EditOverlay.Visibility == Visibility.Visible)
        {
            if (e.Key == Key.Escape)
            {
                CloseEdit();
                e.Handled = true;
            }
            return;
        }

        if (e.Key == Key.Escape)
        {
            Hide();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            CopySelected();
            e.Handled = true;
            return;
        }

        if (SearchBox.IsKeyboardFocusWithin && e.Key is >= Key.D0 and <= Key.D9 or >= Key.NumPad0 and <= Key.NumPad9)
            return;

        if (e.Key is >= Key.D1 and <= Key.D9)
        {
            int index = (int)e.Key - (int)Key.D1;
            if (index < _rows.Count) Copy(_rows[index].Item);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Down && ClipList.SelectedIndex < _rows.Count - 1)
        {
            ClipList.SelectedIndex++;
            ClipList.ScrollIntoView(ClipList.SelectedItem);
            e.Handled = true;
        }
        else if (e.Key == Key.Up && ClipList.SelectedIndex > 0)
        {
            ClipList.SelectedIndex--;
            ClipList.ScrollIntoView(ClipList.SelectedItem);
            e.Handled = true;
        }
    }

    private void OnListDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ClipList.SelectedItem is ClipRow row) Copy(row.Item);
    }

    private void CopySelected()
    {
        if (ClipList.SelectedItem is ClipRow row) Copy(row.Item);
    }

    private void Copy(ClipItem item)
    {
        ClipboardService.CopyToClipboard(item);
        Hide();
        App.ShowToast("已复制", item.PreviewText);
    }

    private void OnListRightClick(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject) is { } row)
        {
            row.IsSelected = true;
            row.Focus();
        }
    }

    private void BeginEdit()
    {
        if (Current is not { IsImage: false } item) return;
        _editing = item;
        EditTextBox.Text = item.Text ?? "";
        EditOverlay.Visibility = Visibility.Visible;
        EditTextBox.Focus();
        EditTextBox.SelectAll();
    }

    private void OnEditSave(object sender, RoutedEventArgs e)
    {
        if (_editing != null)
        {
            _editing.Text = EditTextBox.Text;
            if (App.State != null) App.State.Notify();
        }
        CloseEdit();
        Reload();
    }

    private void OnEditCancel(object sender, RoutedEventArgs e) => CloseEdit();

    private void CloseEdit()
    {
        _editing = null;
        EditOverlay.Visibility = Visibility.Collapsed;
    }

    private void PinToDesktop()
    {
        if (Current == null) return;
        var image = ClipboardService.LoadFullImage(Current);
        if (image == null) return;
        new PinWindow(image).Show();
        Hide();
    }

    private void EditAndPin()
    {
        if (Current == null) return;
        var image = ClipboardService.LoadFullImage(Current);
        if (image == null) return;
        new ScreenshotEditorWindow(image).Show();
        Hide();
    }

    private static T FindAncestor<T>(DependencyObject current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T typed) return typed;
            current = current is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }
        return null;
    }

    private void OnDeactivated(object sender, EventArgs e)
    {
        if (_menuOpen || EditOverlay.Visibility == Visibility.Visible) return;
        Hide();
    }
}

public class ClipRow
{
    public ClipItem Item { get; set; }
    public int Number { get; set; }
}
