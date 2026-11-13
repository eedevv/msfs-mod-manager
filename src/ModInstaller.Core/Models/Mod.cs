using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ModInstaller.Core.Models;

/// <summary>
/// Represents an installed MSFS mod in the Community folder.
/// </summary>
public class Mod : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    private string _fullPath = "";
    private ModStatus _status = ModStatus.Enabled;
    private LinkType _linkType = LinkType.Copy;
    private string? _libraryPath;
    private string? _note;
    public int Id { get; set; }
    public required string FolderName { get; set; }
    public required string FullPath { get => _fullPath; set { if (_fullPath != value) { _fullPath = value; Notify(); } } }

    // From manifest.json (if available)
    public string? Title { get; set; }
    public string? Creator { get; set; }
    public string? Version { get; set; }
    public string? MinGameVersion { get; set; }
    public string? ContentType { get; set; }

    // Computed/detected
    public ModType Type { get; set; } = ModType.Unknown;
    public ModStatus Status { get => _status; set { if (_status != value) { _status = value; Notify(); } } }
    public long SizeBytes { get; set; }
    public int FileCount { get; set; }
    public DateTime? InstalledDate { get; set; }
    public DateTime? LastModified { get; set; }

    // Link info
    public LinkType LinkType { get => _linkType; set { if (_linkType != value) { _linkType = value; Notify(); } } }
    public string? LibraryPath { get => _libraryPath; set { if (_libraryPath != value) { _libraryPath = value; Notify(); } } }

    // Health
    public bool HasManifest { get; set; }
    public bool HasLayout { get; set; }
    public HealthStatus Health { get; set; } = HealthStatus.Healthy;
    public List<string> HealthIssues { get; set; } = [];

    // Dependencies
    public List<string> Dependencies { get; set; } = [];

    // Which sim this mod is associated with
    public SimVersion SimVersion { get; set; } = SimVersion.Unknown;

    // User-added note/tag
    public string? Note { get => _note; set { if (_note != value) { _note = value; Notify(); } } }

    public string DisplayName => Title ?? FolderName;
    public string SizeDisplay => FormatSize(SizeBytes);
    public string InstalledDateDisplay => InstalledDate?.ToString("MMM dd, yyyy") ?? LastModified?.ToString("MMM dd, yyyy") ?? "-";

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB"
    };
}
