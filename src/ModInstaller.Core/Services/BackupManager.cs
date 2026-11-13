using System.IO.Compression;
using ModInstaller.Core.Models;
using ModInstaller.Core.Utils;

namespace ModInstaller.Core.Services;

/// <summary>
/// Manages mod backups — create, restore, list, and clean up.
/// </summary>
public class BackupManager
{
    private readonly AppSettings _settings;
    private readonly FileOperations _fileOps;

    public BackupManager(AppSettings settings, FileOperations fileOps)
    {
        _settings = settings;
        _fileOps = fileOps;
    }

    /// <summary>
    /// Creates a backup of a single mod.
    /// </summary>
    public async Task<string> BackupModAsync(
        Mod mod,
        IProgress<FileOperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_settings.BackupDirectory);

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var backupName = $"{mod.FolderName}_{timestamp}";
        var backupPath = Path.Combine(_settings.BackupDirectory, backupName);

        var sourcePath = mod.FullPath;

        // If it's a junction, back up the target content
        if (JunctionHelper.IsJunction(mod.FullPath))
        {
            var target = JunctionHelper.GetJunctionTarget(mod.FullPath);
            if (target != null && Directory.Exists(target))
                sourcePath = target;
        }

        await _fileOps.CopyDirectoryAsync(sourcePath, backupPath, progress, cancellationToken);

        // Write metadata
        var metaPath = Path.Combine(backupPath, ".backup_meta.json");
        var meta = System.Text.Json.JsonSerializer.Serialize(new
        {
            ModName = mod.FolderName,
            mod.Title,
            mod.Version,
            mod.Type,
            BackupDate = DateTime.Now,
            OriginalPath = mod.FullPath,
            SizeBytes = mod.SizeBytes
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

        await File.WriteAllTextAsync(metaPath, meta, cancellationToken);

        return backupPath;
    }

    /// <summary>
    /// Creates a compressed ZIP backup of a mod.
    /// </summary>
    public async Task<string> BackupModCompressedAsync(
        Mod mod,
        IProgress<FileOperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_settings.BackupDirectory);

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var zipName = $"{mod.FolderName}_{timestamp}.zip";
        var zipPath = Path.Combine(_settings.BackupDirectory, zipName);

        var sourcePath = mod.FullPath;
        if (JunctionHelper.IsJunction(mod.FullPath))
        {
            var target = JunctionHelper.GetJunctionTarget(mod.FullPath);
            if (target != null) sourcePath = target;
        }

        await Task.Run(() =>
        {
            ZipFile.CreateFromDirectory(sourcePath, zipPath, CompressionLevel.Optimal, includeBaseDirectory: true);
        }, cancellationToken);

        return zipPath;
    }

    /// <summary>
    /// Creates a full backup of all mods in the Community folder.
    /// </summary>
    public async Task<string> BackupAllModsAsync(
        string communityFolder,
        IProgress<FileOperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_settings.BackupDirectory);

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var backupDir = Path.Combine(_settings.BackupDirectory, $"FullBackup_{timestamp}");
        if (Directory.Exists(backupDir))
            throw new IOException($"Backup already exists: {backupDir}");
        Directory.CreateDirectory(backupDir);

        var modDirs = Directory.GetDirectories(communityFolder);
        int completed = 0;

        foreach (var modDir in modDirs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var modName = Path.GetFileName(modDir);
            var destPath = Path.Combine(backupDir, modName);

            var sourcePath = modDir;
            if (JunctionHelper.IsJunction(modDir))
            {
                var target = JunctionHelper.GetJunctionTarget(modDir);
                if (target != null) sourcePath = target;
            }

            await _fileOps.CopyDirectoryAsync(sourcePath, destPath, null, cancellationToken);
            completed++;

            progress?.Report(new FileOperationProgress
            {
                CurrentFile = modName,
                ProcessedFiles = completed,
                TotalFiles = modDirs.Length,
                PercentComplete = (double)completed / modDirs.Length * 100
            });
        }

