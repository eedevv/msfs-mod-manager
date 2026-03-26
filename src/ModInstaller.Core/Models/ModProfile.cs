namespace ModInstaller.Core.Models;

/// <summary>
/// A saved mod configuration profile — snapshot of which mods are enabled/disabled.
/// </summary>
public class ModProfile
{
    public required string Name { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedDate { get; set; } = DateTime.Now;
    public DateTime? LastUsedDate { get; set; }
    public SimVersion SimVersion { get; set; } = SimVersion.Unknown;
    public List<string> EnabledMods { get; set; } = [];
    public List<string> DisabledMods { get; set; } = [];

    public int TotalModCount => EnabledMods.Count + DisabledMods.Count;
}
