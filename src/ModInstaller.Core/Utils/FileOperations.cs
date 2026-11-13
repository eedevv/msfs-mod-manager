using System.IO.Compression;

namespace ModInstaller.Core.Utils;

/// <summary>
/// Safe file copy/move operations with progress reporting.
/// </summary>
public class FileOperations
{
    private const int ProgressThrottleMs = 100; // Don't flood UI thread

    public Task CopyDirectoryAsync(
        string sourceDir, string destDir,
        IProgress<FileOperationProgress>? progress = null,
        CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            var sourceInfo = new DirectoryInfo(sourceDir);
            if (!sourceInfo.Exists)
                throw new DirectoryNotFoundException($"Source not found: {sourceDir}");

            RejectNestedLinks(sourceDir, ct);
            var allFiles = sourceInfo.GetFiles("*", SearchOption.AllDirectories);
            long totalBytes = allFiles.Sum(f => f.Length);
            long copiedBytes = 0;
            int copiedFiles = 0;
            var lastReport = DateTime.MinValue;

            Directory.CreateDirectory(destDir);

            foreach (var file in allFiles)
            {
                ct.ThrowIfCancellationRequested();

                var relativePath = Path.GetRelativePath(sourceDir, file.FullName);
                var destFile = Path.Combine(destDir, relativePath);

                // Prevent path traversal
                if (!IsPathWithinDirectory(destDir, destFile))
                    throw new IOException($"Path traversal detected: {relativePath}");

                var destFileDir = Path.GetDirectoryName(destFile);
                if (destFileDir != null) Directory.CreateDirectory(destFileDir);

                file.CopyTo(destFile, overwrite: true);
                copiedBytes += file.Length;
                copiedFiles++;

                // Throttle progress reports to avoid UI thread starvation
                if (progress != null && (DateTime.UtcNow - lastReport).TotalMilliseconds >= ProgressThrottleMs)
                {
                    lastReport = DateTime.UtcNow;
                    progress.Report(new FileOperationProgress
                    {
                        CurrentFile = relativePath,
                        TotalFiles = allFiles.Length,
                        ProcessedFiles = copiedFiles,
                        TotalBytes = totalBytes,
                        ProcessedBytes = copiedBytes,
                        PercentComplete = totalBytes > 0 ? (double)copiedBytes / totalBytes * 100 : 100
                    });
                }
            }

            // Final report
            progress?.Report(new FileOperationProgress
            {
                TotalFiles = allFiles.Length, ProcessedFiles = copiedFiles,
                TotalBytes = totalBytes, ProcessedBytes = copiedBytes, PercentComplete = 100
            });
        }, ct);
    }

    /// <summary>Reject links within content rather than traversing them. The root may itself be an intentional mod junction.</summary>
    public static void RejectNestedLinks(string root, CancellationToken ct = default)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        var options = new EnumerationOptions { RecurseSubdirectories = false, AttributesToSkip = 0, IgnoreInaccessible = false };
        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            foreach (var entry in Directory.EnumerateFileSystemEntries(pending.Pop(), "*", options))
            {
                ct.ThrowIfCancellationRequested();
                var attributes = File.GetAttributes(entry);
                if (attributes.HasFlag(FileAttributes.ReparsePoint))
                    throw new IOException($"Nested links in mod or backup content are not supported: {entry}");
                if (attributes.HasFlag(FileAttributes.Directory)) pending.Push(entry);
            }
        }
    }

    public async Task MoveDirectoryAsync(
        string sourceDir, string destDir,
        IProgress<FileOperationProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (!Directory.Exists(sourceDir))
            throw new DirectoryNotFoundException($"Source not found: {sourceDir}");

        if (Directory.Exists(destDir) || File.Exists(destDir))
            throw new IOException($"Destination already exists: {destDir}");

        // Same drive: try fast rename first
        if (Path.GetPathRoot(sourceDir)?.Equals(Path.GetPathRoot(destDir), StringComparison.OrdinalIgnoreCase) == true)
        {
            try
            {
                Directory.Move(sourceDir, destDir);
                progress?.Report(new FileOperationProgress { PercentComplete = 100, ProcessedFiles = 1, TotalFiles = 1 });
                return;
            }
            catch (IOException) { /* Fall through to copy+delete */ }
        }

        await CopyDirectoryAsync(sourceDir, destDir, progress, ct);
        SafeDeleteDirectory(sourceDir);
    }

    /// <summary>
    /// Gets size and file count in one pass (avoids double enumeration).
    /// </summary>
    public static (long SizeBytes, int FileCount) GetDirectoryStats(string path)
    {
        if (!Directory.Exists(path)) throw new DirectoryNotFoundException($"Directory not found: {path}");
        long size = 0;
        int count = 0;
        foreach (var file in new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories))
        {
            size += file.Length;
            count++;
        }
        return (size, count);
    }

    public static long GetDirectorySize(string path) => GetDirectoryStats(path).SizeBytes;
    public static int GetFileCount(string path) => GetDirectoryStats(path).FileCount;

    public static void SafeDeleteDirectory(string path)
    {
        if (!Directory.Exists(path)) return;
        try
        {
            DeleteDirectoryWithoutFollowingLinks(path);
        }
        catch (Exception ex)
        {
            throw new IOException($"Failed to delete directory {Path.GetFileName(path)}: {ex.Message}", ex);
        }
    }

    private static void DeleteDirectoryWithoutFollowingLinks(string path)
    {
        var attributes = File.GetAttributes(path);
        if (attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            Directory.Delete(path, recursive: false);
            return;
        }

        var options = new EnumerationOptions { RecurseSubdirectories = false, AttributesToSkip = 0, IgnoreInaccessible = false };
        foreach (var entry in Directory.EnumerateFileSystemEntries(path, "*", options))
        {
            var entryAttributes = File.GetAttributes(entry);
            if (entryAttributes.HasFlag(FileAttributes.Directory))
                DeleteDirectoryWithoutFollowingLinks(entry);
            else if (entryAttributes.HasFlag(FileAttributes.ReparsePoint))
                File.Delete(entry);
            else
            {
                File.SetAttributes(entry, FileAttributes.Normal);
                File.Delete(entry);
            }
        }
        File.SetAttributes(path, FileAttributes.Normal);
        Directory.Delete(path, recursive: false);
    }

    /// <summary>Checks containment on a path-component boundary, not a string prefix.</summary>
    public static bool IsPathWithinDirectory(string directory, string path)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        var fullPath = Path.GetFullPath(path);
        var prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        return fullPath.Equals(root, StringComparison.OrdinalIgnoreCase) ||
            fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Reject paths and manager-reserved names where one package folder name is required.</summary>
    public static void ValidatePackageName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(['/', '\\']) >= 0 ||
            IsUnsafeZipComponent(name) || name.StartsWith(".msfsmm-", StringComparison.OrdinalIgnoreCase))
            throw new IOException($"Invalid or reserved package name: {name}");
    }

    private static bool IsUnsafeZipComponent(string part)
    {
        if (part is "." or ".." || part.EndsWith(' ') || part.EndsWith('.') ||
            part.Any(c => c < 32 || "<>:\"|?*".Contains(c))) return true;
        var stem = part.Split('.')[0];
        if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase) || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("CONIN$", StringComparison.OrdinalIgnoreCase) || stem.Equals("CONOUT$", StringComparison.OrdinalIgnoreCase)) return true;
        return stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
            stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && "123456789¹²³".Contains(stem[3]);
    }

    public async Task ExtractZipAsync(
        string zipPath, string destDir,
        IProgress<FileOperationProgress>? progress = null,
        CancellationToken ct = default)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var totalEntries = archive.Entries.Count;
        const int maxEntries = 1_000_000;
        const long maxUncompressedBytes = 100L * 1024 * 1024 * 1024;
        if (totalEntries > maxEntries)
            throw new IOException("Archive contains too many entries.");
        long totalBytes = 0;
        var filesSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directoriesSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Validate the entire archive before writing any file.
        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();
            var parts = entry.FullName.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || Path.IsPathRooted(entry.FullName) || parts.Any(IsUnsafeZipComponent))
                throw new IOException($"Unsafe archive path: {entry.FullName}");
            var destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(destDir, entry.FullName)));
            if (!IsPathWithinDirectory(destDir, destination))
                throw new IOException($"Zip slip detected: {entry.FullName}");
            var directoryEntry = entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\');
            if (directoryEntry)
            {
                if (filesSeen.Contains(destination)) throw new IOException($"Archive file/directory collision: {entry.FullName}");
                directoriesSeen.Add(destination);
            }
            else if (directoriesSeen.Contains(destination) || !filesSeen.Add(destination))
                throw new IOException($"Duplicate archive destination: {entry.FullName}");
            var unixType = (entry.ExternalAttributes >> 16) & 0xF000;
            if (unixType == 0xA000 || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                throw new IOException($"Archive links are not supported: {entry.FullName}");
            if (entry.Length > maxUncompressedBytes - totalBytes)
                throw new IOException("Archive exceeds the 100 GiB uncompressed size limit.");
            totalBytes += entry.Length;
        }
        // A file must not also be the parent of another entry (including implicit directories).
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destDir));
        foreach (var destination in filesSeen.Concat(directoriesSeen))
        {
            ct.ThrowIfCancellationRequested();
            var parent = Path.GetDirectoryName(destination);
            while (parent != null && !parent.Equals(root, StringComparison.OrdinalIgnoreCase) && IsPathWithinDirectory(root, parent))
            {
                if (filesSeen.Contains(parent)) throw new IOException($"Archive file/directory collision: {parent}");
                parent = Path.GetDirectoryName(parent);
            }
        }
        long extractedBytes = 0;
        int extractedFiles = 0;
        var lastReport = DateTime.MinValue;

        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();

            var destPath = Path.GetFullPath(Path.Combine(destDir, entry.FullName));

            // Prevent zip slip attack (path traversal via archive entry names)
            if (!IsPathWithinDirectory(destDir, destPath))
                throw new IOException($"Zip slip detected: {entry.FullName}");

            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
            {
                Directory.CreateDirectory(destPath);
                continue;
            }

            var entryDir = Path.GetDirectoryName(destPath);
            if (entryDir != null) Directory.CreateDirectory(entryDir);

            long entryBytes = 0;
            using (var entryStream = entry.Open())
            using (var fileStream = new FileStream(destPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                int count;
                while ((count = await entryStream.ReadAsync(buffer, ct)) != 0)
                {
                    if (count > entry.Length - entryBytes || count > maxUncompressedBytes - extractedBytes)
                        throw new IOException($"Archive entry exceeds its declared size or the extraction limit: {entry.FullName}");
                    await fileStream.WriteAsync(buffer.AsMemory(0, count), ct);
                    entryBytes += count;
                    extractedBytes += count;
                }
            }
            if (entryBytes != entry.Length)
                throw new IOException($"Archive entry size does not match its declaration: {entry.FullName}");
            extractedFiles++;

            if (progress != null && (DateTime.UtcNow - lastReport).TotalMilliseconds >= ProgressThrottleMs)
            {
                lastReport = DateTime.UtcNow;
                progress.Report(new FileOperationProgress
                {
                    CurrentFile = entry.FullName, TotalFiles = totalEntries,
                    ProcessedFiles = extractedFiles, TotalBytes = totalBytes,
                    ProcessedBytes = extractedBytes,
                    PercentComplete = totalBytes > 0 ? (double)extractedBytes / totalBytes * 100 : 100
                });
            }
        }
    }
}

public class FileOperationProgress
{
    public string? CurrentFile { get; set; }
    public int TotalFiles { get; set; }
    public int ProcessedFiles { get; set; }
    public long TotalBytes { get; set; }
    public long ProcessedBytes { get; set; }
    public double PercentComplete { get; set; }
}
