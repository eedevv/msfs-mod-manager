namespace ModInstaller.Core.Utils;

/// <summary>
/// Promotes a staged directory or junction while keeping the displaced item on the
/// same volume. Ambiguous interrupted swaps are reported for manual recovery.
/// Recovery folders are deliberately retained for the user; never delete link targets.
/// </summary>
public static class RecoverableSwap
{
    public const string RecoveryPrefix = ".msfsmm-recovery-";

    public static string? Commit(string stage, string destination, bool overwrite, Action? beforePromote = null)
    {
        if (!Directory.Exists(stage)) throw new DirectoryNotFoundException($"Staged content not found: {stage}");
        var parent = Path.GetDirectoryName(Path.GetFullPath(destination))
            ?? throw new IOException("Destination has no parent directory.");
        if (!Directory.Exists(parent)) throw new DirectoryNotFoundException(parent);
        if (File.Exists(destination) && !Directory.Exists(destination))
            throw new IOException($"A file exists at the destination: {destination}");
        var exists = Directory.Exists(destination) || JunctionHelper.IsJunction(destination);
        if (exists && !overwrite) throw new IOException($"Destination already exists: {destination}");
        if (!exists)
        {
            beforePromote?.Invoke();
            Directory.Move(stage, destination);
            return null;
        }

        var recovery = Path.Combine(parent, RecoveryPrefix + Guid.NewGuid().ToString("N"));
        var original = Path.Combine(recovery, "original");
        Directory.CreateDirectory(recovery);
        File.WriteAllText(Path.Combine(recovery, "destination.txt"), Path.GetFileName(destination));
        var pending = Path.Combine(recovery, "pending");
        File.WriteAllText(pending, "");
        var retired = false;
        var promoted = false;
        try
        {
            Directory.Move(destination, original);
            retired = true;
            beforePromote?.Invoke();
            Directory.Move(stage, destination);
            promoted = true;
            File.Move(pending, Path.Combine(recovery, "committed"));
            return recovery;
        }
        catch (Exception ex)
        {
            if (promoted)
                throw new IOException($"Replacement was installed, but its recovery marker could not be finalized. The previous item remains at {original}; do not repeat the operation without checking both locations.", ex);
            if (retired && !Directory.Exists(destination) && !File.Exists(destination) && !JunctionHelper.IsJunction(destination))
            {
                try { Directory.Move(original, destination); }
                catch (Exception rollbackError)
                {
                    throw new IOException($"Replacement failed and automatic rollback failed. The original remains at {original}: {rollbackError.Message}", ex);
                }
            }
            throw;
        }
    }

    /// <summary>Report ambiguous interruptions rather than restoring something the user may have removed after promotion.</summary>
    public static List<string> RecoverInterrupted(string communityFolder)
    {
        var errors = new List<string>();
        if (!Directory.Exists(communityFolder)) return errors;
        using var operation = MutationGate.Enter();
        foreach (var folder in Directory.GetDirectories(communityFolder, RecoveryPrefix + "*"))
        {
            try
            {
                if (new DirectoryInfo(folder).Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                var marker = Path.Combine(folder, "destination.txt");
                var original = Path.Combine(folder, "original");
                var pending = Path.Combine(folder, "pending");
                if (!File.Exists(marker) || !File.Exists(pending) || !Directory.Exists(original)) continue;
                var name = File.ReadAllText(marker).Trim();
                if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || Path.GetFileName(name) != name)
                    throw new IOException("Invalid recovery destination name.");
                var destination = Path.Combine(communityFolder, name);
                if (!Directory.Exists(destination) && !File.Exists(destination) && !JunctionHelper.IsJunction(destination))
                    errors.Add($"Interrupted replacement for {destination}. Original retained at {original}; inspect and restore it manually if needed.");
                else
                    File.Move(pending, Path.Combine(folder, "committed"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                errors.Add($"{folder}: {ex.Message}");
            }
        }
        return errors;
    }
}
