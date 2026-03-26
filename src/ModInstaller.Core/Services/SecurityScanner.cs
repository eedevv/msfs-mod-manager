using System.Security.Cryptography;

namespace ModInstaller.Core.Services;

/// <summary>
/// Security scanner for mod files — checks for suspicious content,
/// verifies file integrity, and validates archives before installation.
/// </summary>
public class SecurityScanner
{
    // Known dangerous file extensions inside mod archives
    private static readonly HashSet<string> DangerousExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".bat", ".cmd", ".ps1", ".vbs", ".vbe", ".js", ".jse",
        ".wsf", ".wsh", ".msi", ".scr", ".pif", ".com", ".hta",
        ".cpl", ".inf", ".reg"
    };

    // Extensions that are safe/expected in MSFS mods
    private static readonly HashSet<string> SafeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".json", ".cfg", ".xml", ".bgl", ".dds", ".png", ".jpg", ".jpeg",
        ".tga", ".bmp", ".wav", ".ogg", ".fsb", ".spb", ".gltf", ".glb",
        ".bin", ".html", ".css", ".txt", ".pdf", ".flt", ".pln",
        ".loc", ".locpak", ".wasm"
    };

    // DLLs are only safe inside WASM module folders
    private static readonly string[] WasmPaths = ["wasm", "WASM", "Wasm"];

    /// <summary>
    /// Scans a directory for security issues before installing as a mod.
    /// Returns a report with findings.
    /// </summary>
    public SecurityReport ScanDirectory(string path)
    {
        var report = new SecurityReport { Path = path };

        if (!Directory.Exists(path))
        {
            report.Issues.Add(new SecurityIssue
            {
                Severity = SecuritySeverity.Critical,
                Message = "Directory does not exist",
                FilePath = path
            });
            return report;
        }

        try
        {
            var files = Directory.GetFiles(path, "*", SearchOption.AllDirectories);
            report.TotalFiles = files.Length;

            foreach (var file in files)
            {
                var ext = Path.GetExtension(file);
                var relativePath = Path.GetRelativePath(path, file);

                // Check for dangerous extensions
                if (DangerousExtensions.Contains(ext))
                {
                    report.Issues.Add(new SecurityIssue
                    {
                        Severity = SecuritySeverity.High,
                        Message = $"Potentially dangerous file type: {ext}",
                        FilePath = relativePath,
                        Recommendation = "This file type should not be in an MSFS mod. Consider removing it."
                    });
                }

                // Check DLLs — only safe inside WASM folders
                if (ext.Equals(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    bool inWasm = WasmPaths.Any(w => relativePath.Contains(w, StringComparison.OrdinalIgnoreCase));
                    if (!inWasm)
                    {
                        report.Issues.Add(new SecurityIssue
                        {
                            Severity = SecuritySeverity.Medium,
                            Message = "DLL file found outside WASM module folder",
                            FilePath = relativePath,
                            Recommendation = "DLLs are expected inside WASM/ folders. This may be suspicious."
                        });
                    }
                }

                // Check for hidden files
                var attr = File.GetAttributes(file);
                if (attr.HasFlag(FileAttributes.Hidden))
                {
                    report.Issues.Add(new SecurityIssue
                    {
                        Severity = SecuritySeverity.Low,
                        Message = "Hidden file detected",
                        FilePath = relativePath
                    });
                }

                // Check for extremely large files (> 2GB single file is suspicious)
                var fileInfo = new FileInfo(file);
                if (fileInfo.Length > 2L * 1024 * 1024 * 1024)
                {
                    report.Issues.Add(new SecurityIssue
                    {
                        Severity = SecuritySeverity.Medium,
                        Message = $"Very large file ({fileInfo.Length / (1024.0 * 1024 * 1024):F1} GB)",
                        FilePath = relativePath,
                        Recommendation = "File is unusually large for an MSFS mod component."
                    });
                }

                // Check for path traversal attempts in file names
                if (relativePath.Contains("..") || relativePath.Contains("~"))
                {
                    report.Issues.Add(new SecurityIssue
                    {
                        Severity = SecuritySeverity.Critical,
                        Message = "Path traversal attempt detected",
                        FilePath = relativePath,
                        Recommendation = "This file attempts to escape the mod directory. Do not install."
                    });
                }
            }

            // Check manifest.json for suspicious content
            var manifestPath = Path.Combine(path, "manifest.json");
            if (File.Exists(manifestPath))
            {
                var content = File.ReadAllText(manifestPath);
                if (content.Contains("http://") || content.Contains("https://"))
                {
                    // URLs in manifest are unusual
                    report.Issues.Add(new SecurityIssue
                    {
                        Severity = SecuritySeverity.Low,
                        Message = "manifest.json contains URLs",
                        FilePath = "manifest.json",
                        Recommendation = "URLs in manifest are unusual but not necessarily malicious."
                    });
                }
            }
        }
        catch (Exception ex)
        {
            report.Issues.Add(new SecurityIssue
            {
                Severity = SecuritySeverity.Medium,
                Message = $"Scan error: {ex.Message}",
                FilePath = path
            });
        }

        report.IsSafe = !report.Issues.Any(i => i.Severity >= SecuritySeverity.High);
        return report;
    }

    /// <summary>
    /// Computes SHA256 hash of a file for integrity verification.
    /// </summary>
    public static async Task<string> ComputeFileHashAsync(string filePath, CancellationToken ct = default)
    {
        using var sha = SHA256.Create();
        await using var stream = File.OpenRead(filePath);
        var hash = await sha.ComputeHashAsync(stream, ct);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    /// <summary>
    /// Validates a ZIP archive is not corrupted before extraction.
    /// </summary>
    public static bool ValidateArchive(string archivePath)
    {
        try
        {
            if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                using var archive = System.IO.Compression.ZipFile.OpenRead(archivePath);
                // Try reading each entry to verify integrity
                foreach (var entry in archive.Entries)
                {
                    using var stream = entry.Open();
                    // Read a byte to verify the entry is accessible
                    stream.ReadByte();
                }
                return true;
            }
            // For non-zip, just check the file exists and has content
            return new FileInfo(archivePath).Length > 0;
        }
        catch
        {
            return false;
        }
    }
}

public class SecurityReport
{
    public string Path { get; set; } = "";
    public int TotalFiles { get; set; }
    public bool IsSafe { get; set; } = true;
    public List<SecurityIssue> Issues { get; set; } = [];

    public int HighCount => Issues.Count(i => i.Severity >= SecuritySeverity.High);
    public int MediumCount => Issues.Count(i => i.Severity == SecuritySeverity.Medium);
    public int LowCount => Issues.Count(i => i.Severity == SecuritySeverity.Low);
}

public class SecurityIssue
{
    public SecuritySeverity Severity { get; set; }
    public string Message { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string? Recommendation { get; set; }
}

public enum SecuritySeverity
{
    Low,
    Medium,
    High,
    Critical
}
