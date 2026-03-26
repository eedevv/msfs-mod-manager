namespace ModInstaller.Core.Models;

/// <summary>
/// Describes the change state of an installed mod compared to a cached baseline.
/// </summary>
public class ModUpdateInfo
{
    public string FolderName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? CurrentVersion { get; set; }
    public string? PreviousVersion { get; set; }
    public bool HasChanged { get; set; }
    public DateTime LastChecked { get; set; }
    public long SizeDelta { get; set; }
}
