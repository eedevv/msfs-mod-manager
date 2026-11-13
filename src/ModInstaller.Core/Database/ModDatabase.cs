using Microsoft.Data.Sqlite;
using ModInstaller.Core.Models;
using ModInstaller.Core.Services;

namespace ModInstaller.Core.Database;

/// <summary>
/// SQLite-backed storage for mod metadata, operation history, and conflict cache.
/// </summary>
public class ModDatabase : IDisposable
{
    private readonly SqliteConnection _connection;

    public ModDatabase(string dbPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        _connection = new SqliteConnection($"Data Source={dbPath}");
        _connection.Open();
        Initialize();
    }

    private void Initialize()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS mods (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                folder_name TEXT NOT NULL UNIQUE,
                full_path TEXT NOT NULL,
                title TEXT,
                creator TEXT,
                version TEXT,
                min_game_version TEXT,
                content_type TEXT,
                mod_type TEXT NOT NULL DEFAULT 'Unknown',
                status TEXT NOT NULL DEFAULT 'Enabled',
                size_bytes INTEGER DEFAULT 0,
                file_count INTEGER DEFAULT 0,
                installed_date TEXT,
                last_modified TEXT,
                link_type TEXT NOT NULL DEFAULT 'Copy',
                library_path TEXT,
                has_manifest INTEGER DEFAULT 0,
                has_layout INTEGER DEFAULT 0,
                health TEXT NOT NULL DEFAULT 'Healthy',
                sim_version TEXT NOT NULL DEFAULT 'Unknown',
                dependencies TEXT
            );

            CREATE TABLE IF NOT EXISTS operations (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                operation_type TEXT NOT NULL,
                mod_name TEXT NOT NULL,
                details TEXT,
                timestamp TEXT NOT NULL DEFAULT (datetime('now', 'localtime')),
                can_undo INTEGER DEFAULT 0,
                undo_data TEXT
            );

            CREATE TABLE IF NOT EXISTS conflicts (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                mod_a TEXT NOT NULL,
                mod_b TEXT NOT NULL,
                conflicting_files TEXT,
                detected_date TEXT NOT NULL DEFAULT (datetime('now', 'localtime')),
                UNIQUE(mod_a, mod_b)
            );

            CREATE TABLE IF NOT EXISTS profiles (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL UNIQUE,
                description TEXT,
                created_date TEXT NOT NULL DEFAULT (datetime('now', 'localtime')),
                last_used_date TEXT,
                sim_version TEXT NOT NULL DEFAULT 'Unknown',
                enabled_mods TEXT,
                disabled_mods TEXT
            );

            CREATE TABLE IF NOT EXISTS community_profiles (
                community_key TEXT NOT NULL,
                name TEXT NOT NULL COLLATE NOCASE,
                description TEXT,
                created_date TEXT NOT NULL DEFAULT (datetime('now', 'localtime')),
                last_used_date TEXT,
                sim_version TEXT NOT NULL DEFAULT 'Unknown',
                enabled_mods TEXT,
                disabled_mods TEXT,
                PRIMARY KEY (community_key, name)
            );

            CREATE INDEX IF NOT EXISTS idx_mods_folder ON mods(folder_name);
            CREATE INDEX IF NOT EXISTS idx_mods_status ON mods(status);
            CREATE INDEX IF NOT EXISTS idx_operations_timestamp ON operations(timestamp);
            """;
        cmd.ExecuteNonQuery();
    }

    #region Mod CRUD

    public void UpsertMod(Mod mod)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO mods (folder_name, full_path, title, creator, version, min_game_version,
                content_type, mod_type, status, size_bytes, file_count, installed_date, last_modified,
                link_type, library_path, has_manifest, has_layout, health, sim_version, dependencies)
            VALUES (@folder, @path, @title, @creator, @version, @minVer,
                @contentType, @modType, @status, @size, @fileCount, @installed, @modified,
                @linkType, @libPath, @hasManifest, @hasLayout, @health, @simVer, @deps)
            ON CONFLICT(folder_name) DO UPDATE SET
                full_path=@path, title=@title, creator=@creator, version=@version,
                min_game_version=@minVer, content_type=@contentType, mod_type=@modType,
                status=@status, size_bytes=@size, file_count=@fileCount, last_modified=@modified,
                link_type=@linkType, library_path=@libPath, has_manifest=@hasManifest,
                has_layout=@hasLayout, health=@health, sim_version=@simVer, dependencies=@deps
            """;

