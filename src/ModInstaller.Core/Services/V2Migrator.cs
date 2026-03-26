using ModInstaller.Core.Database;
using ModInstaller.Core.Models;

namespace ModInstaller.Core.Services;

/// <summary>
/// Imports settings and profiles from the V2 batch tool.
/// Detects existing V2 config at %APPDATA%\MSFS_ModInstaller\.
/// </summary>
public class V2Migrator
{
    private static readonly string V2ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MSFS_ModInstaller");

    public bool HasV2Data => Directory.Exists(V2ConfigDir);

    /// <summary>
    /// Imports V2 settings into the new AppSettings.
    /// </summary>
    public AppSettings? ImportSettings()
    {
        var configPath = Path.Combine(V2ConfigDir, "config.ini");
        if (!File.Exists(configPath)) return null;

        var settings = new AppSettings();

        try
        {
            foreach (var line in File.ReadAllLines(configPath))
            {
                var parts = line.Split('=', 2);
                if (parts.Length != 2) continue;

                var key = parts[0].Trim();
                var value = parts[1].Trim();

                switch (key)
                {
                    case "USE_CUSTOM_DIR":
                        settings.UseCustomFolder = value.Equals("yes", StringComparison.OrdinalIgnoreCase);
                        break;
                    case "CUSTOM_INSTALL_DIR":
                        if (!string.IsNullOrEmpty(value))
                            settings.CustomCommunityFolder2024 = value;
                        break;
                }
            }
        }
        catch { return null; }

        return settings;
    }

    /// <summary>
    /// Imports V2 profiles into the database.
    /// </summary>
    public int ImportProfiles(ModDatabase db)
    {
        var profilesDir = Path.Combine(V2ConfigDir, "profiles");
        if (!Directory.Exists(profilesDir)) return 0;

        int imported = 0;

        foreach (var profileFile in Directory.GetFiles(profilesDir, "*.profile"))
        {
            try
            {
                var lines = File.ReadAllLines(profileFile);
                var profile = new ModProfile
                {
                    Name = Path.GetFileNameWithoutExtension(profileFile),
                    Description = "Imported from V2"
                };

                string section = "";
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (trimmed == "[ENABLED]") { section = "enabled"; continue; }
                    if (trimmed == "[DISABLED]") { section = "disabled"; continue; }
                    if (string.IsNullOrEmpty(trimmed)) continue;

                    if (section == "enabled") profile.EnabledMods.Add(trimmed);
                    else if (section == "disabled") profile.DisabledMods.Add(trimmed);
                }

                db.SaveProfile(profile);
                imported++;
            }
            catch { /* Skip invalid profiles */ }
        }

        return imported;
    }

    /// <summary>
    /// Imports disabled mods list from V2.
    /// </summary>
    public List<string> GetV2DisabledMods()
    {
        var disabledDir = Path.Combine(V2ConfigDir, "MSFS_Disabled_Mods");
        if (!Directory.Exists(disabledDir)) return [];

        return Directory.GetDirectories(disabledDir)
            .Select(Path.GetFileName)
            .Where(n => n != null)
            .Cast<string>()
            .ToList();
    }
}
