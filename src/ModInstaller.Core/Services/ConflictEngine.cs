using System.Security.Cryptography;
using ModInstaller.Core.Database;
using ModInstaller.Core.Models;

namespace ModInstaller.Core.Services;

/// <summary>
/// Detects file conflicts between mods using hash-based comparison.
/// Much faster than V2's O(n²) temp-file approach.
/// </summary>
public class ConflictEngine
{
    private readonly ModDatabase _db;

    public ConflictEngine(ModDatabase db)
    {
        _db = db;
    }

    /// <summary>
    /// Scans all mods for file conflicts (files with the same relative path).
    /// Returns conflicts grouped by file path.
    /// </summary>
    public Task<List<ConflictResult>> ScanForConflictsAsync(
        List<Mod> mods,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => ScanForConflictsInternal(mods, progress, cancellationToken), cancellationToken);
    }

    private List<ConflictResult> ScanForConflictsInternal(
        List<Mod> mods,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        // Build a file index: relative path → list of mods that contain it
        var fileIndex = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        int scanned = 0;

        foreach (var mod in mods)
        {
            cancellationToken.ThrowIfCancellationRequested();
            scanned++;
            progress?.Report($"Indexing {mod.FolderName} ({scanned}/{mods.Count})...");

            if (!Directory.Exists(mod.FullPath)) continue;

            try
            {
                var files = Directory.GetFiles(mod.FullPath, "*", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    var relativePath = Path.GetRelativePath(mod.FullPath, file).ToLowerInvariant();

                    // Skip metadata files
                    if (relativePath is "manifest.json" or "layout.json" or ".backup_meta.json")
                        continue;

                    if (!fileIndex.TryGetValue(relativePath, out var modList))
                    {
                        modList = [];
                        fileIndex[relativePath] = modList;
                    }

                    modList.Add(mod.FolderName);
                }
            }
            catch { /* Skip mods we can't read */ }
        }

        // Find files that appear in multiple mods
        var conflictPairs = new Dictionary<(string, string), List<string>>();

        foreach (var (relativePath, modNames) in fileIndex)
        {
            if (modNames.Count < 2) continue;

            // Generate all pairs
            for (int i = 0; i < modNames.Count; i++)
            {
                for (int j = i + 1; j < modNames.Count; j++)
                {
                    var key = (
                        string.Compare(modNames[i], modNames[j], StringComparison.OrdinalIgnoreCase) < 0
                            ? modNames[i] : modNames[j],
                        string.Compare(modNames[i], modNames[j], StringComparison.OrdinalIgnoreCase) < 0
                            ? modNames[j] : modNames[i]
                    );

                    if (!conflictPairs.TryGetValue(key, out var files))
                    {
                        files = [];
                        conflictPairs[key] = files;
                    }

                    files.Add(relativePath);
                }
            }
        }

        // Convert to results
        var results = new List<ConflictResult>();
        foreach (var (pair, files) in conflictPairs)
        {
            var result = new ConflictResult
            {
                ModA = pair.Item1,
                ModB = pair.Item2,
                ConflictingFiles = files
            };

            results.Add(result);
            _db.SaveConflict(result);
        }

        progress?.Report($"Scan complete. Found {results.Count} conflict(s).");
        return results;
    }

    /// <summary>
    /// Checks if a specific mod conflicts with any currently installed mods.
    /// Used during installation to warn the user.
    /// </summary>
    public List<ConflictResult> CheckModConflicts(string modPath, List<Mod> installedMods)
    {
        var conflicts = new List<ConflictResult>();

        if (!Directory.Exists(modPath)) return conflicts;

        var newModName = Path.GetFileName(modPath);
        var newModFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var file in Directory.GetFiles(modPath, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(modPath, file).ToLowerInvariant();
                if (relativePath is not "manifest.json" and not "layout.json")
                    newModFiles.Add(relativePath);
            }
        }
        catch { return conflicts; }

        foreach (var installedMod in installedMods)
        {
            if (!Directory.Exists(installedMod.FullPath)) continue;

            var conflictingFiles = new List<string>();

            try
            {
                foreach (var file in Directory.GetFiles(installedMod.FullPath, "*", SearchOption.AllDirectories))
                {
                    var relativePath = Path.GetRelativePath(installedMod.FullPath, file).ToLowerInvariant();
                    if (newModFiles.Contains(relativePath))
                        conflictingFiles.Add(relativePath);
                }
            }
            catch { continue; }

            if (conflictingFiles.Count > 0)
            {
                conflicts.Add(new ConflictResult
                {
                    ModA = newModName,
                    ModB = installedMod.FolderName,
                    ConflictingFiles = conflictingFiles
                });
            }
        }

        return conflicts;
    }

    /// <summary>
    /// Verifies if two conflicting files are actually identical (same content hash).
    /// </summary>
    public static async Task<bool> AreFilesIdenticalAsync(string fileA, string fileB)
    {
        if (!File.Exists(fileA) || !File.Exists(fileB)) return false;

        var infoA = new FileInfo(fileA);
        var infoB = new FileInfo(fileB);

        // Quick check: different sizes means different content
        if (infoA.Length != infoB.Length) return false;

        using var hashAlg = SHA256.Create();

        await using var streamA = File.OpenRead(fileA);
        var hashA = await hashAlg.ComputeHashAsync(streamA);

        await using var streamB = File.OpenRead(fileB);
        var hashB = await hashAlg.ComputeHashAsync(streamB);

        return hashA.SequenceEqual(hashB);
    }
}