        cmd.Parameters.AddWithValue("@folder", mod.FolderName);
        cmd.Parameters.AddWithValue("@path", mod.FullPath);
        cmd.Parameters.AddWithValue("@title", (object?)mod.Title ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@creator", (object?)mod.Creator ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@version", (object?)mod.Version ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@minVer", (object?)mod.MinGameVersion ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@contentType", (object?)mod.ContentType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@modType", mod.Type.ToString());
        cmd.Parameters.AddWithValue("@status", mod.Status.ToString());
        cmd.Parameters.AddWithValue("@size", mod.SizeBytes);
        cmd.Parameters.AddWithValue("@fileCount", mod.FileCount);
        cmd.Parameters.AddWithValue("@installed", mod.InstalledDate?.ToString("o") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@modified", mod.LastModified?.ToString("o") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@linkType", mod.LinkType.ToString());
        cmd.Parameters.AddWithValue("@libPath", (object?)mod.LibraryPath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@hasManifest", mod.HasManifest ? 1 : 0);
        cmd.Parameters.AddWithValue("@hasLayout", mod.HasLayout ? 1 : 0);
        cmd.Parameters.AddWithValue("@health", mod.Health.ToString());
        cmd.Parameters.AddWithValue("@simVer", mod.SimVersion.ToString());
        cmd.Parameters.AddWithValue("@deps", mod.Dependencies.Count > 0
            ? string.Join("|", mod.Dependencies) : (object)DBNull.Value);

        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Batch upsert — wraps all writes in a single transaction for performance.
    /// </summary>
    public void UpsertMods(IEnumerable<Mod> mods)
    {
        using var transaction = _connection.BeginTransaction();
        try
        {
            foreach (var mod in mods)
                UpsertMod(mod);
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public void RemoveMod(string folderName)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM mods WHERE folder_name = @folder";
        cmd.Parameters.AddWithValue("@folder", folderName);
        cmd.ExecuteNonQuery();
    }

    public List<Mod> GetAllMods()
    {
        var mods = new List<Mod>();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM mods ORDER BY folder_name";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            mods.Add(ReadMod(reader));
        }

        return mods;
    }

    private static Mod ReadMod(SqliteDataReader reader)
    {
        var mod = new Mod
        {
            Id = reader.GetInt32(reader.GetOrdinal("id")),
            FolderName = reader.GetString(reader.GetOrdinal("folder_name")),
            FullPath = reader.GetString(reader.GetOrdinal("full_path")),
            Title = reader.IsDBNull(reader.GetOrdinal("title")) ? null : reader.GetString(reader.GetOrdinal("title")),
            Creator = reader.IsDBNull(reader.GetOrdinal("creator")) ? null : reader.GetString(reader.GetOrdinal("creator")),
            Version = reader.IsDBNull(reader.GetOrdinal("version")) ? null : reader.GetString(reader.GetOrdinal("version")),
            MinGameVersion = reader.IsDBNull(reader.GetOrdinal("min_game_version")) ? null : reader.GetString(reader.GetOrdinal("min_game_version")),
            ContentType = reader.IsDBNull(reader.GetOrdinal("content_type")) ? null : reader.GetString(reader.GetOrdinal("content_type")),
            SizeBytes = reader.GetInt64(reader.GetOrdinal("size_bytes")),
            FileCount = reader.GetInt32(reader.GetOrdinal("file_count")),
            HasManifest = reader.GetInt32(reader.GetOrdinal("has_manifest")) == 1,
            HasLayout = reader.GetInt32(reader.GetOrdinal("has_layout")) == 1,
        };

        if (Enum.TryParse<ModType>(reader.GetString(reader.GetOrdinal("mod_type")), out var modType))
            mod.Type = modType;
        if (Enum.TryParse<ModStatus>(reader.GetString(reader.GetOrdinal("status")), out var status))
            mod.Status = status;
        if (Enum.TryParse<LinkType>(reader.GetString(reader.GetOrdinal("link_type")), out var linkType))
            mod.LinkType = linkType;
        if (Enum.TryParse<HealthStatus>(reader.GetString(reader.GetOrdinal("health")), out var health))
            mod.Health = health;
        if (Enum.TryParse<SimVersion>(reader.GetString(reader.GetOrdinal("sim_version")), out var simVer))
            mod.SimVersion = simVer;

        var libPath = reader.IsDBNull(reader.GetOrdinal("library_path")) ? null : reader.GetString(reader.GetOrdinal("library_path"));
        mod.LibraryPath = libPath;

        var installed = reader.IsDBNull(reader.GetOrdinal("installed_date")) ? null : reader.GetString(reader.GetOrdinal("installed_date"));
        if (installed != null && DateTime.TryParse(installed, out var installedDate))
            mod.InstalledDate = installedDate;

        var modified = reader.IsDBNull(reader.GetOrdinal("last_modified")) ? null : reader.GetString(reader.GetOrdinal("last_modified"));
        if (modified != null && DateTime.TryParse(modified, out var modifiedDate))
            mod.LastModified = modifiedDate;

        var deps = reader.IsDBNull(reader.GetOrdinal("dependencies")) ? null : reader.GetString(reader.GetOrdinal("dependencies"));
        if (!string.IsNullOrEmpty(deps))
            mod.Dependencies = deps.Split('|').ToList();

        return mod;
    }

    #endregion

    #region Operations Log

    public void LogOperation(string operationType, string modName, string? details = null, bool canUndo = false, string? undoData = null)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO operations (operation_type, mod_name, details, can_undo, undo_data)
            VALUES (@type, @mod, @details, @canUndo, @undoData)
            """;
        cmd.Parameters.AddWithValue("@type", operationType);
        cmd.Parameters.AddWithValue("@mod", modName);
        cmd.Parameters.AddWithValue("@details", (object?)details ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@canUndo", canUndo ? 1 : 0);
        cmd.Parameters.AddWithValue("@undoData", (object?)undoData ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public List<OperationLog> GetRecentOperations(int limit = 50)
    {
        var ops = new List<OperationLog>();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM operations ORDER BY timestamp DESC LIMIT @limit";
        cmd.Parameters.AddWithValue("@limit", limit);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            ops.Add(new OperationLog
            {
                Id = reader.GetInt32(reader.GetOrdinal("id")),
                OperationType = reader.GetString(reader.GetOrdinal("operation_type")),
                ModName = reader.GetString(reader.GetOrdinal("mod_name")),
                Details = reader.IsDBNull(reader.GetOrdinal("details")) ? null : reader.GetString(reader.GetOrdinal("details")),
                Timestamp = DateTime.TryParse(reader.GetString(reader.GetOrdinal("timestamp")), out var ts) ? ts : DateTime.Now,
                CanUndo = reader.GetInt32(reader.GetOrdinal("can_undo")) == 1,
                UndoData = reader.IsDBNull(reader.GetOrdinal("undo_data")) ? null : reader.GetString(reader.GetOrdinal("undo_data")),
            });
        }

        return ops;
    }

    #endregion

    #region Profiles

    public void SaveProfile(ModProfile profile)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO profiles (name, description, sim_version, enabled_mods, disabled_mods)
            VALUES (@name, @desc, @simVer, @enabled, @disabled)
            ON CONFLICT(name) DO UPDATE SET
                description=@desc, sim_version=@simVer, enabled_mods=@enabled,
                disabled_mods=@disabled, last_used_date=datetime('now', 'localtime')
            """;
        cmd.Parameters.AddWithValue("@name", profile.Name);
        cmd.Parameters.AddWithValue("@desc", (object?)profile.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@simVer", profile.SimVersion.ToString());
        cmd.Parameters.AddWithValue("@enabled", string.Join("|", profile.EnabledMods));
        cmd.Parameters.AddWithValue("@disabled", string.Join("|", profile.DisabledMods));
        cmd.ExecuteNonQuery();
    }

    public List<ModProfile> GetAllProfiles()
    {
        var profiles = new List<ModProfile>();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM profiles ORDER BY name";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var profile = ReadProfile(reader);

            profiles.Add(profile);
        }

        return profiles;
    }

    private static ModProfile ReadProfile(SqliteDataReader reader)
    {
        var profile = new ModProfile
        {
            Name = reader.GetString(reader.GetOrdinal("name")),
            Description = reader.IsDBNull(reader.GetOrdinal("description")) ? null : reader.GetString(reader.GetOrdinal("description")),
            CreatedDate = DateTime.TryParse(reader.GetString(reader.GetOrdinal("created_date")), out var cd) ? cd : DateTime.Now,
        };
        var lastUsed = reader.IsDBNull(reader.GetOrdinal("last_used_date")) ? null : reader.GetString(reader.GetOrdinal("last_used_date"));
        if (lastUsed != null && DateTime.TryParse(lastUsed, out var lu)) profile.LastUsedDate = lu;
        if (Enum.TryParse<SimVersion>(reader.GetString(reader.GetOrdinal("sim_version")), out var simVer)) profile.SimVersion = simVer;
        var enabled = reader.IsDBNull(reader.GetOrdinal("enabled_mods")) ? null : reader.GetString(reader.GetOrdinal("enabled_mods"));
        if (!string.IsNullOrEmpty(enabled)) profile.EnabledMods = enabled.Split('|', StringSplitOptions.RemoveEmptyEntries).ToList();
        var disabled = reader.IsDBNull(reader.GetOrdinal("disabled_mods")) ? null : reader.GetString(reader.GetOrdinal("disabled_mods"));
        if (!string.IsNullOrEmpty(disabled)) profile.DisabledMods = disabled.Split('|', StringSplitOptions.RemoveEmptyEntries).ToList();
        return profile;
    }

    /// <summary>Save a profile for one Community folder; legacy profiles are never changed.</summary>
    public void SaveCommunityProfile(string communityFolder, ModProfile profile, bool replace = true)
    {
        var key = DisabledModStorage.GetCommunityKey(communityFolder);
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = replace ? """
            INSERT INTO community_profiles (community_key, name, description, sim_version, enabled_mods, disabled_mods)
            VALUES (@key, @name, @desc, @simVer, @enabled, @disabled)
            ON CONFLICT(community_key, name) DO UPDATE SET
                description=@desc, sim_version=@simVer, enabled_mods=@enabled,
                disabled_mods=@disabled, last_used_date=datetime('now', 'localtime')
            """ : """
            INSERT INTO community_profiles (community_key, name, description, sim_version, enabled_mods, disabled_mods)
            VALUES (@key, @name, @desc, @simVer, @enabled, @disabled)
            """;
        cmd.Parameters.AddWithValue("@key", key);
        cmd.Parameters.AddWithValue("@name", profile.Name);
        cmd.Parameters.AddWithValue("@desc", (object?)profile.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@simVer", profile.SimVersion.ToString());
        cmd.Parameters.AddWithValue("@enabled", string.Join("|", profile.EnabledMods));
        cmd.Parameters.AddWithValue("@disabled", string.Join("|", profile.DisabledMods));
        cmd.ExecuteNonQuery();
        profile.CommunityKey = key;
    }

    public List<ModProfile> GetCommunityProfiles(string communityFolder)
    {
        var key = DisabledModStorage.GetCommunityKey(communityFolder);
        var profiles = new List<ModProfile>();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM community_profiles WHERE community_key = @key ORDER BY name";
        cmd.Parameters.AddWithValue("@key", key);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var profile = ReadProfile(reader);
            profile.CommunityKey = key;
            profiles.Add(profile);
        }
        return profiles;
    }

    public void DeleteCommunityProfile(string communityFolder, string name)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM community_profiles WHERE community_key = @key AND name = @name";
        cmd.Parameters.AddWithValue("@key", DisabledModStorage.GetCommunityKey(communityFolder));
        cmd.Parameters.AddWithValue("@name", name);
        cmd.ExecuteNonQuery();
    }

