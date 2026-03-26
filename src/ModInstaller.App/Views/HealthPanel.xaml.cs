using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using ModInstaller.Core.Models;
using ModInstaller.Core.Services;

namespace ModInstaller.App.Views;

public partial class HealthPanel : UserControl
{
    private readonly HealthChecker _checker = new();
    private ObservableCollection<Mod>? _mods;
    private Action<string, string, ToastNotification.ToastType>? _showToast;

    public HealthPanel()
    {
        InitializeComponent();
    }

    public void Initialize(ObservableCollection<Mod> mods,
        Action<string, string, ToastNotification.ToastType> showToast)
    {
        _mods = mods;
        _showToast = showToast;
    }

    private async void RunHealthCheck_Click(object sender, RoutedEventArgs e)
    {
        if (_mods == null || _mods.Count == 0) return;

        var progress = new Progress<string>(_ => { });

        try
        {
            var reports = await _checker.CheckAllModsAsync(_mods.ToList(), progress);
            ReportList.ItemsSource = reports;

            int healthy = reports.Count(r => r.Status == HealthStatus.Healthy);
            int warnings = reports.Count(r => r.Status == HealthStatus.Warning);
            int critical = reports.Count(r => r.Status == HealthStatus.Critical);

            HealthyCount.Text = healthy.ToString();
            WarningCount.Text = warnings.ToString();
            CriticalCount.Text = critical.ToString();

            if (critical > 0)
                _showToast?.Invoke("Health Check", $"{critical} critical issue(s) found", ToastNotification.ToastType.Error);
            else if (warnings > 0)
                _showToast?.Invoke("Health Check", $"{warnings} warning(s) found", ToastNotification.ToastType.Warning);
            else
                _showToast?.Invoke("All Healthy", $"All {healthy} mods passed health check", ToastNotification.ToastType.Success);
        }
        catch (Exception ex)
        {
            _showToast?.Invoke("Error", ex.Message, ToastNotification.ToastType.Error);
        }
    }
}
