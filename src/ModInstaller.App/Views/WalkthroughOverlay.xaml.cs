using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ModInstaller.App.Views;

public partial class WalkthroughOverlay : UserControl
{
    private int _currentStep;
    private Action? _onComplete;

    private static readonly (string Title, string Description, string? Tip, string Icon)[] Steps =
    [
        (
            "Welcome to MSFS Mod Manager",
            "The ultimate tool for managing your Microsoft Flight Simulator mods.\n\nLet's take a quick tour of what you can do.",
            null,
            "app" // uses app icon
        ),
        (
            "Install Mods Easily",
            "Drag and drop ZIP, 7z, or RAR files onto the window to install mods instantly.\n\nOr click the Install button in the top-right corner to browse for files.",
            "Tip: You can drop multiple files at once for batch installation.",
            "install"
        ),
        (
            "Enable & Disable Mods",
            "Double-click any mod in the list to quickly toggle it on or off.\n\nDisabled mods are moved to a staging area — your Community folder stays clean.",
            "Tip: Use junctions (enabled by default in Settings) for instant toggling with zero file copying.",
            "toggle"
        ),
        (
            "Right-Click for More",
            "Right-click any mod (or select multiple with Ctrl/Shift+click) to access batch operations:\n\n- Enable/Disable all selected\n- Open mod folder\n- Uninstall with backup",
            "Tip: Ctrl+A selects all mods.",
            "menu"
        ),
        (
            "Search & Filter",
            "Use the search bar to find mods by name, creator, or type.\n\nSpecial filters available:",
            "type:aircraft    creator:pmdg    status:disabled",
            "search"
        ),
        (
            "Profiles & Presets",
            "Save your current mod configuration as a named profile.\n\nSwitch between profiles to instantly enable/disable the right mods for different flights.",
            "Example: \"VFR Europe\", \"Airliner\", \"Bush Flying\"",
            "profile"
        ),
        (
            "Stay Healthy",
            "Use the Health Check page to scan all mods for issues — missing manifests, invalid JSON, suspicious files.\n\nThe Conflict Scanner finds mods that overwrite each other's files.",
            null,
            "health"
        ),
        (
            "Keyboard Shortcuts",
            "Work faster with shortcuts:\n\nCtrl+I — Install mods\nCtrl+R — Refresh mod list\nCtrl+E — Export mod list\nCtrl+1-6 — Switch pages\nCtrl+A — Select all",
            null,
            "keyboard"
        ),
        (
            "You're All Set!",
            "That's everything you need to know.\n\nYour Community folder has been detected and your mods are ready to manage. Enjoy your flights!",
            "This tutorial won't show again. You can always find help in Settings.",
            "app"
        )
    ];

    public WalkthroughOverlay()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Shows the walkthrough. Call onComplete when finished.
    /// </summary>
    public void Show(Action onComplete)
    {
        _onComplete = onComplete;
        _currentStep = 0;
        Visibility = Visibility.Visible;
        RenderStep();
    }

    private void RenderStep()
    {
        var step = Steps[_currentStep];

        StepTitle.Text = step.Title;
        StepDescription.Text = step.Description;
        StepCounter.Text = $"{_currentStep + 1} / {Steps.Length}";

        // Tip box
        if (step.Tip != null)
        {
            StepTipBox.Visibility = Visibility.Visible;
            StepTip.Text = step.Tip;
        }
        else
        {
            StepTipBox.Visibility = Visibility.Collapsed;
        }

        // Next button text
        NextButtonText.Text = _currentStep == Steps.Length - 1 ? "Get Started" : "Next";

        // Skip button visibility
        SkipButton.Visibility = _currentStep == Steps.Length - 1 ? Visibility.Collapsed : Visibility.Visible;

        // Step dots
        StepDots.Children.Clear();
        for (int i = 0; i < Steps.Length; i++)
        {
            var dot = new Ellipse
            {
                Width = i == _currentStep ? 8 : 6,
                Height = i == _currentStep ? 8 : 6,
                Margin = new Thickness(3, 0, 3, 0),
                Fill = new SolidColorBrush(i == _currentStep
                    ? Color.FromRgb(88, 166, 255)       // accent
                    : i < _currentStep
                        ? Color.FromArgb(80, 88, 166, 255) // done
                        : Color.FromArgb(40, 255, 255, 255)) // pending
            };
            StepDots.Children.Add(dot);
        }
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_currentStep < Steps.Length - 1)
        {
            _currentStep++;
            RenderStep();
        }
        else
        {
            Complete();
        }
    }

    private void Skip_Click(object sender, RoutedEventArgs e) => Complete();

    private void Complete()
    {
        try
        {
            Visibility = Visibility.Collapsed;
            _onComplete?.Invoke();
        }
        catch
        {
            // Ensure overlay is hidden even if callback fails
            Visibility = Visibility.Collapsed;
        }
    }
}
