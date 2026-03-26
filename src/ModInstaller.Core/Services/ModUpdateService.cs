using System.Security.Cryptography;
using System.Text.Json;
using ModInstaller.Core.Models;

namespace ModInstaller.Core.Services;

/// <summary>
/// Local mod change-detection service. Compares currently installed mods against a cached
/// baseline of file sizes, modification dates, and content hashes to detect changes.
/// No web calls — purely offline comparison.
/// </summary>
public class ModUpdateService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private Dictionary<string, ModSnapshot> _cache = new(StringComparer.OrdinalIgnoreCase);
    private string? _cacheFilePath;

    /// <summary>
    /// Checks installed mods against the cached baseline and returns change information.
    /// </summary>
    public async Task<List<ModUpdateInfo>> CheckForUpdatesAsync(
        List<Mod> mods,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ModUpdateInfo>();
        var now = DateTime.UtcNow;

        foreach (var mod in mods)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var info = new ModUpdateInfo
            {
                FolderName = mod.FolderName,
                DisplayName = mod.DisplayName,
                CurrentVersion = mod.Version,
                LastChecked = now
            };

            var currentSnapshot = await CaptureSnapshotAsync(mod, cancellationToken).ConfigureAwait(false);

            if (_cache.TryGetValue(mod.FolderName, out var previousSnapshot))
            {
                info.PreviousVersion = previousSnapshot.Version;
                info.HasChanged = HasSnapshotChanged(previousSnapshot, currentSnapshot);
                info.SizeDelta = currentSnapshot.TotalSizeBytes - previousSnapshot.TotalSizeBytes;
            }
            else
            {
                // No baseline — this is a new mod, not an update
                info.HasChanged = false;
                info.SizeDelta = 0;
            }

            // Update the cache with the current snapshot
            _cache[mod.FolderName] = currentSnapshot;

            results.Add(info);
        }

        return results;
    }

    /// <summary>
    /// Saves the current cache to a JSON file.
    /// </summary>
    public async Task SaveCacheAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        _cacheFilePath = filePath;

        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(_cache, JsonOptions);
        await File.WriteAllTextAsync(filePath, json, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Loads a previously saved cache from a JSON file.
    /// </summary>
    public async Task LoadCacheAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        _cacheFilePath = filePath;

        if (!File.Exists(filePath))
        {
            _cache = new Dictionary<string, ModSnapshot>(StringComparer.OrdinalIgnoreCase);
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            var loaded = JsonSerializer.Deserialize<Dictionary<string, ModSnapshot>>(json, JsonOptions);
            _cache = loaded ?? new Dictionary<string, ModSnapshot>(StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            // Corrupt cache — start fresh
            _cache = new Dictionary<string, ModSnapshot>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Removes a mod entry from the cache.
    /// </summary>
    public void RemoveFromCache(string folderName)
    {
        _cache.Remove(folderName);
    }

    /// <summary>
    /// Clears the entire cache.
    /// </summary>
    public void ClearCache()
    {
        _cache.Clear();
    }

    private static async Task<ModSnapshot> CaptureSnapshotAsync(Mod mod, CancellationToken cancellationToken)
    {
        var snapshot = new ModSnapshot
        {
            FolderName = mod.FolderName,
            Version = mod.Version,
            CapturedAt = DateTime.UtcNow
        };

        if (!Directory.Exists(mod.FullPath))
            return snapshot;

        try
        {
            var files = Directory.GetFiles(mod.FullPath, "*", SearchOption.AllDirectories);
            long totalSize = 0;
            DateTime latestWrite = DateTime.MinValue;

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var fi = new FileInfo(file);
                    totalSize += fi.Length;

                    if (fi.LastWriteTimeUtc > latestWrite)
                        latestWrite = fi.LastWriteTimeUtc;

                    // Store relative path and size for change detection
                    var relativePath = Path.GetRelativePath(mod.FullPath, file);
                    snapshot.Files[relativePath] = new FileSnapshot
                    {
                        SizeBytes = fi.Length,
                        LastModifiedUtc = fi.LastWriteTimeUtc
                    };
                }
                catch (IOException)
                {
                    // File may be locked — skip it
                }
            }

            snapshot.TotalSizeBytes = totalSize;
            snapshot.FileCount = files.Length;
            snapshot.LatestFileWriteUtc = latestWrite;

            // Compute a lightweight hash of the manifest for quick version comparison
            var manifestPath = Path.Combine(mod.FullPath, "manifest.json");
            if (File.Exists(manifestPath))
            {
                snapshot.ManifestHash = await ComputeFileHashAsync(manifestPath, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Could not fully snapshot — return partial data
        }

        return snapshot;
    }

    private static bool HasSnapshotChanged(ModSnapshot previous, ModSnapshot current)
    {
        // Quick checks first
        if (previous.FileCount != current.FileCount)
            return true;

        if (previous.TotalSizeBytes != current.TotalSizeBytes)
            return true;

        if (previous.Version != current.Version)
            return true;

        if (previous.ManifestHash != current.ManifestHash)
            return true;

        // Detailed file-level comparison
        if (previous.Files.Count != current.Files.Count)
            return true;

        foreach (var (path, prevFile) in previous.Files)
        {
            if (!current.Files.TryGetValue(path, out var curFile))
                return true; // File was removed or renamed

            if (prevFile.SizeBytes != curFile.SizeBytes)
                return true;

            if (prevFile.LastModifiedUtc != curFile.LastModifiedUtc)
                return true;
        }

        return false;
    }

    private static async Task<string?> ComputeFileHashAsync(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            var hashBytes = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
            return Convert.ToHexString(hashBytes);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Internal snapshot of a mod's state at a point in time.
    /// </summary>
    private class ModSnapshot
    {
        public string FolderName { get; set; } = "";
        public string? Version { get; set; }
        public long TotalSizeBytes { get; set; }
        public int FileCount { get; set; }
        public DateTime LatestFileWriteUtc { get; set; }
        public DateTime CapturedAt { get; set; }
        public string? ManifestHash { get; set; }
        public Dictionary<string, FileSnapshot> Files { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Snapshot of an individual file within a mod.
    /// </summary>
    private class FileSnapshot
    {
        public long SizeBytes { get; set; }
        public DateTime LastModifiedUtc { get; set; }
    }
}
