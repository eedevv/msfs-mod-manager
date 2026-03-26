using System.Xml.Linq;
using ModInstaller.Core.Models;
using ModInstaller.Core.Utils;

namespace ModInstaller.Core.Services;

/// <summary>
/// Imports mod configurations from other MSFS mod managers
/// (MSFS Addons Linker, addon organizers, generic linker folders).
/// </summary>
public class ImportService
{
    // Well-known config locations for popular mod managers
    private static readonly (string Name, string RelativePath, string Type)[] KnownManagers =
    [
        ("MSFS Addons Linker", @"MSFS Addons Linker\userconfig.xml", "AddonLinker"),
        ("MSFS Addons Linker", @"MSFS Addons Linker\config.xml", "AddonLinker"),
        ("Addon Organizer", @"MSFS Addon Organizer\config.xml", "AddonOrganizer"),
        ("Addon Organizer", @"MSFS Addon Organizer\settings.xml", "AddonOrganizer"),
    ];

    /// <summary>
    /// Scans well-known locations for other mod manager installations.
    /// </summary>
    public List<ExternalManager> DetectOtherManagers()
    {
        var results = new List<ExternalManager>();
        var searchRoots = GetSearchRoots();

        foreach (var root in searchRoots)
        {
            foreach (var (name, relativePath, type) in KnownManagers)
            {
                var fullPath = Path.Combine(root, relativePath);
                if (File.Exists(fullPath))
                {
                    // Avoid duplicates
                    if (results.Any(r => r.ConfigPath.Equals(fullPath, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    results.Add(new ExternalManager
                    {
                        Name = name,
                        ConfigPath = fullPath,
                        Type = type
                    });
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Imports mod paths from an MSFS Addons Linker XML configuration file.
    /// The XML typically contains elements with source folder paths that are linked into Community.
    /// </summary>
    public async Task<ImportResult> ImportFromAddonLinkerAsync(string configPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);

        var result = new ImportResult();

        if (!File.Exists(configPath))
        {
            result.Errors.Add($"Config file not found: {configPath}");
            return result;
        }

        try
        {
            var xml = await File.ReadAllTextAsync(configPath, cancellationToken).ConfigureAwait(false);
            var doc = XDocument.Parse(xml);

            // MSFS Addons Linker stores linked folders in various XML structures.
            // Common patterns: <Addon Path="..." />, <Link Source="..." />, <Entry>path</Entry>
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Strategy 1: attributes named Path, Source, or Directory
            foreach (var element in doc.Descendants())
            {
                cancellationToken.ThrowIfCancellationRequested();

                foreach (var attr in element.Attributes())
                {
                    if (attr.Name.LocalName.Equals("Path", StringComparison.OrdinalIgnoreCase) ||
                        attr.Name.LocalName.Equals("Source", StringComparison.OrdinalIgnoreCase) ||
                        attr.Name.LocalName.Equals("Directory", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrWhiteSpace(attr.Value))
                            paths.Add(attr.Value.Trim());
                    }
                }

                // Strategy 2: element text that looks like a directory path
                if (!element.HasElements && !string.IsNullOrWhiteSpace(element.Value))
                {
                    var value = element.Value.Trim();
                    if (LooksLikeDirectoryPath(value))
                        paths.Add(value);
                }
            }

            foreach (var path in paths)
            {
                if (Directory.Exists(path))
                {
                    result.ImportedCount++;
                }
                else
                {
                    result.SkippedCount++;
                    result.Errors.Add($"Path does not exist: {path}");
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            result.Errors.Add($"Failed to parse Addon Linker config: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Imports mod paths from a generic addon organizer XML configuration file.
    /// </summary>
    public async Task<ImportResult> ImportFromAddonOrganizerAsync(string configPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);

        var result = new ImportResult();

        if (!File.Exists(configPath))
        {
            result.Errors.Add($"Config file not found: {configPath}");
            return result;
        }

        try
        {
            var xml = await File.ReadAllTextAsync(configPath, cancellationToken).ConfigureAwait(false);
            var doc = XDocument.Parse(xml);

            // Addon organizers typically store mod entries with path and enabled/disabled state
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var element in doc.Descendants())
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Look for elements with path-like attributes or text content
                foreach (var attr in element.Attributes())
                {
                    if (IsPathAttribute(attr.Name.LocalName) && !string.IsNullOrWhiteSpace(attr.Value))
                        paths.Add(attr.Value.Trim());
                }

                // Check element value for directory paths
                if (!element.HasElements && !string.IsNullOrWhiteSpace(element.Value))
                {
                    var value = element.Value.Trim();
                    if (LooksLikeDirectoryPath(value))
                        paths.Add(value);
                }
            }

            foreach (var path in paths)
            {
                if (Directory.Exists(path))
                {
                    result.ImportedCount++;
                }
                else
                {
                    result.SkippedCount++;
                    result.Errors.Add($"Path does not exist: {path}");
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            result.Errors.Add($"Failed to parse Addon Organizer config: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Imports mod references by reading junction/symlink targets from a linker-managed folder.
    /// Each subdirectory that is a junction or symlink is resolved to its target.
    /// </summary>
    public Task<ImportResult> ImportFromLinkerAsync(string lnkFolder, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lnkFolder);

        var result = new ImportResult();

        if (!Directory.Exists(lnkFolder))
        {
            result.Errors.Add($"Linker folder does not exist: {lnkFolder}");
            return Task.FromResult(result);
        }

        try
        {
            var subdirectories = Directory.GetDirectories(lnkFolder);

            foreach (var subdir in subdirectories)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    // Check if the directory is a junction or symlink
                    var dirInfo = new DirectoryInfo(subdir);
                    if (!dirInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    {
                        result.SkippedCount++;
                        continue;
                    }

                    // Resolve the target
                    var target = JunctionHelper.GetJunctionTarget(subdir);
                    if (target == null)
                    {
                        // Try .NET native resolution as fallback
                        var linkTarget = dirInfo.ResolveLinkTarget(returnFinalTarget: true);
                        target = linkTarget?.FullName;
                    }

                    if (!string.IsNullOrEmpty(target) && Directory.Exists(target))
                    {
                        result.ImportedCount++;
                    }
                    else
                    {
                        result.SkippedCount++;
                        result.Errors.Add($"Broken link: {Path.GetFileName(subdir)} -> {target ?? "(unresolvable)"}");
                    }
                }
                catch (Exception ex)
                {
                    result.SkippedCount++;
                    result.Errors.Add($"Error reading link '{Path.GetFileName(subdir)}': {ex.Message}");
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            result.Errors.Add($"Failed to scan linker folder: {ex.Message}");
        }

        return Task.FromResult(result);
    }

    private static List<string> GetSearchRoots()
    {
        var roots = new List<string>();

        // AppData\Local and AppData\Roaming
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrEmpty(localAppData))
            roots.Add(localAppData);

        var roamingAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrEmpty(roamingAppData))
            roots.Add(roamingAppData);

        // Program Files
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrEmpty(programFiles))
            roots.Add(programFiles);

        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrEmpty(programFilesX86))
            roots.Add(programFilesX86);

        // User profile (Documents, Desktop)
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (!string.IsNullOrEmpty(documents))
            roots.Add(documents);

        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        if (!string.IsNullOrEmpty(desktop))
            roots.Add(desktop);

        return roots;
    }

    private static bool IsPathAttribute(string name)
    {
        return name.Equals("Path", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Source", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Directory", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Folder", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Location", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeDirectoryPath(string value)
    {
        if (value.Length < 3) return false;

        // Windows absolute path (e.g., C:\...)
        if (value.Length >= 3 && char.IsLetter(value[0]) && value[1] == ':' && (value[2] == '\\' || value[2] == '/'))
            return true;

        // UNC path
        if (value.StartsWith(@"\\", StringComparison.Ordinal))
            return true;

        return false;
    }
}
