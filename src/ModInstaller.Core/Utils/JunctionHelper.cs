namespace ModInstaller.Core.Utils;

/// <summary>
/// Creates and manages NTFS directory junctions (reparse points).
/// Junctions don't require elevated privileges unlike symbolic links.
/// Uses .NET native APIs where possible and cmd.exe for junction creation.
/// </summary>
public static class JunctionHelper
{
    /// <summary>
    /// Creates a directory junction. The junction at junctionPath will contain files from targetPath.
    /// </summary>
    public static void CreateJunction(string junctionPath, string targetPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(junctionPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        // Sanitize paths — prevent injection
        junctionPath = Path.GetFullPath(junctionPath);
        targetPath = Path.GetFullPath(targetPath);

        // cmd expands percent variables even inside quotes; reject rather than risk
        // creating or removing a link at an unintended expanded path.
        if (junctionPath.Contains('%') || targetPath.Contains('%'))
            throw new IOException("Junction creation does not support percent characters in paths. Use copy mode for this path.");

        if (Directory.Exists(junctionPath) || IsJunction(junctionPath))
            throw new IOException($"Junction path already exists: {junctionPath}");

        if (!Directory.Exists(targetPath))
            throw new DirectoryNotFoundException($"Target directory does not exist: {targetPath}");

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/d /v:off /c mklink /J \"{junctionPath}\" \"{targetPath}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = System.Diagnostics.Process.Start(psi)
            ?? throw new IOException("Failed to start cmd.exe");

        if (!process.WaitForExit(30000))
        {
            try { process.Kill(); } catch { }
            throw new TimeoutException("Junction creation timed out");
        }

        if (process.ExitCode != 0)
        {
            var error = process.StandardError.ReadToEnd();
            throw new IOException($"Failed to create junction: {error}");
        }
    }

    /// <summary>
    /// Removes a directory junction without deleting the target contents.
    /// </summary>
    public static void RemoveJunction(string junctionPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(junctionPath);
        junctionPath = Path.GetFullPath(junctionPath);

        if (!IsJunction(junctionPath))
            throw new IOException($"Path is not a junction: {junctionPath}");

        // Non-recursive removal removes the link itself, not its target, and
        // avoids cmd.exe expansion for existing paths containing percent signs.
        Directory.Delete(junctionPath, recursive: false);
    }

    /// <summary>
    /// Checks if a path is a directory junction (reparse point).
    /// </summary>
    public static bool IsJunction(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            var attributes = File.GetAttributes(path);
            return attributes.HasFlag(FileAttributes.ReparsePoint) && attributes.HasFlag(FileAttributes.Directory);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets the target path of a junction. Returns null if not a junction.
    /// </summary>
    public static string? GetJunctionTarget(string junctionPath)
    {
        if (!IsJunction(junctionPath)) return null;

        try
        {
            // .NET 6+ can resolve link targets natively
            var target = new DirectoryInfo(junctionPath).ResolveLinkTarget(returnFinalTarget: true);
            if (target != null) return target.FullName;

        }
        catch { }

        return null;
    }

}
