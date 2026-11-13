using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModInstaller.Core.Database;
using ModInstaller.Core.Models;
using ModInstaller.Core.Services;
using ModInstaller.Core.Utils;

namespace ModInstaller.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly SimDetector _simDetector;
    private readonly ModScanner _scanner;
    private readonly FileOperations _fileOps;
    private ModDatabase? _db;
    private ModInstallerService? _installer;
    private AppSettings _settings = new();
    private bool _initializingInstallations;
    private int _scanGeneration;

    public AppSettings Settings => _settings;
    public ModDatabase? Database => _db;
    public ModInstallerService? Installer => _installer;

    [ObservableProperty] private string _statusText = "Ready";
    [ObservableProperty] private string _communityFolderPath = "";
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private string _progressText = "";
    [ObservableProperty] private int _totalModCount;
    [ObservableProperty] private int _enabledModCount;
    [ObservableProperty] private int _disabledModCount;
    [ObservableProperty] private string _totalSizeDisplay = "0 MB";
    [ObservableProperty] private SimVersion _activeSimVersion = SimVersion.MSFS2024;
    [ObservableProperty] private Mod? _selectedMod;
    [ObservableProperty] private string _currentPage = "Mods";
    [ObservableProperty] private string _windowTitle = "MSFS Mod Manager v3.0";
    [ObservableProperty] private string _typeBreakdown = "";

    public ObservableCollection<Mod> Mods { get; } = [];
    public ICollectionView ModsView { get; }
    public ObservableCollection<SimInstallation> DetectedInstallations { get; } = [];

    [ObservableProperty] private SimInstallation? _selectedInstallation;

    public MainViewModel()
    {
        _simDetector = new SimDetector();
        _scanner = new ModScanner();
        _fileOps = new FileOperations();

        ModsView = CollectionViewSource.GetDefaultView(Mods);
        ModsView.Filter = FilterMods;
    }

    partial void OnSearchTextChanged(string value)
    {
        ModsView.Refresh();
    }

    partial void OnSelectedInstallationChanged(SimInstallation? value)
    {
        if (_initializingInstallations || value == null) return;

        ActiveSimVersion = value.Version;
        _settings.ActiveSim = value.Version;
        _ = ApplyCommunityFolderAsync();
    }

    /// <summary>Apply the selected simulator's custom or detected Community folder.</summary>
    public Task ApplyCommunityFolderAsync()
    {
        var resolved = _settings.ResolveCommunityFolder(ActiveSimVersion, SelectedInstallation?.CommunityFolder);
        CommunityFolderPath = resolved;
        // Clear the old folder's mods immediately, even if the new path is unavailable.
        Mods.Clear();
        SelectedMod = null;
        UpdateStats();
        return RefreshModListAsync();
    }

    private bool FilterMods(object obj)
    {
        if (obj is not Mod mod) return false;
        if (string.IsNullOrWhiteSpace(SearchText)) return true;

        var search = SearchText.Trim();

        // Support special prefixes for targeted search
        // type:aircraft, creator:pmdg, status:enabled, status:disabled
        if (search.StartsWith("type:", StringComparison.OrdinalIgnoreCase))
        {
            var typeFilter = search[5..].Trim();
            return mod.Type.ToString().Contains(typeFilter, StringComparison.OrdinalIgnoreCase);
        }
        if (search.StartsWith("creator:", StringComparison.OrdinalIgnoreCase))
        {
            var creatorFilter = search[8..].Trim();
            return mod.Creator?.Contains(creatorFilter, StringComparison.OrdinalIgnoreCase) == true;
        }
        if (search.StartsWith("status:", StringComparison.OrdinalIgnoreCase))
        {
            var statusFilter = search[7..].Trim();
            return mod.Status.ToString().Contains(statusFilter, StringComparison.OrdinalIgnoreCase);
        }

        // General search — matches name, folder, creator, type, version
        return mod.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               mod.FolderName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               (mod.Creator?.Contains(search, StringComparison.OrdinalIgnoreCase) == true) ||
               mod.Type.ToString().Contains(search, StringComparison.OrdinalIgnoreCase) ||
               (mod.Version?.Contains(search, StringComparison.OrdinalIgnoreCase) == true);
    }

    [RelayCommand]
    public async Task InitializeAsync()
    {
        IsLoading = true;
        StatusText = "Initializing...";

        try
        {
            // Load or create settings
            await LoadSettingsAsync();

            // Initialize localization
            LocalizationService.SetLanguage(_settings.Language);

            // Initialize database
            Directory.CreateDirectory(AppSettings.SettingsDirectory);
            _db = new ModDatabase(AppSettings.DatabaseFilePath);
            _installer = new ModInstallerService(_db, _scanner, _fileOps, _settings);

            // Detect MSFS installations
            StatusText = "Detecting MSFS installations...";
            var installations = _simDetector.DetectInstallations();

            DetectedInstallations.Clear();
            foreach (var install in installations)
                DetectedInstallations.Add(install);

            _initializingInstallations = true;
            try
            {
                SelectedInstallation = installations.FirstOrDefault(i => i.Version == _settings.ActiveSim)
                                       ?? installations.FirstOrDefault(i => i.Version == SimVersion.MSFS2024)
                                       ?? installations.FirstOrDefault();
                ActiveSimVersion = SelectedInstallation?.Version ??
                    (_settings.ActiveSim == SimVersion.MSFS2020 ? SimVersion.MSFS2020 : SimVersion.MSFS2024);
                _settings.ActiveSim = ActiveSimVersion;
            }
            finally { _initializingInstallations = false; }

            await ApplyCommunityFolderAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task RefreshModListAsync()
    {
        var generation = ++_scanGeneration;
        var communityFolder = CommunityFolderPath;
        var simVersion = ActiveSimVersion;
        if (string.IsNullOrWhiteSpace(communityFolder) || !Directory.Exists(communityFolder))
        {
            Mods.Clear();
            SelectedMod = null;
            UpdateStats();
            IsLoading = false;
            StatusText = "Community folder not found — check Settings";
            return;
        }

        IsLoading = true;
        StatusText = "Scanning mods...";

        try
        {
            var progress = new Progress<string>(msg =>
            {
                if (generation == _scanGeneration) StatusText = msg;
            });
            var mods = await _scanner.ScanCommunityFolderAsync(communityFolder, simVersion, progress);

            // Also scan disabled mods
            if (Directory.Exists(AppSettings.DisabledModsDirectory))
            {
                var disabledMods = await _scanner.ScanCommunityFolderAsync(AppSettings.DisabledModsDirectory, simVersion);
                foreach (var markerMod in disabledMods)
                {
                    var mod = markerMod;
                    try
                    {
                        var target = ModInstallerService.GetDisabledContentPath(markerMod.FullPath);
                        if (target != null && Directory.Exists(target))
                        {
                            mod = await _scanner.ScanModDirectoryAsync(target, simVersion) ?? markerMod;
                            mod.FolderName = markerMod.FolderName;
                            mod.FullPath = markerMod.FullPath;
                            mod.LibraryPath = target;
                            mod.LinkType = LinkType.Junction;
                        }
                        else if (target != null)
                        {
                            mod.Health = HealthStatus.Critical;
                            mod.HealthIssues.Add($"Disabled mod target not found: {target}");
                        }
                    }
                    catch (IOException ex)
                    {
                        mod.Health = HealthStatus.Critical;
                        mod.HealthIssues.Add(ex.Message);
                    }
                    mod.Status = ModStatus.Disabled;
                    mods.Add(mod);
                }
            }

            // A newer scan may have started after the folder or simulator changed.
            if (generation != _scanGeneration) return;

            Mods.Clear();
            var sorted = mods.OrderBy(m => m.DisplayName).ToList();
            foreach (var mod in sorted)
                Mods.Add(mod);

            _db?.UpsertMods(sorted);
            LoadModNotes();
            UpdateStats();
            StatusText = $"Loaded {mods.Count} mods";
        }
        catch (Exception ex)
        {
            if (generation == _scanGeneration) StatusText = $"Error scanning: {ex.Message}";
        }
        finally
        {
            if (generation == _scanGeneration) IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task<bool> ToggleModAsync(Mod? mod)
    {
        if (mod == null || _installer == null) return false;

        try
        {
            var progress = new Progress<FileOperationProgress>(p =>
            {
                ProgressValue = p.PercentComplete;
                ProgressText = p.CurrentFile ?? "";
            });

            if (mod.Status == ModStatus.Enabled)
            {
                StatusText = $"Disabling {mod.DisplayName}...";
                await _installer.DisableModAsync(mod, progress);
            }
            else
            {
                StatusText = $"Enabling {mod.DisplayName}...";
                await _installer.EnableModAsync(mod, CommunityFolderPath, progress);
            }

            ModsView.Refresh();
            UpdateStats();
            StatusText = $"{mod.DisplayName} {(mod.Status == ModStatus.Enabled ? "enabled" : "disabled")}";
            ProgressValue = 0;
            ProgressText = "";
            return true;
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
            return false;
        }
    }

    [RelayCommand]
    public async Task<bool> InstallFromPathAsync(string path)
    {
        if (_installer == null) return false;
        if (string.IsNullOrWhiteSpace(CommunityFolderPath) || !Directory.Exists(CommunityFolderPath))
        {
            StatusText = "Community folder not found — check Settings";
            return false;
        }

        IsLoading = true;
        try
        {
            var progress = new Progress<FileOperationProgress>(p =>
            {
                ProgressValue = p.PercentComplete;
                ProgressText = p.CurrentFile ?? "";
            });

            StatusText = $"Installing from {Path.GetFileName(path)}...";
            var mod = await _installer.InstallModAsync(
                path, CommunityFolderPath, ActiveSimVersion, _settings.UseJunctions, progress);

            if (mod != null)
            {
                StatusText = $"Installed {mod.DisplayName}";
            }
            else
            {
                StatusText = "Install completed but mod could not be scanned";
            }

            // Always do a full refresh after install to ensure the mod shows up
            await RefreshModListAsync();
            return true;
        }
        catch (Exception ex)
        {
            StatusText = $"Install failed: {ex.Message}";
            return false;
        }
        finally
        {
            IsLoading = false;
            ProgressValue = 0;
            ProgressText = "";
        }
    }

    [RelayCommand]
    public void UninstallMod(Mod? mod) => TryUninstallMod(mod);

    public bool TryUninstallMod(Mod? mod)
    {
        if (mod == null || _installer == null) return false;
        try
        {
            _installer.UninstallMod(mod, createBackup: true);
            Mods.Remove(mod);
            UpdateStats();
            StatusText = $"Uninstalled {mod.DisplayName}";
            return true;
        }
        catch (Exception ex)
        {
            StatusText = $"Uninstall failed: {ex.Message}";
            return false;
        }
    }

    private void UpdateStats()
    {
        TotalModCount = Mods.Count;
        EnabledModCount = Mods.Count(m => m.Status == ModStatus.Enabled);
        DisabledModCount = Mods.Count(m => m.Status == ModStatus.Disabled);
        var totalBytes = Mods.Sum(m => m.SizeBytes);
        TotalSizeDisplay = FormatSize(totalBytes);

        // Type breakdown for status bar
        var groups = Mods.GroupBy(m => m.Type)
            .Where(g => g.Key != ModType.Unknown)
            .OrderByDescending(g => g.Count())
            .Take(4)
            .Select(g => $"{g.Count()} {g.Key}");
        TypeBreakdown = string.Join("  |  ", groups);

        // Update window title with sim info
        if (SelectedInstallation != null)
            WindowTitle = $"MSFS Mod Manager v3.0 — {SelectedInstallation.Version} ({SelectedInstallation.Source})";

        // Community folder size warning
        if (totalBytes > _settings.CommunitySizeWarningThresholdBytes)
            StatusText = $"Warning: Community folder is {FormatSize(totalBytes)} — consider disabling unused mods";
    }

    [RelayCommand]
    public void OpenCommunityFolder()
    {
        if (string.IsNullOrEmpty(CommunityFolderPath)) return;
        try
        {
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = CommunityFolderPath,
                UseShellExecute = true
            });
        }
        catch { }
    }

    public void SetModNote(Mod mod, string? note)
    {
        mod.Note = note;
        if (string.IsNullOrWhiteSpace(note))
            _settings.ModNotes.Remove(mod.FolderName);
        else
            _settings.ModNotes[mod.FolderName] = note;
        SaveSettingsSync();
    }

    public void LoadModNotes()
    {
        foreach (var mod in Mods)
        {
            if (_settings.ModNotes.TryGetValue(mod.FolderName, out var note))
                mod.Note = note;
        }
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB"
    };

    [RelayCommand]
    public async Task ExportModListAsync()
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var fileName = $"MSFS_ModList_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
        var filePath = Path.Combine(desktop, fileName);

        var lines = new List<string>
        {
            "MSFS Mod Manager v3.0 — Mod List Export",
            $"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
            $"Community Folder: {CommunityFolderPath}",
            $"Total Mods: {TotalModCount} ({EnabledModCount} enabled)",
            $"Total Size: {TotalSizeDisplay}",
            new string('═', 70),
            ""
        };

        foreach (var mod in Mods.OrderBy(m => m.DisplayName))
        {
            var status = mod.Status == ModStatus.Enabled ? "[ON] " : "[OFF]";
            lines.Add($"{status} {mod.DisplayName,-40} {mod.Type,-12} {mod.SizeDisplay,10}");
            if (mod.Creator != null)
                lines.Add($"       Creator: {mod.Creator}  Version: {mod.Version ?? "-"}");
            lines.Add("");
        }

        lines.Add(new string('═', 70));
        lines.Add($"Enabled: {EnabledModCount} | Disabled: {Mods.Count(m => m.Status == ModStatus.Disabled)} | Total: {TotalModCount}");

        await File.WriteAllLinesAsync(filePath, lines);
        StatusText = $"Exported to {fileName}";
    }

    private async Task LoadSettingsAsync()
    {
        if (File.Exists(AppSettings.SettingsFilePath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(AppSettings.SettingsFilePath);
                _settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
            catch { _settings = new AppSettings(); }
        }
    }

    public async Task SaveSettingsAsync()
    {
        Directory.CreateDirectory(AppSettings.SettingsDirectory);
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals
        };
        var json = JsonSerializer.Serialize(_settings, options);
        await File.WriteAllTextAsync(AppSettings.SettingsFilePath, json);
    }

    /// <summary>
    /// Synchronous save — use for critical settings that must persist immediately
    /// (tutorial completion, window position on close, etc.)
    /// </summary>
    public void SaveSettingsSync()
    {
        try
        {
            Directory.CreateDirectory(AppSettings.SettingsDirectory);
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals
            };
            var json = JsonSerializer.Serialize(_settings, options);
            File.WriteAllText(AppSettings.SettingsFilePath, json);
        }
        catch (Exception ex)
        {
            // Write error to a debug file so we can diagnose
            try
            {
                File.WriteAllText(
                    Path.Combine(AppSettings.SettingsDirectory, "save_error.txt"),
                    $"{DateTime.Now}: {ex}");
            }
            catch { }
        }
    }
}
