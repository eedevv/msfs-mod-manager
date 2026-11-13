namespace ModInstaller.Core.Models;

public class AppSettings
{
    public string? CustomCommunityFolder2020 { get; set; }
    public string? CustomCommunityFolder2024 { get; set; }
    public bool UseCustomFolder { get; set; }

    public string? GetCustomCommunityFolder(SimVersion version) => version switch
    {
        SimVersion.MSFS2020 => CustomCommunityFolder2020,
        SimVersion.MSFS2024 => CustomCommunityFolder2024,
        _ => null
    };

    public void SetCustomCommunityFolder(SimVersion version, string? path)
    {
        switch (version)
        {
            case SimVersion.MSFS2020: CustomCommunityFolder2020 = path; break;
            case SimVersion.MSFS2024: CustomCommunityFolder2024 = path; break;
            default: throw new ArgumentOutOfRangeException(nameof(version));
        }
    }

    // A configured custom path takes precedence even when missing: never silently
    // install into a different folder if a drive has been disconnected.
    public string ResolveCommunityFolder(SimVersion version, string? detectedFolder)
    {
        var custom = GetCustomCommunityFolder(version);
        return UseCustomFolder && !string.IsNullOrWhiteSpace(custom)
            ? custom : detectedFolder ?? "";
    }

    public string BackupDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MSFS_Mod_Backups");
    public string ModLibraryDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MSFS_ModLibrary");
    public bool UseJunctions { get; set; } = true;
    public string Theme { get; set; } = "Dark";
    public string Language { get; set; } = "en";
    public SimVersion ActiveSim { get; set; } = SimVersion.MSFS2024;
    public bool CheckUpdatesOnStartup { get; set; } = true;
    public DateTime? LastUpdateCheck { get; set; }

    // Watch folder
    public bool WatchFolderEnabled { get; set; }
    public string WatchFolderPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    // System tray
    public bool MinimizeToTray { get; set; }

    // Window state (-1 = not set / use default centering)
    public double WindowLeft { get; set; } = -1;
    public double WindowTop { get; set; } = -1;
    public double WindowWidth { get; set; } = 1340;
    public double WindowHeight { get; set; } = 760;
    public bool WindowMaximized { get; set; }

    // Sorting memory
    public string? SortColumn { get; set; }
    public bool SortDescending { get; set; }

    // First-time + usage
    public bool HasCompletedTutorial { get; set; }
    public bool HasMigratedV2Profiles { get; set; }
    public int LaunchCount { get; set; }

    // Mod notes (folder name → note text)
    public Dictionary<string, string> ModNotes { get; set; } = new();

    // Warnings
    public long CommunitySizeWarningThresholdBytes { get; set; } = 50L * 1024 * 1024 * 1024; // 50 GB

    public static string SettingsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MSFS_ModManager");
    public static string SettingsFilePath => Path.Combine(SettingsDirectory, "settings.json");
    public static string DatabaseFilePath => Path.Combine(SettingsDirectory, "modmanager.db");
    public static string DisabledModsDirectory => Path.Combine(SettingsDirectory, "DisabledMods");
    public static string ProfilesDirectory => Path.Combine(SettingsDirectory, "Profiles");
    public static string GroupsFilePath => Path.Combine(SettingsDirectory, "groups.json");
    public static string UpdateCacheFilePath => Path.Combine(SettingsDirectory, "update_cache.json");
}
