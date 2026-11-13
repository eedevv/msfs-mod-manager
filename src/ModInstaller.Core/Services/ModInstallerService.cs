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
        CancellationToken cancellationToken = default,
        Func<string, bool>? confirmReplacement = null)
    {
        using var operation = MutationGate.Enter();
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

                var modRoots = ModScanner.FindModRootsInExtractedArchive(tempDir);
                if (modRoots.Count == 0)
                    throw new InvalidOperationException("No valid MSFS mod found in the archive.");
                var names = modRoots.Select(root => string.Equals(root, tempDir, StringComparison.OrdinalIgnoreCase)
                    ? Path.GetFileNameWithoutExtension(sourcePath) : Path.GetFileName(root)).ToList();
                if (names.Any(string.IsNullOrWhiteSpace) || names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Count)
                    throw new IOException("Archive contains duplicate or invalid package names; nothing was installed.");
                // Ask about all existing destinations before installing any package.
                var replacements = new List<bool>();
                foreach (var name in names)
                {
                    var destination = Path.Combine(communityFolder, name);
                    var exists = Directory.Exists(destination) || JunctionHelper.IsJunction(destination);
                    if (File.Exists(destination) && !exists)
                        throw new IOException($"A file exists at {destination}; nothing was installed.");
                    if (exists && (confirmReplacement == null || !confirmReplacement(destination)))
                        throw new IOException($"Replacement declined for {name}; nothing was installed.");
                    replacements.Add(exists);
                }
                Mod? lastArchiveMod = null;
                for (int i = 0; i < modRoots.Count; i++)
                    lastArchiveMod = await InstallSingleModAsync(modRoots[i], communityFolder, simVersion, useJunction,
                        progress, cancellationToken, names[i], replacements[i]);
                return lastArchiveMod;
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
                        lastMod = await InstallSingleModAsync(subMod, communityFolder, simVersion, useJunction, progress, cancellationToken,
                            confirmReplacement: confirmReplacement);
                    }
                    return lastMod;
                }

                modSourceDir = sourcePath;
            }
            else
            {
                throw new FileNotFoundException($"Source not found: {sourcePath}");
            }

            return await InstallSingleModAsync(modSourceDir, communityFolder, simVersion, useJunction, progress, cancellationToken,
                confirmReplacement: confirmReplacement);
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
        CancellationToken cancellationToken,
        string? folderName = null,
        bool? approvedReplacement = null,
        Func<string, bool>? confirmReplacement = null)
    {
        var modFolderName = folderName ?? Path.GetFileName(Path.TrimEndingDirectorySeparator(modSourceDir));
        var destPath = Path.Combine(communityFolder, modFolderName);

        var exists = Directory.Exists(destPath) || JunctionHelper.IsJunction(destPath);
        if (File.Exists(destPath) && !exists)
            throw new IOException($"A file already exists at {destPath}.");
        var overwrite = approvedReplacement ?? (exists && confirmReplacement?.Invoke(destPath) == true);
        if (exists && !overwrite)
            throw new IOException($"A mod already exists at {destPath}. Replacement was not approved.");

        var baseDir = useJunction
            ? new DisabledModStorage().GetLibraryDirectory(_settings.ModLibraryDirectory, communityFolder)
            : communityFolder;
        Directory.CreateDirectory(baseDir);
        var finalContentPath = useJunction ? Path.Combine(baseDir, modFolderName) : destPath;
        // Never replace or delete an existing library target; it may still be shared.
        if (useJunction && (Directory.Exists(finalContentPath) || File.Exists(finalContentPath) || JunctionHelper.IsJunction(finalContentPath)))
            finalContentPath = Path.Combine(baseDir, $"{modFolderName}__{Guid.NewGuid():N}");

        var stagePath = Path.Combine(baseDir, $".msfsmm-stage-{Guid.NewGuid():N}");
        string? stagedLink = null;
        try
        {
            await _fileOps.CopyDirectoryAsync(modSourceDir, stagePath, progress, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (useJunction)
            {
                Directory.Move(stagePath, finalContentPath);
                stagedLink = Path.Combine(communityFolder, $".msfsmm-stage-{Guid.NewGuid():N}");
                JunctionHelper.CreateJunction(stagedLink, finalContentPath);
                RecoverableSwap.Commit(stagedLink, destPath, overwrite);
            }
            else
                RecoverableSwap.Commit(stagePath, destPath, overwrite);
        }
        finally
        {
            if (stagedLink != null && JunctionHelper.IsJunction(stagedLink))
                try { JunctionHelper.RemoveJunction(stagedLink); } catch { }
            if (Directory.Exists(stagePath))
                try { FileOperations.SafeDeleteDirectory(stagePath); } catch { }
        }

        // Scan the installed mod
        var mod = await _scanner.ScanModDirectoryAsync(destPath, simVersion);
        if (mod != null)
        {
            mod.InstalledDate = DateTime.Now;
            _db.UpsertCommunityMod(communityFolder, mod);
            _db.LogOperation("Install", mod.FolderName, $"Installed from {modSourceDir}");
        }

        return mod;
    }

    /// <summary>
    /// Uninstalls a mod from the Community folder.
    /// </summary>
    public void UninstallMod(Mod mod, string communityFolder, bool createBackup = false)
    {
        using var operation = MutationGate.Enter();
        var expectedParent = mod.Status == ModStatus.Disabled
            ? new DisabledModStorage().GetDirectory(communityFolder) : Path.GetFullPath(communityFolder);
        var actualParent = Path.GetDirectoryName(Path.GetFullPath(mod.FullPath));
        if (actualParent == null || !string.Equals(Path.TrimEndingDirectorySeparator(actualParent),
                Path.TrimEndingDirectorySeparator(expectedParent), StringComparison.OrdinalIgnoreCase))
            throw new IOException("The mod does not belong to the selected Community folder; nothing was removed.");
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
        _db.RemoveCommunityMod(communityFolder, mod.FolderName);
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
        using var operation = MutationGate.Enter();
        var disabledPath = Path.Combine(new DisabledModStorage().GetDirectory(communityFolder), mod.FolderName);
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
            var libraryDirectory = new DisabledModStorage().GetLibraryDirectory(_settings.ModLibraryDirectory, communityFolder);
            var libraryPath = Path.Combine(libraryDirectory, mod.FolderName);
            if (Directory.Exists(libraryPath) || JunctionHelper.IsJunction(libraryPath))
                throw new IOException($"Library destination already exists: {libraryPath}. No files were removed.");
            Directory.CreateDirectory(libraryDirectory);
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
        _db.UpsertCommunityMod(communityFolder, mod);
        _db.LogOperation("Enable", mod.FolderName);
    }

    /// <summary>Disable without overwriting any existing disabled mod.</summary>
    public async Task DisableModAsync(Mod mod, IProgress<FileOperationProgress>? progress = null)
    {
        using var operation = MutationGate.Enter();
        var communityFolder = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(mod.FullPath))
            ?? throw new IOException("Cannot identify the mod's Community folder.");
        var disabledDirectory = new DisabledModStorage().GetDirectory(communityFolder);
        Directory.CreateDirectory(disabledDirectory);
        var disabledPath = Path.Combine(disabledDirectory, mod.FolderName);
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
        _db.UpsertCommunityMod(communityFolder, mod);
        _db.LogOperation("Disable", mod.FolderName);
    }

    private static bool IsArchive(string path) =>
        path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".7z", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".rar", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Extracts ZIPs using the validated .NET extractor and 7z/RAR using installed 7-Zip.
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
            // A failed ZIP validation or extraction must not be retried through an
            // extractor with different path/link handling.
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new IOException($"ZIP extraction failed: {ex.Message}", ex);
            }
        }

        // 7-Zip is required for non-ZIP formats.
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
        var tried = ext == ".zip" ? ".NET ZipFile" : "extraction";
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
