using Microsoft.Win32;
using ModInstaller.Core.Models;

namespace ModInstaller.Core.Services;

/// <summary>
/// Detects MSFS 2020 and 2024 installations by checking registry, known paths,
/// and configuration files.
/// </summary>
public class SimDetector
{
    /// <summary>
    /// Finds all MSFS installations on this machine.
    /// </summary>
    public List<SimInstallation> DetectInstallations()
    {
        var installations = new List<SimInstallation>();

        // Try each detection method, dedup by community folder path
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var install in DetectFromKnownPaths())
        {
            if (seen.Add(install.CommunityFolder))
                installations.Add(install);
        }

        foreach (var install in DetectFromRegistry())
        {
            if (seen.Add(install.CommunityFolder))
                installations.Add(install);
        }

        foreach (var install in DetectFromUserCfg())
        {
            if (seen.Add(install.CommunityFolder))
                installations.Add(install);
        }

        return installations;
    }

    /// <summary>
    /// Check standard installation paths (same approach as V2 but expanded).
    /// </summary>
    private IEnumerable<SimInstallation> DetectFromKnownPaths()
    {
        var candidates = new List<(string path, SimVersion version, InstallSource source)>
        {
            // MSFS 2024 - MS Store
            (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Packages\Microsoft.Limitless_8wekyb3d8bbwe\LocalCache\Packages\Community"),
                SimVersion.MSFS2024, InstallSource.MSStore),

            // MSFS 2024 - MS Store (alternate package name)
            (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Packages\Microsoft.FlightSimulator2024_8wekyb3d8bbwe\LocalCache\Packages\Community"),
                SimVersion.MSFS2024, InstallSource.MSStore),

            // MSFS 2020 - MS Store
            (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Packages\Microsoft.FlightSimulator_8wekyb3d8bbwe\LocalCache\Packages\Community"),
                SimVersion.MSFS2020, InstallSource.MSStore),
        };

        // Steam paths - check common drive letters
        string[] drives = ["C", "D", "E", "F", "G"];
        string[] steamLibs = [
            @"Program Files (x86)\Steam\steamapps\common",
            @"Program Files\Steam\steamapps\common",
            @"Steam\steamapps\common",
            @"SteamLibrary\steamapps\common"
        ];

        foreach (var drive in drives)
        {
            foreach (var lib in steamLibs)
            {
                // MSFS 2024 Steam
                candidates.Add((
                    Path.Combine($@"{drive}:\{lib}", "MicrosoftFlightSimulator2024", "Community"),
                    SimVersion.MSFS2024, InstallSource.Steam));

                // MSFS 2020 Steam
                candidates.Add((
                    Path.Combine($@"{drive}:\{lib}", "MicrosoftFlightSimulator", "Community"),
                    SimVersion.MSFS2020, InstallSource.Steam));
            }
        }

        foreach (var (path, version, source) in candidates)
        {
            if (Directory.Exists(path))
            {
                yield return new SimInstallation
                {
                    Version = version,
                    CommunityFolder = path,
                    Source = source,
                    InstallPath = Path.GetDirectoryName(path)
                };
            }
        }
    }

    /// <summary>
    /// Check Windows Registry for Steam install paths.
    /// </summary>
    private IEnumerable<SimInstallation> DetectFromRegistry()
    {
        // Steam stores install paths in registry
        string[] registryPaths = [
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 1250410", // MSFS 2024
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 1250410", // MSFS 2024 alt
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 314160",  // MSFS 2020
        ];

        var simVersions = new[] { SimVersion.MSFS2024, SimVersion.MSFS2024, SimVersion.MSFS2020 };

        for (int i = 0; i < registryPaths.Length; i++)
        {
            string? installLocation = null;
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(registryPaths[i]);
                installLocation = key?.GetValue("InstallLocation") as string;
            }
            catch { /* Registry access may fail */ }

            if (string.IsNullOrEmpty(installLocation)) continue;

            var communityPath = Path.Combine(installLocation, "Community");
            if (Directory.Exists(communityPath))
            {
                yield return new SimInstallation
                {
                    Version = simVersions[i],
                    CommunityFolder = communityPath,
                    Source = InstallSource.Steam,
                    InstallPath = installLocation
                };
            }
        }
    }

    /// <summary>
    /// Parse UserCfg.opt files to find custom community folder locations.
    /// The UserCfg.opt contains an InstalledPackagesPath setting.
    /// </summary>
    private List<SimInstallation> DetectFromUserCfg()
    {
        var results = new List<SimInstallation>();

        var configLocations = new List<(string cfgPath, SimVersion version)>
        {
            // MSFS 2024 MS Store
            (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Packages\Microsoft.Limitless_8wekyb3d8bbwe\LocalCache\UserCfg.opt"),
                SimVersion.MSFS2024),

            // MSFS 2020 MS Store
            (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Packages\Microsoft.FlightSimulator_8wekyb3d8bbwe\LocalCache\UserCfg.opt"),
                SimVersion.MSFS2020),

            // MSFS 2020 Steam
            (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Microsoft Flight Simulator\UserCfg.opt"),
                SimVersion.MSFS2020),

            // MSFS 2024 Steam
            (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Microsoft Flight Simulator 2024\UserCfg.opt"),
                SimVersion.MSFS2024),
        };

        foreach (var (cfgPath, version) in configLocations)
        {
            if (!File.Exists(cfgPath)) continue;

            try
            {
                var lines = File.ReadAllLines(cfgPath);
                foreach (var line in lines)
                {
                    if (!line.TrimStart().StartsWith("InstalledPackagesPath", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var parts = line.Split('"');
                    if (parts.Length < 2) continue;

                    var packagesPath = parts[1].Trim();
                    var communityPath = Path.Combine(packagesPath, "Community");

                    if (Directory.Exists(communityPath))
                    {
                        results.Add(new SimInstallation
                        {
                            Version = version,
                            CommunityFolder = communityPath,
                            Source = cfgPath.Contains("Packages") ? InstallSource.MSStore : InstallSource.Steam,
                            InstallPath = packagesPath
                        });
                    }
                }
            }
            catch { /* Config file parsing may fail */ }
        }

        return results;
    }
}
