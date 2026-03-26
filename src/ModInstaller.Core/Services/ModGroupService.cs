using System.Text.Json;

namespace ModInstaller.Core.Services;

/// <summary>
/// Manages user-defined mod groups/tags.
/// Groups are stored as a mapping of group name to a list of mod folder names.
/// </summary>
public class ModGroupService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private static readonly string[] DefaultGroupNames = ["Favorites", "Testing", "Broken"];

    private readonly object _lock = new();
    private Dictionary<string, List<string>> _groups;

    public ModGroupService()
    {
        _groups = CreateDefaultGroups();
    }

    /// <summary>
    /// Creates a new group. Throws if the group already exists.
    /// </summary>
    public void CreateGroup(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        lock (_lock)
        {
            if (_groups.ContainsKey(name))
                throw new InvalidOperationException($"Group '{name}' already exists.");

            _groups[name] = [];
        }
    }

    /// <summary>
    /// Deletes a group. Throws if the group does not exist.
    /// </summary>
    public void DeleteGroup(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        lock (_lock)
        {
            if (!_groups.Remove(name))
                throw new KeyNotFoundException($"Group '{name}' does not exist.");
        }
    }

    /// <summary>
    /// Adds a mod folder to a group. No-op if the mod is already in the group.
    /// </summary>
    public void AddModToGroup(string group, string modFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(group);
        ArgumentException.ThrowIfNullOrWhiteSpace(modFolder);

        lock (_lock)
        {
            if (!_groups.TryGetValue(group, out var mods))
                throw new KeyNotFoundException($"Group '{group}' does not exist.");

            if (!mods.Contains(modFolder, StringComparer.OrdinalIgnoreCase))
                mods.Add(modFolder);
        }
    }

    /// <summary>
    /// Removes a mod folder from a group. Throws if the mod is not in the group.
    /// </summary>
    public void RemoveModFromGroup(string group, string modFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(group);
        ArgumentException.ThrowIfNullOrWhiteSpace(modFolder);

        lock (_lock)
        {
            if (!_groups.TryGetValue(group, out var mods))
                throw new KeyNotFoundException($"Group '{group}' does not exist.");

            var index = mods.FindIndex(m => m.Equals(modFolder, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                throw new InvalidOperationException($"Mod '{modFolder}' is not in group '{group}'.");

            mods.RemoveAt(index);
        }
    }

    /// <summary>
    /// Returns a snapshot of all groups and their mod folder lists.
    /// </summary>
    public Dictionary<string, List<string>> GetGroups()
    {
        lock (_lock)
        {
            return _groups.ToDictionary(
                kvp => kvp.Key,
                kvp => new List<string>(kvp.Value));
        }
    }

    /// <summary>
    /// Returns the names of all groups that contain the specified mod folder.
    /// </summary>
    public List<string> GetModGroups(string modFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modFolder);

        lock (_lock)
        {
            return _groups
                .Where(kvp => kvp.Value.Any(m => m.Equals(modFolder, StringComparison.OrdinalIgnoreCase)))
                .Select(kvp => kvp.Key)
                .ToList();
        }
    }

    /// <summary>
    /// Saves the current groups to a JSON file.
    /// </summary>
    public async Task SaveAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        Dictionary<string, List<string>> snapshot;
        lock (_lock)
        {
            snapshot = _groups.ToDictionary(
                kvp => kvp.Key,
                kvp => new List<string>(kvp.Value));
        }

        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(snapshot, JsonOptions);
        await File.WriteAllTextAsync(filePath, json, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Loads groups from a JSON file. Missing default groups are re-created.
    /// </summary>
    public async Task LoadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!File.Exists(filePath))
        {
            lock (_lock)
            {
                _groups = CreateDefaultGroups();
            }
            return;
        }

        var json = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
        var loaded = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json, JsonOptions);

        lock (_lock)
        {
            _groups = loaded ?? CreateDefaultGroups();

            // Ensure default groups exist
            foreach (var defaultGroup in DefaultGroupNames)
            {
                _groups.TryAdd(defaultGroup, []);
            }
        }
    }

    private static Dictionary<string, List<string>> CreateDefaultGroups()
    {
        var groups = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var name in DefaultGroupNames)
        {
            groups[name] = [];
        }
        return groups;
    }
}
