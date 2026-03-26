using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using ModInstaller.Core.Database;
using ModInstaller.Core.Models;
using ModInstaller.Core.Services;

namespace ModInstaller.App.Views;

public partial class ConflictPanel : UserControl
{
    private ConflictEngine? _engine;
    private ObservableCollection<Mod>? _mods;
    private Action<string, string, ToastNotification.ToastType>? _showToast;

    public ConflictPanel()
    {
        InitializeComponent();
    }

    public void Initialize(ModDatabase db, ObservableCollection<Mod> mods,
        Action<string, string, ToastNotification.ToastType> showToast)
    {
        _engine = new ConflictEngine(db);
        _mods = mods;
        _showToast = showToast;
    }

    private async void ScanConflicts_Click(object sender, RoutedEventArgs e)
    {
        if (_engine == null || _mods == null) return;

        ScanStatus.Text = "Scanning...";
        var progress = new Progress<string>(msg => ScanStatus.Text = msg);

        try
        {
            var results = await _engine.ScanForConflictsAsync(_mods.ToList(), progress);
            ConflictList.ItemsSource = results;
            ScanStatus.Text = $"Found {results.Count} conflict(s)";

            if (results.Count == 0)
                _showToast?.Invoke("No Conflicts", "All mods are compatible", ToastNotification.ToastType.Success);
            else
                _showToast?.Invoke("Conflicts Found", $"{results.Count} conflict(s) detected", ToastNotification.ToastType.Warning);
        }
        catch (Exception ex)
        {
            ScanStatus.Text = $"Error: {ex.Message}";
        }
    }
}
