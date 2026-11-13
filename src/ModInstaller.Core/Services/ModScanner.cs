using System.Text.Json;
using ModInstaller.Core.Models;
using ModInstaller.Core.Utils;

namespace ModInstaller.Core.Services;

/// <summary>
/// Scans a Community folder and parses mod metadata from manifest.json and layout.json files.
/// </summary>
public class ModScanner
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    /// <summary>
    /// Scans a Community folder and returns all detected mods.
    /// </summary>
    public async Task<List<Mod>> ScanCommunityFolderAsync(
        string communityFolder,
        SimVersion simVersion = SimVersion.Unknown,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var mods = new List<Mod>();

        if (!Directory.Exists(communityFolder))
            return mods;

        var directories = Directory.GetDirectories(communityFolder);

        foreach (var dir in directories)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var folderName = Path.GetFileName(dir);
            if (folderName.StartsWith(".msfsmm-stage-", StringComparison.OrdinalIgnoreCase) ||
                folderName.StartsWith(".msfsmm-restore-", StringComparison.OrdinalIgnoreCase))
                continue;
            progress?.Report($"Scanning {folderName}...");

            var mod = await ScanModDirectoryAsync(dir, simVersion);
            if (mod != null)
                mods.Add(mod);
        }

        return mods;
    }

    /// <summary>
    /// Scans a single mod directory and extracts metadata.
    /// </summary>
    public async Task<Mod?> ScanModDirectoryAsync(string modPath, SimVersion simVersion = SimVersion.Unknown)
    {
        if (!Directory.Exists(modPath))
            return null;

        var folderName = Path.GetFileName(modPath);
        var isJunction = JunctionHelper.IsJunction(modPath);

        var mod = new Mod
        {
            FolderName = folderName,
            FullPath = modPath,
            SimVersion = simVersion,
            LinkType = isJunction ? LinkType.Junction : LinkType.Copy,
            LibraryPath = isJunction ? JunctionHelper.GetJunctionTarget(modPath) : null,
            LastModified = Directory.GetLastWriteTime(modPath)
        };

        // Parse manifest.json
        var manifestPath = Path.Combine(modPath, "manifest.json");
        if (File.Exists(manifestPath))
        {
            mod.HasManifest = true;
            try
            {
                var json = await File.ReadAllTextAsync(manifestPath);
                var manifest = JsonSerializer.Deserialize<ModManifest>(json, JsonOptions);
                if (manifest != null)
                {
                    mod.Title = manifest.Title;
                    mod.Creator = manifest.Creator ?? manifest.Manufacturer;
                    mod.Version = manifest.PackageVersion;
                    mod.MinGameVersion = manifest.MinimumGameVersion;
                    mod.ContentType = manifest.ContentType;
                    mod.Type = DetectModType(manifest, modPath);
                    mod.Dependencies = manifest.Dependencies?
                        .Where(d => d.Name != null)
                        .Select(d => d.Name!)
                        .ToList() ?? [];
                }
            }
            catch (JsonException)
            {
                mod.Health = HealthStatus.Warning;
                mod.HealthIssues.Add("manifest.json contains invalid JSON");
            }
        }

        // Parse layout.json
        var layoutPath = Path.Combine(modPath, "layout.json");
        if (File.Exists(layoutPath))
        {
            mod.HasLayout = true;
            try
            {
                var json = await File.ReadAllTextAsync(layoutPath);
                var layout = JsonSerializer.Deserialize<ModLayout>(json, JsonOptions);
                if (layout?.Content != null)
                {
                    mod.FileCount = layout.Content.Count;
                    mod.SizeBytes = layout.Content.Sum(e => e.Size);
                }
            }
            catch (JsonException)
            {
                // Fall back to filesystem size
            }
        }

        // Fallback: calculate size from filesystem if layout.json didn't provide it
        if (mod.SizeBytes == 0)
        {
            mod.SizeBytes = FileOperations.GetDirectorySize(modPath);
            mod.FileCount = FileOperations.GetFileCount(modPath);
        }

        // Fallback: detect type from folder structure if manifest didn't provide it
        if (mod.Type == ModType.Unknown)
        {
            mod.Type = DetectModTypeFromStructure(modPath);
        }

        // Basic health checks
        if (!mod.HasManifest)
        {
            mod.Health = HealthStatus.Warning;
            mod.HealthIssues.Add("Missing manifest.json");
        }

        if (mod.SizeBytes == 0)
        {
            mod.Health = HealthStatus.Critical;
            mod.HealthIssues.Add("Mod folder is empty");
        }

        return mod;
    }

    /// <summary>
    /// Detects mod type from manifest content_type field.
    /// </summary>
    private static ModType DetectModType(ModManifest manifest, string modPath)
    {
        var contentType = manifest.ContentType?.ToLowerInvariant();

        return contentType switch
        {
            "AIRCRAFT" or "aircraft" => ModType.Aircraft,
            "SCENERY" or "scenery" => ModType.Scenery,
            "LIVERY" or "livery" => ModType.Livery,
            "instrument" or "instruments" => ModType.Instruments,
            "MISSION" or "missions" or "mission" => ModType.Missions,
            _ => DetectModTypeFromStructure(modPath)
        };
    }

    /// <summary>
    /// Detects mod type by examining the folder structure (fallback).
    /// Matches the V2 batch logic but with more precision.
    /// </summary>
    private static ModType DetectModTypeFromStructure(string modPath)
    {
        if (Directory.Exists(Path.Combine(modPath, "SimObjects", "Airplanes")))
            return ModType.Aircraft;
        if (Directory.Exists(Path.Combine(modPath, "SimObjects", "Rotorcraft")))
            return ModType.Helicopter;
        if (Directory.Exists(Path.Combine(modPath, "scenery")))
            return ModType.Scenery;
        if (Directory.Exists(Path.Combine(modPath, "html_ui")))
            return ModType.Avionics;
        if (Directory.Exists(Path.Combine(modPath, "Effects")))
            return ModType.Effects;
        if (Directory.Exists(Path.Combine(modPath, "ModelBehaviorDefs")))
            return ModType.Models;
        if (Directory.Exists(Path.Combine(modPath, "missions")))
            return ModType.Missions;

        // Check if it's a livery (has SimObjects but also texture files)
        if (Directory.Exists(Path.Combine(modPath, "SimObjects")))
        {
            var textureFiles = Directory.GetFiles(modPath, "texture.*", SearchOption.AllDirectories);
            if (textureFiles.Length > 0)
                return ModType.Livery;
            return ModType.Aircraft;
        }

        return ModType.Other;
    }

    /// <summary>
    /// Validates whether a directory looks like a valid MSFS mod.
    /// </summary>
    public static bool IsValidModDirectory(string path)
    {
        if (!Directory.Exists(path)) return false;

        // Must have manifest.json or layout.json, or recognized folder structure
        if (File.Exists(Path.Combine(path, "manifest.json"))) return true;
        if (File.Exists(Path.Combine(path, "layout.json"))) return true;
        if (Directory.Exists(Path.Combine(path, "SimObjects"))) return true;
        if (Directory.Exists(Path.Combine(path, "html_ui"))) return true;
        if (Directory.Exists(Path.Combine(path, "scenery"))) return true;
        if (Directory.Exists(Path.Combine(path, "Effects"))) return true;

        return false;
    }

    /// <summary>
    /// Finds the mod root directory inside an extracted archive.
    /// Searches up to 4 levels deep. Handles common packaging patterns:
    /// - mod files directly in ZIP root
    /// - single folder containing the mod
    /// - nested folders (author/mod/content)
    /// </summary>
    public static string? FindModRootInExtractedArchive(string extractedPath) =>
        FindModRootsInExtractedArchive(extractedPath).FirstOrDefault();

    /// <summary>Find independent package roots, without descending into an already found package.</summary>
    public static List<string> FindModRootsInExtractedArchive(string extractedPath)
    {
        var roots = new List<string>();
        if (!Directory.Exists(extractedPath)) return roots;
        var queue = new Queue<(string Path, int Depth)>();
        queue.Enqueue((extractedPath, 0));
        while (queue.Count > 0)
        {
            var (path, depth) = queue.Dequeue();
            if (IsValidModDirectory(path))
            {
                roots.Add(path);
                continue;
            }
            if (depth >= 4) continue;
            foreach (var child in Directory.GetDirectories(path))
                queue.Enqueue((child, depth + 1));
        }
        return roots;
    }

}
