using System.Security.Cryptography;
using System.Text;
using ModInstaller.Core.Models;

namespace ModInstaller.Core.Services;

/// <summary>Stores disabled mods separately for each physical Community folder.</summary>
public sealed class DisabledModStorage
{
    private readonly string _scopedRoot;
    private readonly string _legacyRoot;

    public DisabledModStorage(string? scopedRoot = null, string? legacyRoot = null)
    {
        _scopedRoot = scopedRoot ?? Path.Combine(AppSettings.SettingsDirectory, "DisabledModsByCommunity");
        _legacyRoot = legacyRoot ?? AppSettings.DisabledModsDirectory;
    }

    public static string GetCommunityKey(string communityFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(communityFolder);
        var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(communityFolder)).ToUpperInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }

    public string GetDirectory(string communityFolder) => Path.Combine(_scopedRoot, GetCommunityKey(communityFolder));

    public string GetLibraryDirectory(string libraryRoot, string communityFolder) =>
        Path.Combine(libraryRoot, "ByCommunity", Path.GetFileName(GetDirectory(communityFolder)));

    public IReadOnlyList<string> GetLegacyDirectories() => Directory.Exists(_legacyRoot)
        ? Directory.GetDirectories(_legacyRoot) : [];

    /// <summary>
    /// Explicitly assigns legacy mods to a selected Community folder. Conflicts and
    /// failures are left in the legacy directory and reported, never overwritten.
    /// </summary>
    public (int Moved, List<string> Errors) MigrateLegacy(string communityFolder)
    {
        if (!Directory.Exists(communityFolder))
            throw new DirectoryNotFoundException("The selected Community folder is unavailable.");
        var destinationRoot = GetDirectory(communityFolder);
        Directory.CreateDirectory(destinationRoot);
        int moved = 0;
        var errors = new List<string>();
        foreach (var source in GetLegacyDirectories())
        {
            var name = Path.GetFileName(source);
            var destination = Path.Combine(destinationRoot, name);
            try
            {
                if (Directory.Exists(destination) || File.Exists(destination) ||
                    Directory.Exists(Path.Combine(communityFolder, name)) || File.Exists(Path.Combine(communityFolder, name)))
                    throw new IOException("A mod with this name already exists in the selected installation.");
                Directory.Move(source, destination);
                moved++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{name}: {ex.Message}");
            }
        }
        return (moved, errors);
    }
}
