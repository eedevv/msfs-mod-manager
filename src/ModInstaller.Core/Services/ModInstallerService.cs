using System.IO.Compression;
using ModInstaller.Core.Database;
using ModInstaller.Core.Models;
using ModInstaller.Core.Utils;

namespace ModInstaller.Core.Services;

/// <summary>
/// Handles mod installation, uninstallation, enable/disable operations.
/// </summary>
public class ModInstallerService
{
    private readonly ModDatabase _db;
    private readonly ModScanner _scanner;
    private readonly FileOperations _fileOps;
    private readonly AppSettings _settings;

    public ModInstallerService(ModDatabase db, ModScanner scanner, FileOperations fileOps, AppSettings settings)
    {
        _db = db;
        _scanner = scanner;
        _fileOps = fileOps;
        _settings = settings;
    }

    /// <summary>
    /// Installs a mod from a ZIP file or folder into the Community folder.
    /// </summary>
    public async Task<Mod?> InstallModAsync(
        string sourcePath,
        string communityFolder,
        SimVersion simVersion,
        bool useJunction,
        IProgress<FileOperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string modSourceDir;
        string? tempDir = null;

        try
        {
            // Extract if archive
            if (IsArchive(sourcePath))
            {
                tempDir = Path.Combine(Path.GetTempPath(), $"MSFSModInstaller_{Guid.NewGuid():N}");
                Directory.CreateDirectory(tempDir);

                progress?.Report(new FileOperationProgress { CurrentFile = "Extracting archive..." });

                await ExtractArchiveAsync(sourcePath, tempDir, progress, cancellationToken);

                // Find the actual mod root inside the extracted content
                var modRoot = ModScanner.FindModRootInExtractedArchive(tempDir);
                if (modRoot == null)
                    throw new InvalidOperationException("No valid MSFS mod found in the archive.");

                modSourceDir = modRoot;
            }
            else if (Directory.Exists(sourcePath))
            {
                if (!ModScanner.IsValidModDirectory(sourcePath))
                {
                    // Check if the folder contains mod subfolders (multi-mod install)
                    var subMods = Directory.GetDirectories(sourcePath)
                        .Where(ModScanner.IsValidModDirectory)
                        .ToList();

                    if (subMods.Count == 0)
                        throw new InvalidOperationException("The selected folder does not contain a valid MSFS mod.");

                    // Install each sub-mod
                    Mod? lastMod = null;
                    foreach (var subMod in subMods)
                    {
                        lastMod = await InstallSingleModAsync(subMod, communityFolder, simVersion, useJunction, progress, cancellationToken);
                    }
                    return lastMod;
                }

                modSourceDir = sourcePath;
            }
            else
            {
                throw new FileNotFoundException($"Source not found: {sourcePath}");
            }

            return await InstallSingleModAsync(modSourceDir, communityFolder, simVersion, useJunction, progress, cancellationToken);
        }
        finally
        {
            // Clean up temp directory
            if (tempDir != null && Directory.Exists(tempDir))
            {
                try { FileOperations.SafeDeleteDirectory(tempDir); } catch { }
            }
        }
    }

