using System.Text.Json.Serialization;

namespace ModInstaller.Core.Models;

/// <summary>
/// Represents a parsed MSFS manifest.json file.
/// </summary>
public class ModManifest
{
    [JsonPropertyName("dependencies")]
    public List<ManifestDependency>? Dependencies { get; set; }

    [JsonPropertyName("content_type")]
    public string? ContentType { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("manufacturer")]
    public string? Manufacturer { get; set; }

    [JsonPropertyName("creator")]
    public string? Creator { get; set; }

    [JsonPropertyName("package_version")]
    public string? PackageVersion { get; set; }

    [JsonPropertyName("minimum_game_version")]
    public string? MinimumGameVersion { get; set; }

    [JsonPropertyName("release_notes")]
    public ManifestReleaseNotes? ReleaseNotes { get; set; }

    [JsonPropertyName("total_package_size")]
    public string? TotalPackageSize { get; set; }
}

public class ManifestDependency
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("package_version")]
    public string? PackageVersion { get; set; }
}

public class ManifestReleaseNotes
{
    [JsonPropertyName("neutral")]
    public ManifestNeutral? Neutral { get; set; }
}

public class ManifestNeutral
{
    [JsonPropertyName("LastUpdate")]
    public string? LastUpdate { get; set; }

    [JsonPropertyName("OlderHistory")]
    public string? OlderHistory { get; set; }
}

/// <summary>
/// Represents a parsed MSFS layout.json file.
/// </summary>
public class ModLayout
{
    [JsonPropertyName("content")]
    public List<LayoutEntry>? Content { get; set; }
}

public class LayoutEntry
{
    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("date")]
    public long Date { get; set; }
}
