namespace ModInstaller.Core.Models;

/// <summary>
/// Represents a detected MSFS installation (2020 or 2024).
/// </summary>
public class SimInstallation
{
    public required SimVersion Version { get; set; }
    public required string CommunityFolder { get; set; }
    public InstallSource Source { get; set; } = InstallSource.Unknown;
    public string? InstallPath { get; set; }
    public bool IsValid => Directory.Exists(CommunityFolder);
}