    public void DeleteProfile(string name)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM profiles WHERE name = @name";
        cmd.Parameters.AddWithValue("@name", name);
        cmd.ExecuteNonQuery();
    }

    #endregion

    #region Conflicts

    public void SaveConflict(ConflictResult conflict)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO conflicts (mod_a, mod_b, conflicting_files)
            VALUES (@modA, @modB, @files)
            """;
        cmd.Parameters.AddWithValue("@modA", conflict.ModA);
        cmd.Parameters.AddWithValue("@modB", conflict.ModB);
        cmd.Parameters.AddWithValue("@files", string.Join("|", conflict.ConflictingFiles));
        cmd.ExecuteNonQuery();
    }

    public List<ConflictResult> GetConflictsForMod(string modName)
    {
        var conflicts = new List<ConflictResult>();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM conflicts WHERE mod_a = @mod OR mod_b = @mod";
        cmd.Parameters.AddWithValue("@mod", modName);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            conflicts.Add(new ConflictResult
            {
                ModA = reader.GetString(reader.GetOrdinal("mod_a")),
                ModB = reader.GetString(reader.GetOrdinal("mod_b")),
                ConflictingFiles = reader.GetString(reader.GetOrdinal("conflicting_files")).Split('|').ToList()
            });
        }

        return conflicts;
    }

    public void ClearConflicts()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM conflicts";
        cmd.ExecuteNonQuery();
    }

    #endregion

    public void Dispose()
    {
        _connection.Close();
        _connection.Dispose();
    }
}
