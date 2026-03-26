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
            throw new DirectoryNotFoundException($"Backup not found: {backupPath}");

        var modName = Path.GetFileName(backupPath) ?? throw new IOException("Invalid backup path");

        // Strip timestamp suffix: "mymod_20240101_120000" → "mymod"
        // Match pattern: _YYYYMMDD_HHMMSS at the end (exactly 16 chars: _8_6)
        if (modName.Length > 16)
        {
            var candidate = modName[^16..]; // last 16 chars
            if (candidate.Length == 16 && candidate[0] == '_' && candidate[9] == '_' &&
                candidate[1..9].All(char.IsDigit) && candidate[10..].All(char.IsDigit))
            {
                modName = modName[..^16];
            }
        }

        var destPath = Path.Combine(communityFolder, modName);

        if (Directory.Exists(destPath) || JunctionHelper.IsJunction(destPath))
        {
            if (!overwrite)
                throw new IOException($"Mod already exists: {modName}. Set overwrite=true to replace.");

            if (JunctionHelper.IsJunction(destPath))
                JunctionHelper.RemoveJunction(destPath);
            else
                FileOperations.SafeDeleteDirectory(destPath);
        }

        await _fileOps.CopyDirectoryAsync(backupPath, destPath, progress, cancellationToken);
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
            if (backup.CreatedDate < cutoff)
            {
                if (backup.IsCompressed)
                    File.Delete(backup.Path);
                else
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