    private async Task<Mod?> InstallSingleModAsync(
        string modSourceDir,
        string communityFolder,
        SimVersion simVersion,
        bool useJunction,
        IProgress<FileOperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        var modFolderName = Path.GetFileName(modSourceDir);
        var destPath = Path.Combine(communityFolder, modFolderName);

        // Check for existing mod
        if (Directory.Exists(destPath) || JunctionHelper.IsJunction(destPath))
        {
            throw new IOException($"A mod already exists at {destPath}. Remove or back it up before installing a replacement; no files were changed.");
        }

        var baseDir = useJunction ? _settings.ModLibraryDirectory : communityFolder;
        Directory.CreateDirectory(baseDir);
        var finalContentPath = useJunction ? Path.Combine(baseDir, modFolderName) : destPath;
        if (Directory.Exists(finalContentPath) || JunctionHelper.IsJunction(finalContentPath))
            throw new IOException($"Destination already exists: {finalContentPath}. No files were changed.");

        // Stage first so a failed copy never leaves a partial installed mod.
        var stagePath = Path.Combine(baseDir, $".msfsmm-stage-{Guid.NewGuid():N}");
        try
        {
            await _fileOps.CopyDirectoryAsync(modSourceDir, stagePath, progress, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            Directory.Move(stagePath, finalContentPath);
            if (useJunction)
                JunctionHelper.CreateJunction(destPath, finalContentPath);
        }
        finally
        {
            if (Directory.Exists(stagePath))
            {
                try { FileOperations.SafeDeleteDirectory(stagePath); } catch { }
            }
        }

        // Scan the installed mod
        var mod = await _scanner.ScanModDirectoryAsync(destPath, simVersion);
        if (mod != null)
        {
            mod.InstalledDate = DateTime.Now;
            _db.UpsertMod(mod);
            _db.LogOperation("Install", mod.FolderName, $"Installed from {modSourceDir}");
        }

        return mod;
    }

    /// <summary>
    /// Uninstalls a mod from the Community folder.
    /// </summary>
    public void UninstallMod(Mod mod, bool createBackup = false)
    {
        var contentPath = GetDisabledContentPath(mod.FullPath) ?? mod.FullPath;
        if (JunctionHelper.IsJunction(mod.FullPath))
            contentPath = JunctionHelper.GetJunctionTarget(mod.FullPath)
                ?? throw new IOException("Cannot resolve the mod link; nothing was removed.");

        if (createBackup)
        {
            if (!Directory.Exists(contentPath))
                throw new DirectoryNotFoundException($"Mod content not found: {contentPath}");
            var backupPath = Path.Combine(_settings.BackupDirectory, $"{mod.FolderName}_{DateTime.Now:yyyyMMdd_HHmmss}");
            Directory.CreateDirectory(_settings.BackupDirectory);
            if (Directory.Exists(backupPath))
                throw new IOException($"Backup already exists: {backupPath}");
            CopyDirSync(contentPath, backupPath);
        }

        // A link target may be shared or owned by another application. Never delete it.
        if (JunctionHelper.IsJunction(mod.FullPath))
            JunctionHelper.RemoveJunction(mod.FullPath);
        else
            FileOperations.SafeDeleteDirectory(mod.FullPath);

        _db.LogOperation("Uninstall", mod.FolderName, createBackup ? "Backed up before uninstall" : null);
        _db.RemoveMod(mod.FolderName);
    }

    /// <summary>
    /// Enables a mod by creating a junction/copy from disabled storage to Community.
    /// </summary>
    public static string? GetDisabledContentPath(string disabledPath)
    {
        var marker = Path.Combine(disabledPath, ".modmanager_disabled");
        if (!File.Exists(marker)) return null;
        var value = File.ReadAllText(marker).Trim();
        const string prefix = "library_path=";
        if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(value[prefix.Length..]))
            throw new IOException($"Invalid disabled-mod marker: {marker}");
        return value[prefix.Length..];
    }

    public async Task EnableModAsync(Mod mod, string communityFolder, IProgress<FileOperationProgress>? progress = null)
    {
        var disabledPath = Path.Combine(AppSettings.DisabledModsDirectory, mod.FolderName);
        var communityPath = Path.Combine(communityFolder, mod.FolderName);
        if (!Directory.Exists(communityFolder)) throw new DirectoryNotFoundException("Community folder not found.");
        if (!Directory.Exists(disabledPath)) throw new DirectoryNotFoundException($"Disabled mod not found: {disabledPath}");
        if (Directory.Exists(communityPath) || JunctionHelper.IsJunction(communityPath))
            throw new IOException($"Community destination already exists: {communityPath}");

        var originalTarget = GetDisabledContentPath(disabledPath);
        if (originalTarget != null)
        {
            if (!Directory.Exists(originalTarget))
                throw new DirectoryNotFoundException($"Disabled mod's original content is unavailable: {originalTarget}");
            // Recreate the original link regardless of today's library settings.
            JunctionHelper.CreateJunction(communityPath, originalTarget);
            FileOperations.SafeDeleteDirectory(disabledPath);
        }
        else if (_settings.UseJunctions)
        {
            var libraryPath = Path.Combine(_settings.ModLibraryDirectory, mod.FolderName);
            if (Directory.Exists(libraryPath) || JunctionHelper.IsJunction(libraryPath))
                throw new IOException($"Library destination already exists: {libraryPath}. No files were removed.");
            Directory.CreateDirectory(_settings.ModLibraryDirectory);
            await _fileOps.MoveDirectoryAsync(disabledPath, libraryPath, progress);
            try { JunctionHelper.CreateJunction(communityPath, libraryPath); }
            catch
            {
                // Preserve the original disabled state when link creation fails.
                await _fileOps.MoveDirectoryAsync(libraryPath, disabledPath, progress);
                throw;
            }
        }
        else
            await _fileOps.MoveDirectoryAsync(disabledPath, communityPath, progress);

        mod.Status = ModStatus.Enabled;
        mod.FullPath = communityPath;
        _db.UpsertMod(mod);
        _db.LogOperation("Enable", mod.FolderName);
    }

    /// <summary>Disable without overwriting any existing disabled mod.</summary>
    public async Task DisableModAsync(Mod mod, IProgress<FileOperationProgress>? progress = null)
    {
        Directory.CreateDirectory(AppSettings.DisabledModsDirectory);
        var disabledPath = Path.Combine(AppSettings.DisabledModsDirectory, mod.FolderName);
        if (Directory.Exists(disabledPath) || File.Exists(disabledPath))
            throw new IOException($"Disabled destination already exists: {disabledPath}. No files were removed.");

        if (JunctionHelper.IsJunction(mod.FullPath))
        {
            var libraryPath = JunctionHelper.GetJunctionTarget(mod.FullPath)
                ?? throw new IOException("Cannot resolve the mod link; nothing was removed.");
            if (!Directory.Exists(libraryPath))
                throw new DirectoryNotFoundException($"Link target is unavailable: {libraryPath}");
            Directory.CreateDirectory(disabledPath);
            try
            {
                await File.WriteAllTextAsync(Path.Combine(disabledPath, ".modmanager_disabled"),
                    $"library_path={libraryPath}");
                JunctionHelper.RemoveJunction(mod.FullPath);
            }
            catch
            {
                FileOperations.SafeDeleteDirectory(disabledPath);
                throw;
            }
        }
        else
            await _fileOps.MoveDirectoryAsync(mod.FullPath, disabledPath, progress);

        mod.Status = ModStatus.Disabled;
        mod.FullPath = disabledPath;
        _db.UpsertMod(mod);
        _db.LogOperation("Disable", mod.FolderName);
    }

