namespace ModInstaller.Core.Utils;

/// <summary>
/// Creates and manages NTFS directory junctions (reparse points).
/// Junctions don't require elevated privileges unlike symbolic links.
/// Uses .NET native APIs where possible, falls back to cmd.exe only when necessary.
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

        if (Directory.Exists(junctionPath) || IsJunction(junctionPath))
            throw new IOException($"Junction path already exists: {junctionPath}");

        if (!Directory.Exists(targetPath))
            throw new DirectoryNotFoundException($"Target directory does not exist: {targetPath}");

        Directory.CreateDirectory(junctionPath);

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c rmdir \"{EscapeCmd(junctionPath)}\" && mklink /J \"{EscapeCmd(junctionPath)}\" \"{EscapeCmd(targetPath)}\"",
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

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c rmdir \"{EscapeCmd(junctionPath)}\"",
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
            throw new TimeoutException("Junction removal timed out");
        }

        if (process.ExitCode != 0)
        {
            var error = process.StandardError.ReadToEnd();
            throw new IOException($"Failed to remove junction: {error}");
        }
    }

    /// <summary>
    /// Checks if a path is a directory junction (reparse point).
    /// </summary>
    public static bool IsJunction(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return false;
            return new DirectoryInfo(path).Attributes.HasFlag(FileAttributes.ReparsePoint);
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

            // Fallback: parse dir output
            var parentDir = Path.GetDirectoryName(junctionPath);
            var folderName = Path.GetFileName(junctionPath);
            if (parentDir == null || folderName == null) return null;

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c dir \"{EscapeCmd(parentDir)}\" /AL",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(psi);
            if (process == null) return null;

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);

            // Find line containing our folder name and extract [target] bracket content
            foreach (var line in output.Split('\n'))
            {
                if (!line.Contains(folderName, StringComparison.OrdinalIgnoreCase)) continue;
                var bracketStart = line.IndexOf('[');
                var bracketEnd = line.IndexOf(']');
                if (bracketStart >= 0 && bracketEnd > bracketStart)
                    return line[(bracketStart + 1)..bracketEnd];
            }
        }
        catch { }

        return null;
    }

    /// <summary>
    /// Escapes special cmd.exe characters in paths to prevent injection.
    /// </summary>
    private static string EscapeCmd(string path)
    {
        // Remove any existing quotes and dangerous characters
        return path
            .Replace("\"", "")
            .Replace("&", "^&")
            .Replace("|", "^|")
            .Replace(">", "^>")
            .Replace("<", "^<")
            .Replace("^", "^^");
    }
}
