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
                if (!Path.GetFullPath(destFile).StartsWith(Path.GetFullPath(destDir), StringComparison.OrdinalIgnoreCase))
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

    public async Task MoveDirectoryAsync(
        string sourceDir, string destDir,
        IProgress<FileOperationProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (!Directory.Exists(sourceDir))
            throw new DirectoryNotFoundException($"Source not found: {sourceDir}");

        // Same drive: try fast rename first
        if (Path.GetPathRoot(sourceDir)?.Equals(Path.GetPathRoot(destDir), StringComparison.OrdinalIgnoreCase) == true)
        {
            try
            {
                if (Directory.Exists(destDir)) Directory.Delete(destDir, true);
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
        if (!Directory.Exists(path)) return (0, 0);
        try
        {
            long size = 0;
            int count = 0;
            foreach (var file in new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories))
            {
                size += file.Length;
                count++;
            }
            return (size, count);
        }
        catch { return (0, 0); }
    }

    public static long GetDirectorySize(string path) => GetDirectoryStats(path).SizeBytes;
    public static int GetFileCount(string path) => GetDirectoryStats(path).FileCount;

    public static void SafeDeleteDirectory(string path)
    {
        if (!Directory.Exists(path)) return;
        try
        {
            foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(path, recursive: true);
        }
        catch (Exception ex)
        {
            throw new IOException($"Failed to delete directory {Path.GetFileName(path)}: {ex.Message}", ex);
        }
    }

    public async Task ExtractZipAsync(
        string zipPath, string destDir,
        IProgress<FileOperationProgress>? progress = null,
        CancellationToken ct = default)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var totalEntries = archive.Entries.Count;
        long totalBytes = archive.Entries.Sum(e => e.Length);
        long extractedBytes = 0;
        int extractedFiles = 0;
        var lastReport = DateTime.MinValue;

        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();

            var destPath = Path.GetFullPath(Path.Combine(destDir, entry.FullName));

            // Prevent zip slip attack (path traversal via archive entry names)
            if (!destPath.StartsWith(Path.GetFullPath(destDir), StringComparison.OrdinalIgnoreCase))
                throw new IOException($"Zip slip detected: {entry.FullName}");

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destPath);
                continue;
            }

            var entryDir = Path.GetDirectoryName(destPath);
            if (entryDir != null) Directory.CreateDirectory(entryDir);

            using (var entryStream = entry.Open())
            using (var fileStream = File.Create(destPath))
                await entryStream.CopyToAsync(fileStream, ct);

            extractedBytes += entry.Length;
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
