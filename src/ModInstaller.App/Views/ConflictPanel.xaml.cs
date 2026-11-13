using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
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
    private Func<bool>? _isBusy;
    private int _generation;
    private CancellationTokenSource? _scanCancellation;
    private readonly HashSet<Mod> _observedMods = [];

    public ConflictPanel()
    {
        InitializeComponent();
    }

    public void Initialize(ModDatabase db, ObservableCollection<Mod> mods,
        Action<string, string, ToastNotification.ToastType> showToast, Func<bool> isBusy)
    {
        _engine = new ConflictEngine(db);
        _mods = mods;
        _showToast = showToast;
        _isBusy = isBusy;
        _mods.CollectionChanged += ModsChanged;
        foreach (var mod in _mods) Subscribe(mod);
    }

    private void Subscribe(Mod mod)
    {
        if (_observedMods.Add(mod)) mod.PropertyChanged += ModChanged;
    }

    private void Unsubscribe(Mod mod)
    {
        if (_observedMods.Remove(mod)) mod.PropertyChanged -= ModChanged;
    }

    private void ModsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var mod in _observedMods.ToArray()) Unsubscribe(mod);
            if (_mods != null) foreach (var mod in _mods) Subscribe(mod);
        }
        else
        {
            if (e.OldItems != null) foreach (Mod mod in e.OldItems) Unsubscribe(mod);
            if (e.NewItems != null) foreach (Mod mod in e.NewItems) Subscribe(mod);
        }
        InvalidateResults();
    }

    private void ModChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Mod.Status) or nameof(Mod.FullPath)) InvalidateResults();
    }

    private void InvalidateResults()
    {
        _generation++;
        _scanCancellation?.Cancel();
        ConflictList.ItemsSource = null;
        ScanStatus.Text = "Mod list changed; run a new scan.";
    }

    private async void ScanConflicts_Click(object sender, RoutedEventArgs e)
    {
        if (_engine == null || _mods == null || _scanCancellation != null) return;
        if (_isBusy?.Invoke() == true)
        {
            ScanStatus.Text = "Wait for the current operation to finish.";
            return;
        }
        var generation = _generation;
        using var cancellation = new CancellationTokenSource();
        _scanCancellation = cancellation;
        ScanButton.IsEnabled = false;
        ScanStatus.Text = "Scanning...";
        ConflictList.ItemsSource = null;
        bool running = true;
        var progress = new Progress<string>(msg => { if (running && generation == _generation) ScanStatus.Text = msg; });

        try
        {
            var results = await _engine.ScanForConflictsAsync(_mods.Where(m => m.Status == ModStatus.Enabled).ToList(), progress, cancellation.Token);
            if (generation != _generation) return;
            running = false;
            ConflictList.ItemsSource = results;
            ScanStatus.Text = $"Found {results.Count} conflict(s)";

            if (results.Count == 0)
                _showToast?.Invoke("No Conflicts", "No matching paths found; this does not guarantee compatibility.", ToastNotification.ToastType.Success);
            else
                _showToast?.Invoke("Conflicts Found", $"{results.Count} conflict(s) detected", ToastNotification.ToastType.Warning);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // The result has already been invalidated when the mod list changed.
        }
        catch (Exception ex)
        {
            running = false;
            if (generation == _generation) ScanStatus.Text = $"Error: {ex.Message}";
        }
        finally
        {
            running = false;
            _scanCancellation = null;
            ScanButton.IsEnabled = true;
        }
    }
}
