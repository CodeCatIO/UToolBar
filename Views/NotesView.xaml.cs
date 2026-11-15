using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using EdgeBar.Models;

namespace EdgeBar.Views;

public partial class NotesView : UserControl
{
    public NotesView()
    {
        InitializeComponent();

        if (App.State != null)
        {
            NoteList.ItemsSource = App.State.Notes;
            App.State.Notes.CollectionChanged += OnNotesChanged;
            foreach (var note in App.State.Notes) note.PropertyChanged += OnNotePropertyChanged;
        }

        Loaded += (_, _) => RefreshMeta();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) RefreshMeta();
        };
    }

    private void OnNotesChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (NoteData note in e.NewItems) note.PropertyChanged += OnNotePropertyChanged;
        }
        if (e.OldItems != null)
        {
            foreach (NoteData note in e.OldItems) note.PropertyChanged -= OnNotePropertyChanged;
        }
        RefreshMeta();
    }

    private void OnNotePropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e) => RefreshMeta();

    private void RefreshMeta()
    {
        if (App.State == null) return;
        int count = App.State.Notes.Count;
        CountText.Text = $"{count} / 12 张 · Alt+N 随处新建";
        EmptyHint.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnNewNote(object sender, RoutedEventArgs e) => App.CreateNote();

    private void OnNoteClick(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<Button>(e.OriginalSource as DependencyObject) != null) return;
        if (sender is FrameworkElement { DataContext: NoteData note })
        {
            App.CreateNote(note);
        }
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement { DataContext: NoteData note })
        {
            App.DeleteNote(note);
        }
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
}
