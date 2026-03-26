using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ModInstaller.App.Views;

public partial class ToastNotification : UserControl
{
    private readonly Storyboard _showStoryboard;
    private readonly Storyboard _hideStoryboard;

    public enum ToastType { Success, Error, Warning, Info }

    public ToastNotification()
    {
        InitializeComponent();

        _showStoryboard = CreateShowAnimation();
        _hideStoryboard = CreateHideAnimation();
    }

    public void Show(string title, string message, ToastType type = ToastType.Success, int autoHideMs = 4000)
    {
        TitleText.Text = title;
        MessageText.Text = message;
        MessageText.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;

        // Set icon and color based on type
        var (icon, color) = type switch
        {
            ToastType.Success => ("\u2714", Color.FromRgb(63, 185, 80)),    // green check
            ToastType.Error => ("\u2716", Color.FromRgb(248, 81, 73)),      // red X
            ToastType.Warning => ("\u26A0", Color.FromRgb(210, 153, 34)),   // yellow triangle
            ToastType.Info => ("\u2139", Color.FromRgb(88, 166, 255)),      // blue i
            _ => ("\u2714", Color.FromRgb(63, 185, 80))
        };

        IconText.Text = icon;
        IconBorder.Background = new SolidColorBrush(Color.FromArgb(40, color.R, color.G, color.B));
        IconText.Foreground = new SolidColorBrush(color);

        // Accent border glow
        ToastBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(60, color.R, color.G, color.B));

        // Play show animation
        _showStoryboard.Begin(this);

        // Auto-hide after delay
        if (autoHideMs > 0)
        {
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(autoHideMs)
            };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                Hide();
            };
            timer.Start();
        }
    }

    public void Hide()
    {
        _hideStoryboard.Begin(this);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Hide();

    private Storyboard CreateShowAnimation()
    {
        var sb = new Storyboard();

        // Slide in from right
        var slideIn = new DoubleAnimation
        {
            From = 380, To = 0,
            Duration = TimeSpan.FromMilliseconds(400),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(slideIn, this);
        Storyboard.SetTargetProperty(slideIn,
            new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[0].(TranslateTransform.X)"));

        // Fade in
        var fadeIn = new DoubleAnimation
        {
            From = 0, To = 1,
            Duration = TimeSpan.FromMilliseconds(300)
        };
        Storyboard.SetTarget(fadeIn, this);
        Storyboard.SetTargetProperty(fadeIn, new PropertyPath("Opacity"));

        sb.Children.Add(slideIn);
        sb.Children.Add(fadeIn);
        return sb;
    }

    private Storyboard CreateHideAnimation()
    {
        var sb = new Storyboard();

        // Slide out to right
        var slideOut = new DoubleAnimation
        {
            To = 380,
            Duration = TimeSpan.FromMilliseconds(300),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        Storyboard.SetTarget(slideOut, this);
        Storyboard.SetTargetProperty(slideOut,
            new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[0].(TranslateTransform.X)"));

        // Fade out
        var fadeOut = new DoubleAnimation
        {
            To = 0,
            Duration = TimeSpan.FromMilliseconds(250)
        };
        Storyboard.SetTarget(fadeOut, this);
        Storyboard.SetTargetProperty(fadeOut, new PropertyPath("Opacity"));

        sb.Children.Add(slideOut);
        sb.Children.Add(fadeOut);
        return sb;
    }
}