        await File.WriteAllTextAsync(Path.Combine(backupDir, ".backup_meta.json"),
            "{\"Kind\":\"FullBackup\"}", cancellationToken);
        return backupDir;
    }

    /// <summary>
    /// Restores a mod from a backup directory into the Community folder.
    /// </summary>
    public async Task RestoreBackupAsync(
        string backupPath,
        string communityFolder,
        bool overwrite = false,
        IProgress<FileOperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(backupPath))
            throw new DirectoryNotFoundException($"Backup directory not found (ZIP restore is not yet supported): {backupPath}");
        if (!Directory.Exists(communityFolder))
            throw new DirectoryNotFoundException($"Community folder not found: {communityFolder}");

        var name = Path.GetFileName(backupPath);
        var isFullBackup = name.StartsWith("FullBackup_", StringComparison.OrdinalIgnoreCase);
        if (isFullBackup)
        {
            var folders = Directory.GetDirectories(backupPath);
            // Check all destinations before changing anything; overwrites are intentionally disabled.
            foreach (var folder in folders)
                EnsureDestinationFree(Path.Combine(communityFolder, Path.GetFileName(folder)));
            foreach (var folder in folders)
                await RestoreOneAsync(folder, Path.Combine(communityFolder, Path.GetFileName(folder)), progress, cancellationToken);
            return;
        }

        if (name.Length > 16)
        {
            var suffix = name[^16..];
            if (suffix[0] == '_' && suffix[9] == '_' && suffix[1..9].All(char.IsDigit) && suffix[10..].All(char.IsDigit))
                name = name[..^16];
        }
        var destPath = Path.Combine(communityFolder, name);
        EnsureDestinationFree(destPath);
        await RestoreOneAsync(backupPath, destPath, progress, cancellationToken);
    }

    private static void EnsureDestinationFree(string path)
    {
        if (Directory.Exists(path) || File.Exists(path) || JunctionHelper.IsJunction(path))
            throw new IOException($"Destination already exists: {path}. Move or back it up before restoring; nothing was overwritten.");
    }

    private async Task RestoreOneAsync(string source, string dest, IProgress<FileOperationProgress>? progress, CancellationToken ct)
    {
        var stage = Path.Combine(Path.GetDirectoryName(dest)!, $".msfsmm-restore-{Guid.NewGuid():N}");
        try
        {
            await _fileOps.CopyDirectoryAsync(source, stage, progress, ct);
            var metadata = Path.Combine(stage, ".backup_meta.json");
            if (File.Exists(metadata)) File.Delete(metadata);
            ct.ThrowIfCancellationRequested();
            Directory.Move(stage, dest);
        }
        finally
        {
            if (Directory.Exists(stage))
                try { FileOperations.SafeDeleteDirectory(stage); } catch { }
        }
    }

    /// <summary>
    /// Lists all available backups with metadata.
    /// </summary>
    public List<BackupInfo> ListBackups()
    {
        var backups = new List<BackupInfo>();

        if (!Directory.Exists(_settings.BackupDirectory))
            return backups;

        foreach (var dir in Directory.GetDirectories(_settings.BackupDirectory))
        {
            var info = new BackupInfo
            {
                Path = dir,
                Name = Path.GetFileName(dir),
                CreatedDate = Directory.GetCreationTime(dir),
                SizeBytes = FileOperations.GetDirectorySize(dir),
                IsCompressed = false
            };

            // Try to read metadata
            var metaPath = Path.Combine(dir, ".backup_meta.json");
            if (File.Exists(metaPath))
            {
                try
                {
                    var json = File.ReadAllText(metaPath);
                    var meta = System.Text.Json.JsonDocument.Parse(json);
                    info.ModName = meta.RootElement.GetProperty("ModName").GetString();
                    info.ModVersion = meta.RootElement.TryGetProperty("Version", out var v) ? v.GetString() : null;
                }
                catch { }
            }

            backups.Add(info);
        }

        // Also list ZIP backups
        foreach (var zip in Directory.GetFiles(_settings.BackupDirectory, "*.zip"))
        {
            var fileInfo = new FileInfo(zip);
            backups.Add(new BackupInfo
            {
                Path = zip,
                Name = Path.GetFileNameWithoutExtension(zip),
                CreatedDate = fileInfo.CreationTime,
                SizeBytes = fileInfo.Length,
                IsCompressed = true
            });
        }

        return backups.OrderByDescending(b => b.CreatedDate).ToList();
    }

    /// <summary>
    /// Deletes backups older than the specified number of days.
    /// </summary>
    public int CleanOldBackups(int olderThanDays)
    {
        var cutoff = DateTime.Now.AddDays(-olderThanDays);
        int deleted = 0;

        foreach (var backup in ListBackups())
        {
            // Only directories carrying our metadata are eligible for automatic deletion.
            // Legacy backups and ZIPs have no reliable ownership marker; preserve them.
            if (!backup.IsCompressed && backup.CreatedDate < cutoff &&
                File.Exists(Path.Combine(backup.Path, ".backup_meta.json")))
            {
                FileOperations.SafeDeleteDirectory(backup.Path);
                deleted++;
            }
        }

        return deleted;
    }
}

public class BackupInfo
{
    public required string Path { get; set; }
    public required string Name { get; set; }
    public DateTime CreatedDate { get; set; }
    public long SizeBytes { get; set; }
    public bool IsCompressed { get; set; }
    public string? ModName { get; set; }
    public string? ModVersion { get; set; }

    public string SizeDisplay => SizeBytes switch
    {
        < 1024 * 1024 => $"{SizeBytes / 1024.0:F1} KB",
        < 1024L * 1024 * 1024 => $"{SizeBytes / (1024.0 * 1024):F1} MB",
        _ => $"{SizeBytes / (1024.0 * 1024 * 1024):F2} GB"
    };
}
