namespace ModInstaller.Core.Models;

/// <summary>
/// Represents an external mod manager detected on the system.
/// </summary>
public class ExternalManager
{
    public string Name { get; set; } = "";
    public string ConfigPath { get; set; } = "";
    public string Type { get; set; } = ""; // "AddonLinker", "AddonOrganizer", "Generic"
}

/// <summary>
/// Result of importing mods from an external manager.
/// </summary>
public class ImportResult
{
    public int ImportedCount { get; set; }
    public int SkippedCount { get; set; }
    public List<string> Errors { get; set; } = new();
}