    private static bool IsArchive(string path) =>
        path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".7z", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".rar", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Extracts any archive using multiple strategies with fallbacks:
    /// 1. .NET ZipFile (for .zip)
    /// 2. Windows built-in tar command (handles most ZIPs, built into Win10+)
    /// 3. 7-Zip (for .7z, .rar, and as fallback for corrupt ZIPs)
    /// </summary>
    private async Task ExtractArchiveAsync(
        string archivePath, string destDir,
        IProgress<FileOperationProgress>? progress,
        CancellationToken ct)
    {
        var ext = Path.GetExtension(archivePath).ToLowerInvariant();

        // For .zip files, try multiple methods
        if (ext == ".zip")
        {
            // Strategy 1: .NET ZipFile
            try
            {
                await _fileOps.ExtractZipAsync(archivePath, destDir, progress, ct);
                return;
            }
            catch (OperationCanceledException) { throw; }
            catch (IOException ex) when (ex.Message.StartsWith("Zip slip detected:", StringComparison.Ordinal)) { throw; }
            catch
            {
                // ZipFile failed (corrupt central directory, etc.) — try fallbacks
                CleanDirectory(destDir);
            }

            // Strategy 2: Windows tar (built into Win10+, handles many ZIP variants)
            try
            {
                await RunProcessAsync("tar", $"-xf \"{archivePath}\" -C \"{destDir}\"", ct);
                if (Directory.GetFileSystemEntries(destDir).Length > 0) return;
            }
            catch (OperationCanceledException) { throw; }
            catch { CleanDirectory(destDir); }

        }

        // Strategy 4: 7-Zip (works for all formats including corrupt ZIPs)
        var sevenZip = Find7Zip();
        if (sevenZip != null)
        {
            try
            {
                await RunProcessAsync(sevenZip, $"x \"{archivePath}\" -o\"{destDir}\" -y", ct);
                if (Directory.GetFileSystemEntries(destDir).Length > 0) return;
            }
            catch (OperationCanceledException) { throw; }
            catch { CleanDirectory(destDir); }
        }

        // All strategies failed
        var tried = ext == ".zip" ? ".NET ZipFile, tar" : "extraction";
        if (sevenZip != null) tried += ", 7-Zip";
        throw new IOException($"Failed to extract {Path.GetFileName(archivePath)}. Tried: {tried}. The archive may be corrupted." +
            (sevenZip == null ? " Install 7-Zip (7-zip.org) for better archive support." : ""));
    }

    private static string? Find7Zip()
    {
        string[] paths = [
            @"C:\Program Files\7-Zip\7z.exe",
            @"C:\Program Files (x86)\7-Zip\7z.exe",
            @"C:\7-Zip\7z.exe"
        ];

        var found = paths.FirstOrDefault(File.Exists);
        if (found != null) return found;

        // Check PATH
        var pathDirs = Environment.GetEnvironmentVariable("PATH")?.Split(';') ?? [];
        foreach (var dir in pathDirs)
        {
            try
            {
                var candidate = Path.Combine(dir.Trim(), "7z.exe");
                if (File.Exists(candidate)) return candidate;
            }
            catch { }
        }
        return null;
    }

    private static async Task RunProcessAsync(string fileName, string arguments, CancellationToken ct)
    {
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        }) ?? throw new IOException($"Failed to start {fileName}");

        // Drain both pipes concurrently so verbose extractors cannot block on a full buffer.
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            if (ct.IsCancellationRequested) throw;
            throw new TimeoutException($"{fileName} timed out");
        }
        await stdout;
        var error = await stderr;
        if (process.ExitCode != 0)
            throw new IOException($"{fileName} failed (exit {process.ExitCode}): {error}");
    }

    private static void CleanDirectory(string path)
    {
        try
        {
            foreach (var entry in Directory.GetFileSystemEntries(path))
            {
                if (File.Exists(entry)) File.Delete(entry);
                else if (Directory.Exists(entry)) Directory.Delete(entry, true);
            }
        }
        catch { }
    }

    private static void CopyDirSync(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(source, file);
            var destFile = Path.Combine(dest, rel);
            var destFileDir = Path.GetDirectoryName(destFile);
            if (destFileDir != null) Directory.CreateDirectory(destFileDir);
            File.Copy(file, destFile, true);
        }
    }
}
