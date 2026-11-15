using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace EdgeBar.Views;

public partial class ToastWindow : Window
{
    private readonly DispatcherTimer _timer;

    public ToastWindow()
    {
        InitializeComponent();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _timer.Tick += (_, _) => HideToast();
        MouseLeftButtonDown += (_, _) => HideToast();
    }

    public void ShowToast(string title, string message)
    {
        _timer.Stop();
        TitleText.Text = title;
        MessageText.Text = message;
        Opacity = 0;

        if (!IsVisible) Show();
        UpdateLayout();

        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth - 4;
        Top = area.Bottom - ActualHeight - 4;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
        Slide.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
            new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });

        _timer.Start();
    }

    private void HideToast()
    {
        _timer.Stop();
        var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(180));
        fade.Completed += (_, _) =>
        {
            BeginAnimation(OpacityProperty, null);
            Hide();
        };
        BeginAnimation(OpacityProperty, fade);
    }

    private void OnDismiss(object sender, MouseButtonEventArgs e) => HideToast();
}
