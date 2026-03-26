namespace ModInstaller.Core.Models;

public class ConflictResult
{
    public required string ModA { get; set; }
    public required string ModB { get; set; }
    public List<string> ConflictingFiles { get; set; } = [];
    public string Description => $"{ModA} conflicts with {ModB}: {ConflictingFiles.Count} shared file(s)";
}
