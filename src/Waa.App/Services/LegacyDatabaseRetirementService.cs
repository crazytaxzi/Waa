using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Waa.App.Services;

public sealed record LegacyDatabaseRetirementResult(
    bool Retired,
    string? ArchiveDirectory,
    bool? DarkMode,
    bool? AmbientMotionEnabled,
    string Message)
{
    public static LegacyDatabaseRetirementResult NotNeeded { get; } =
        new(false, null, null, null, "No previous-generation WAA database retirement was needed.");
}

/// <summary>
/// Archives the known pre-current WAA database generation before the current
/// driver-code schema is initialized. The retirement is intentionally archival:
/// old PTA/call/note/reminder/BOL semantics are preserved intact in the legacy
/// database instead of being guessed into current work entries.
/// </summary>
public sealed class LegacyDatabaseRetirementService
{
    private static readonly string[] KnownLegacyDriverColumns =
    [
        "id",
        "full_name",
        "pta_code"
    ];

    public LegacyDatabaseRetirementResult RetireIfNeeded(
        string databasePath,
        string dataDirectory,
        DateTimeOffset? retiredUtc = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);

        if (!File.Exists(databasePath))
        {
            return LegacyDatabaseRetirementResult.NotNeeded;
        }

        var now = (retiredUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var rowCounts = new SortedDictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        bool? darkMode;
        bool? ambientMotion;
        string partialArchiveDirectory;
        string finalArchiveDirectory;
        var legacyLiveDirectory = Path.Combine(dataDirectory, "live");
        var liveStorePresent = Directory.Exists(legacyLiveDirectory);

        using (var source = Open(databasePath, SqliteOpenMode.ReadOnly))
        {
            if (!TableExists(source, "drivers"))
            {
                return LegacyDatabaseRetirementResult.NotNeeded;
            }

            var driverColumns = LoadColumnNames(source, "drivers");
            if (driverColumns.Contains("driver_code"))
            {
                return LegacyDatabaseRetirementResult.NotNeeded;
            }

            var knownLegacy = KnownLegacyDriverColumns.All(driverColumns.Contains) &&
                              TableExists(source, "schema_version");
            if (!knownLegacy)
            {
                throw new InvalidOperationException(
                    "The existing WAA database is incompatible with the current schema, but it does not " +
                    "match the known previous-generation WAA layout. It was left untouched for manual review.");
            }

            darkMode = ReadDarkMode(source);
            ambientMotion = ReadAmbientMotion(source);
            foreach (var pair in LoadTableRowCounts(source))
            {
                rowCounts[pair.Key] = pair.Value;
            }

            var archiveRoot = BuildArchiveRoot(dataDirectory);
            Directory.CreateDirectory(archiveRoot);
            finalArchiveDirectory = ChooseArchiveDirectory(archiveRoot, now);
            partialArchiveDirectory = finalArchiveDirectory + ".partial-" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(partialArchiveDirectory);

            var archiveDatabasePath = Path.Combine(partialArchiveDirectory, "waa-legacy.db");
            using var destination = Open(archiveDatabasePath, SqliteOpenMode.ReadWriteCreate);
            source.BackupDatabase(destination);
            using var quickCheck = destination.CreateCommand();
            quickCheck.CommandText = "PRAGMA quick_check;";
            var integrity = Convert.ToString(quickCheck.ExecuteScalar(), CultureInfo.InvariantCulture);
            if (!string.Equals(integrity, "ok", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"The archived previous-generation database failed SQLite quick_check: {integrity}");
            }
        }

        try
        {
            if (liveStorePresent)
            {
                CopyDirectory(
                    legacyLiveDirectory,
                    Path.Combine(partialArchiveDirectory, "live"));
            }

            var manifest = new LegacyRetirementManifest(
                "WAA previous-generation retirement archive",
                now,
                databasePath,
                "drivers(id, full_name, pta_code) previous-generation schema",
                liveStorePresent,
                rowCounts);
            File.WriteAllText(
                Path.Combine(partialArchiveDirectory, "RETIREMENT-MANIFEST.json"),
                JsonSerializer.Serialize(
                    manifest,
                    new JsonSerializerOptions { WriteIndented = true }));

            Directory.Move(partialArchiveDirectory, finalArchiveDirectory);
        }
        catch
        {
            // Source database and live store have not been modified at this point.
            throw;
        }

        DeleteRequiredLegacyDatabaseFile(databasePath);
        DeleteIfPresent(databasePath + "-wal");
        DeleteIfPresent(databasePath + "-shm");

        var liveCleanupWarning = string.Empty;
        if (liveStorePresent)
        {
            try
            {
                Directory.Delete(legacyLiveDirectory, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                liveCleanupWarning =
                    " The old LMDB live folder could not be removed after it was archived; " +
                    "its archived copy is intact.";
            }
        }

        return new LegacyDatabaseRetirementResult(
            true,
            finalArchiveDirectory,
            darkMode,
            ambientMotion,
            $"Previous-generation WAA data was archived intact at {finalArchiveDirectory}. " +
            "The current WAA database will be created fresh; old PTA/call/note/reminder/BOL semantics " +
            "were not guessed into current work." + liveCleanupWarning);
    }

    private static string BuildArchiveRoot(string dataDirectory)
    {
        var parent = Directory.GetParent(Path.GetFullPath(dataDirectory))?.FullName;
        if (string.IsNullOrWhiteSpace(parent))
        {
            throw new InvalidOperationException("Could not determine a safe parent folder for the legacy WAA archive.");
        }

        return Path.Combine(parent, "WAA-Legacy");
    }

    private static string ChooseArchiveDirectory(string archiveRoot, DateTimeOffset now)
    {
        var baseName = "WAA-retired-" + now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var candidate = Path.Combine(archiveRoot, baseName);
        for (var suffix = 0; Directory.Exists(candidate); suffix++)
        {
            candidate = Path.Combine(archiveRoot, $"{baseName}-{suffix + 2}");
        }

        return candidate;
    }

    private static bool? ReadDarkMode(SqliteConnection connection)
    {
        var value = ReadSetting(connection, "appearance_theme");
        if (string.Equals(value, "dark", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(value, "light", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return null;
    }

    private static bool? ReadAmbientMotion(SqliteConnection connection)
    {
        var value = ReadSetting(connection, "appearance_ambient_motion");
        if (string.Equals(value, "on", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(value, "off", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return null;
    }

    private static string? ReadSetting(SqliteConnection connection, string key)
    {
        if (!TableExists(connection, "settings"))
        {
            return null;
        }

        var columns = LoadColumnNames(connection, "settings");
        if (!columns.Contains("key") || !columns.Contains("value"))
        {
            return null;
        }

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM settings WHERE key = $key LIMIT 1;";
        command.Parameters.AddWithValue("$key", key);
        var value = command.ExecuteScalar();
        return value is null or DBNull ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    private static IReadOnlyDictionary<string, long> LoadTableRowCounts(SqliteConnection connection)
    {
        using var tables = connection.CreateCommand();
        tables.CommandText = """
            SELECT name
            FROM sqlite_master
            WHERE type = 'table'
              AND name NOT LIKE 'sqlite_%'
            ORDER BY name;
            """;
        var names = new List<string>();
        using (var reader = tables.ExecuteReader())
        {
            while (reader.Read())
            {
                names.Add(reader.GetString(0));
            }
        }

        var result = new SortedDictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            using var count = connection.CreateCommand();
            count.CommandText = $"SELECT COUNT(*) FROM \"{name.Replace("\"", "\"\"")}\";";
            result[name] = Convert.ToInt64(count.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        return result;
    }

    private static HashSet<string> LoadColumnNames(SqliteConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{tableName.Replace("\"", "\"\"")}\");";
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            columns.Add(reader.GetString(1));
        }

        return columns;
    }

    private static bool TableExists(SqliteConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name);";
        command.Parameters.AddWithValue("$name", tableName);
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) == 1;
    }

    private static SqliteConnection Open(string databasePath, SqliteOpenMode mode)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = mode,
            Cache = SqliteCacheMode.Private,
            Pooling = false
        }.ToString());
        connection.Open();
        using var timeout = connection.CreateCommand();
        timeout.CommandText = "PRAGMA busy_timeout = 5000;";
        timeout.ExecuteNonQuery();
        return connection;
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        foreach (var file in Directory.EnumerateFiles(sourceDirectory))
        {
            File.Copy(file, Path.Combine(destinationDirectory, Path.GetFileName(file)), overwrite: false);
        }

        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory))
        {
            CopyDirectory(
                directory,
                Path.Combine(destinationDirectory, Path.GetFileName(directory)));
        }
    }

    private static void DeleteRequiredLegacyDatabaseFile(string databasePath)
    {
        try
        {
            File.Delete(databasePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                "The previous-generation database was archived successfully, but WAA could not retire the original " +
                $"database file at {databasePath}. Close every older WAA instance and try again. The archive remains intact.",
                exception);
        }
    }

    private static void DeleteIfPresent(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"The previous-generation database was archived, but its SQLite companion file could not be retired: {path}",
                exception);
        }
    }

    private sealed record LegacyRetirementManifest(
        string Kind,
        DateTimeOffset RetiredUtc,
        string OriginalDatabasePath,
        string DetectedSchema,
        bool LiveStoreArchived,
        IReadOnlyDictionary<string, long> TableRowCounts);
}
