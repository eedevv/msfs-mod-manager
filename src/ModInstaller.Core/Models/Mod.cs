namespace ModInstaller.Core.Models;

/// <summary>
/// Represents an installed MSFS mod in the Community folder.
/// </summary>
public class Mod
{
    public int Id { get; set; }
    public required string FolderName { get; set; }
    public required string FullPath { get; set; }

    // From manifest.json (if available)
    public string? Title { get; set; }
    public string? Creator { get; set; }
    public string? Version { get; set; }
    public string? MinGameVersion { get; set; }
    public string? ContentType { get; set; }

    // Computed/detected
    public ModType Type { get; set; } = ModType.Unknown;
    public ModStatus Status { get; set; } = ModStatus.Enabled;
    public long SizeBytes { get; set; }
    public int FileCount { get; set; }
    public DateTime? InstalledDate { get; set; }
    public DateTime? LastModified { get; set; }

    // Link info
    public LinkType LinkType { get; set; } = LinkType.Copy;
    public string? LibraryPath { get; set; }

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
    public string? Note { get; set; }

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
