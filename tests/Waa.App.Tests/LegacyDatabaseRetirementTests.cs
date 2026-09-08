using Microsoft.Data.Sqlite;
using Waa.App.Data;
using Waa.App.Services;
using Xunit;

namespace Waa.App.Tests;

public sealed class LegacyDatabaseRetirementTests
{
    [Fact]
    public void KnownPreviousGeneration_IsArchivedAndCurrentDatabaseCanStartFresh()
    {
        var root = Path.Combine(Path.GetTempPath(), "WaaLegacyRetirementTests", Guid.NewGuid().ToString("N"));
        var dataDirectory = Path.Combine(root, "WAA");
        Directory.CreateDirectory(dataDirectory);
        var databasePath = Path.Combine(dataDirectory, "waa.db");
        var liveDirectory = Path.Combine(dataDirectory, "live");
        Directory.CreateDirectory(liveDirectory);
        File.WriteAllText(Path.Combine(liveDirectory, "data.mdb"), "synthetic legacy live state");
        CreateKnownLegacyDatabase(databasePath);

        try
        {
            var retiredAt = new DateTimeOffset(2026, 9, 8, 22, 0, 0, TimeSpan.Zero);
            var result = new LegacyDatabaseRetirementService()
                .RetireIfNeeded(databasePath, dataDirectory, retiredAt);

            Assert.True(result.Retired);
            Assert.True(result.DarkMode);
            Assert.False(result.AmbientMotionEnabled);
            Assert.NotNull(result.ArchiveDirectory);
            Assert.False(File.Exists(databasePath));
            Assert.False(Directory.Exists(liveDirectory));
            Assert.True(File.Exists(Path.Combine(result.ArchiveDirectory!, "waa-legacy.db")));
            Assert.True(File.Exists(Path.Combine(result.ArchiveDirectory!, "live", "data.mdb")));
            Assert.True(File.Exists(Path.Combine(result.ArchiveDirectory!, "RETIREMENT-MANIFEST.json")));

            using (var archived = Open(Path.Combine(result.ArchiveDirectory!, "waa-legacy.db")))
            {
                Assert.Equal("ok", ScalarText(archived, "PRAGMA quick_check;"));
                Assert.Equal(1L, ScalarLong(archived, "SELECT COUNT(*) FROM drivers;"));
                Assert.Equal(1L, ScalarLong(archived, "SELECT COUNT(*) FROM driver_notes;"));
                Assert.Equal("Synthetic legacy note", ScalarText(archived, "SELECT note FROM driver_notes LIMIT 1;"));
            }

            var manifest = File.ReadAllText(Path.Combine(result.ArchiveDirectory!, "RETIREMENT-MANIFEST.json"));
            Assert.Contains("driver_notes", manifest, StringComparison.Ordinal);
            Assert.Contains("LiveStoreArchived", manifest, StringComparison.Ordinal);

            var current = new WaaRepository(databasePath);
            current.Initialize();
            using var currentConnection = Open(databasePath);
            Assert.Equal(
                1L,
                ScalarLong(
                    currentConnection,
                    "SELECT COUNT(*) FROM pragma_table_info('drivers') WHERE name = 'driver_code';"));
            Assert.Equal(4L, ScalarLong(currentConnection, "PRAGMA user_version;"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void CurrentDatabase_IsNotRetired()
    {
        var root = Path.Combine(Path.GetTempPath(), "WaaLegacyRetirementTests", Guid.NewGuid().ToString("N"));
        var dataDirectory = Path.Combine(root, "WAA");
        Directory.CreateDirectory(dataDirectory);
        var databasePath = Path.Combine(dataDirectory, "waa.db");

        try
        {
            var current = new WaaRepository(databasePath);
            current.Initialize();

            var result = new LegacyDatabaseRetirementService()
                .RetireIfNeeded(databasePath, dataDirectory);

            Assert.False(result.Retired);
            Assert.True(File.Exists(databasePath));
            Assert.False(Directory.Exists(Path.Combine(root, "WAA-Legacy")));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void UnknownIncompatibleDatabase_IsNeverAutoRetired()
    {
        var root = Path.Combine(Path.GetTempPath(), "WaaLegacyRetirementTests", Guid.NewGuid().ToString("N"));
        var dataDirectory = Path.Combine(root, "WAA");
        Directory.CreateDirectory(dataDirectory);
        var databasePath = Path.Combine(dataDirectory, "waa.db");

        try
        {
            using (var connection = Open(databasePath))
            {
                using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE drivers(foo TEXT); INSERT INTO drivers(foo) VALUES ('keep me');";
                command.ExecuteNonQuery();
            }

            var exception = Assert.Throws<InvalidOperationException>(() =>
                new LegacyDatabaseRetirementService().RetireIfNeeded(databasePath, dataDirectory));

            Assert.Contains("left untouched", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(databasePath));
            using var verify = Open(databasePath);
            Assert.Equal("keep me", ScalarText(verify, "SELECT foo FROM drivers LIMIT 1;"));
            Assert.False(Directory.Exists(Path.Combine(root, "WAA-Legacy")));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static void CreateKnownLegacyDatabase(string databasePath)
    {
        using var connection = Open(databasePath);
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE schema_version(version INTEGER PRIMARY KEY, applied_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP);
            CREATE TABLE drivers(
                id INTEGER PRIMARY KEY,
                full_name TEXT NOT NULL DEFAULT 'Unknown',
                pta_code TEXT,
                created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UNIQUE(pta_code)
            );
            CREATE TABLE settings(
                key TEXT PRIMARY KEY,
                value TEXT,
                updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            CREATE TABLE driver_notes(
                id INTEGER PRIMARY KEY,
                driver_id INTEGER NOT NULL REFERENCES drivers(id),
                note TEXT NOT NULL,
                created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            INSERT INTO schema_version(version) VALUES (1), (2), (3);
            INSERT INTO drivers(id, full_name, pta_code) VALUES (1, 'Legacy Example', 'LEG001');
            INSERT INTO settings(key, value) VALUES
                ('appearance_theme', 'dark'),
                ('appearance_ambient_motion', 'off');
            INSERT INTO driver_notes(driver_id, note) VALUES (1, 'Synthetic legacy note');
            """;
        command.ExecuteNonQuery();
    }

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        return connection;
    }

    private static long ScalarLong(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string ScalarText(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
    }
}
