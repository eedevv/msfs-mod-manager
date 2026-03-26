namespace ModInstaller.Core.Models;

/// <summary>
/// Records an operation for undo/history support.
/// </summary>
public class OperationLog
{
    public int Id { get; set; }
    public required string OperationType { get; set; }  // Install, Uninstall, Enable, Disable
    public required string ModName { get; set; }
    public string? Details { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public bool CanUndo { get; set; }
    public string? UndoData { get; set; }  // JSON with info needed to reverse the operation
}
