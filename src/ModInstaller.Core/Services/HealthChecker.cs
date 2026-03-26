using System.Text.Json;
using ModInstaller.Core.Models;

namespace ModInstaller.Core.Services;

/// <summary>
/// Validates mod integrity — checks structure, manifest validity, content types, and permissions.
/// </summary>
public class HealthChecker
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    /// <summary>
    /// Runs a full health check on a mod.
    /// </summary>
    public async Task<HealthReport> CheckModHealthAsync(Mod mod)
    {
        var report = new HealthReport { ModName = mod.FolderName };

        if (!Directory.Exists(mod.FullPath))
        {
            report.Status = HealthStatus.Critical;
            report.Issues.Add(new HealthIssue
            {
                Message = "Mod directory does not exist",
                Severity = HealthStatus.Critical
            });
            return report;
        }

        // Check for empty mod
        await CheckEmpty(mod, report);

        // Check manifest.json
        await CheckManifest(mod, report);

        // Check layout.json
        await CheckLayout(mod, report);

        // Type-specific checks
        await CheckTypeSpecific(mod, report);

        // Check permissions
        CheckPermissions(mod, report);

        // Check for suspicious files
        CheckSuspiciousFiles(mod, report);

        // Set overall status
        if (report.Issues.Any(i => i.Severity == HealthStatus.Critical))
            report.Status = HealthStatus.Critical;
        else if (report.Issues.Any(i => i.Severity == HealthStatus.Warning))
            report.Status = HealthStatus.Warning;
        else
            report.Status = HealthStatus.Healthy;

        return report;
    }

    /// <summary>
    /// Runs health checks on all mods.
    /// </summary>
    public async Task<List<HealthReport>> CheckAllModsAsync(
        List<Mod> mods,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var reports = new List<HealthReport>();
        int checked_ = 0;

        foreach (var mod in mods)
        {
            cancellationToken.ThrowIfCancellationRequested();
            checked_++;
            progress?.Report($"Checking {mod.FolderName} ({checked_}/{mods.Count})...");

            var report = await CheckModHealthAsync(mod);
            reports.Add(report);
        }

        return reports;
    }

    private static Task CheckEmpty(Mod mod, HealthReport report)
    {
        try
        {
            var files = Directory.GetFiles(mod.FullPath, "*", SearchOption.AllDirectories);
            if (files.Length == 0)
            {
                report.Issues.Add(new HealthIssue
                {
                    Message = "Mod folder is empty — no files found",
                    Severity = HealthStatus.Critical
                });
            }
        }
        catch (Exception ex)
        {
            report.Issues.Add(new HealthIssue
            {
                Message = $"Cannot read mod directory: {ex.Message}",
                Severity = HealthStatus.Critical
            });
        }

        return Task.CompletedTask;
    }

    private static async Task CheckManifest(Mod mod, HealthReport report)
    {
        var manifestPath = Path.Combine(mod.FullPath, "manifest.json");

        if (!File.Exists(manifestPath))
        {
            report.Issues.Add(new HealthIssue
            {
                Message = "Missing manifest.json — mod may not load correctly",
                Severity = HealthStatus.Warning
            });
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(manifestPath);
            var manifest = JsonSerializer.Deserialize<ModManifest>(json, JsonOptions);

            if (manifest == null)
            {
                report.Issues.Add(new HealthIssue
                {
                    Message = "manifest.json is empty or invalid",
                    Severity = HealthStatus.Critical
                });
                return;
            }

            // Check required fields
            if (string.IsNullOrWhiteSpace(manifest.ContentType))
            {
                report.Issues.Add(new HealthIssue
                {
                    Message = "manifest.json missing content_type field",
                    Severity = HealthStatus.Warning
                });
            }

            if (string.IsNullOrWhiteSpace(manifest.Title))
            {
                report.Issues.Add(new HealthIssue
                {
                    Message = "manifest.json missing title field",
                    Severity = HealthStatus.Warning
                });
            }

            // Check minimum_game_version for compatibility
            if (!string.IsNullOrEmpty(manifest.MinimumGameVersion))
            {
                if (Version.TryParse(manifest.MinimumGameVersion, out var minVersion))
                {
                    // MSFS 2024 is 1.0+, MSFS 2020 SU15 is ~1.37
                    // Flag if mod requires a very old version
                    if (minVersion.Major == 0)
                    {
                        report.Issues.Add(new HealthIssue
                        {
                            Message = $"Mod targets very old game version: {manifest.MinimumGameVersion}",
                            Severity = HealthStatus.Warning
                        });
                    }
                }
            }
        }
        catch (JsonException ex)
        {
            report.Issues.Add(new HealthIssue
            {
                Message = $"manifest.json has invalid JSON: {ex.Message}",
                Severity = HealthStatus.Critical
            });
        }
    }

    private static async Task CheckLayout(Mod mod, HealthReport report)
    {
        var layoutPath = Path.Combine(mod.FullPath, "layout.json");

        if (!File.Exists(layoutPath))
        {
            report.Issues.Add(new HealthIssue
            {
                Message = "Missing layout.json — sim may not index all files",
                Severity = HealthStatus.Warning
            });
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(layoutPath);
            var layout = JsonSerializer.Deserialize<ModLayout>(json, JsonOptions);

            if (layout?.Content == null || layout.Content.Count == 0)
            {
                report.Issues.Add(new HealthIssue
                {
                    Message = "layout.json has no content entries",
                    Severity = HealthStatus.Warning
                });
                return;
            }

            // Check if layout entries match actual files
            int missing = 0;
            foreach (var entry in layout.Content)
            {
                if (entry.Path == null) continue;
                var filePath = Path.Combine(mod.FullPath, entry.Path.Replace('/', '\\'));
                if (!File.Exists(filePath))
                    missing++;
            }

            if (missing > 0)
            {
                report.Issues.Add(new HealthIssue
                {
                    Message = $"layout.json references {missing} missing file(s) — mod may be incomplete",
                    Severity = missing > layout.Content.Count / 2 ? HealthStatus.Critical : HealthStatus.Warning
                });
            }
        }
        catch (JsonException)
        {
            report.Issues.Add(new HealthIssue
            {
                Message = "layout.json has invalid JSON",
                Severity = HealthStatus.Warning
            });
        }
    }

    private static Task CheckTypeSpecific(Mod mod, HealthReport report)
    {
        switch (mod.Type)
        {
            case ModType.Aircraft:
            case ModType.Helicopter:
                // Aircraft should have aircraft.cfg
                var cfgFiles = Directory.GetFiles(mod.FullPath, "aircraft.cfg", SearchOption.AllDirectories);
                if (cfgFiles.Length == 0)
                {
                    report.Issues.Add(new HealthIssue
                    {
                        Message = "Aircraft mod missing aircraft.cfg",
                        Severity = HealthStatus.Warning
                    });
                }
                break;

            case ModType.Scenery:
                // Scenery should have BGL files
                var bglFiles = Directory.GetFiles(mod.FullPath, "*.bgl", SearchOption.AllDirectories);
                if (bglFiles.Length == 0)
                {
                    report.Issues.Add(new HealthIssue
                    {
                        Message = "Scenery mod has no BGL files",
                        Severity = HealthStatus.Warning
                    });
                }
                break;
        }

        return Task.CompletedTask;
    }

    private static void CheckPermissions(Mod mod, HealthReport report)
    {
        try
        {
            // Try creating a temp file to test write access
            var testFile = Path.Combine(mod.FullPath, $".permission_test_{Guid.NewGuid():N}");
            File.WriteAllText(testFile, "test");
            File.Delete(testFile);
        }
        catch
        {
            report.Issues.Add(new HealthIssue
            {
                Message = "No write permission to mod folder — may cause issues",
                Severity = HealthStatus.Warning
            });
        }
    }

    private static void CheckSuspiciousFiles(Mod mod, HealthReport report)
    {
        string[] suspiciousExtensions = [".exe", ".dll", ".bat", ".cmd", ".ps1", ".vbs"];

        try
        {
            foreach (var ext in suspiciousExtensions)
            {
                var files = Directory.GetFiles(mod.FullPath, $"*{ext}", SearchOption.AllDirectories);
                if (files.Length > 0)
                {
                    // DLLs in WASM modules are expected
                    if (ext == ".dll" && files.All(f => f.Contains("wasm", StringComparison.OrdinalIgnoreCase)))
                        continue;

                    report.Issues.Add(new HealthIssue
                    {
                        Message = $"Contains {files.Length} {ext} file(s) — review for safety",
                        Severity = ext is ".exe" or ".bat" or ".cmd" ? HealthStatus.Warning : HealthStatus.Healthy
                    });
                }
            }
        }
        catch { }
    }
}
