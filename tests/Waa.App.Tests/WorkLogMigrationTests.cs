using Microsoft.Data.Sqlite;
using Waa.App.Data;
using Xunit;

namespace Waa.App.Tests;

public sealed class WorkLogMigrationTests
{
    [Fact]
    public void Initialize_BackfillsPreWorkLogIdleEventExactlyOnceAndPreservesSettings()
    {
        var root = Path.Combine(Path.GetTempPath(), "WaaMigrationTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var databasePath = Path.Combine(root, "waa.db");

        try
        {
            CreatePreWorkLogDatabase(databasePath);
            var repository = new WaaRepository(databasePath);

            repository.Initialize();
            repository.Initialize();
            var restarted = new WaaRepository(databasePath);
            restarted.Initialize();

            var work = Assert.IsType<WorkEntryRecord>(
                restarted.GetWorkEntryForIdleContact(1));
            Assert.Equal(WorkEntryStatus.FollowUp, work.Status);
            Assert.Equal(WorkEntrySource.IdleContact, work.Source);
            Assert.Null(work.ResolvedUtc);
            Assert.Equal(
                new DateTimeOffset(2026, 8, 24, 15, 30, 0, TimeSpan.Zero),
                work.CreatedUtc);
            Assert.Equal(new DateOnly(2026, 8, 23), work.ReportCycleDateSnapshot);
            Assert.Equal("270139", work.UnitCodeSnapshot);
            Assert.Equal("LEADER0001", work.DriverLeaderSnapshot);
            Assert.Contains("Attempted idle contact", work.Text, StringComparison.Ordinal);
            Assert.Contains("28D incomplete 3/4", work.Text, StringComparison.Ordinal);
            Assert.Contains("7D 61.2%", work.Text, StringComparison.Ordinal);
            Assert.Contains("Synthetic legacy note.", work.Text, StringComparison.Ordinal);

            using var connection = new SqliteConnection($"Data Source={databasePath}");
            connection.Open();
            Assert.Equal(
                1L,
                ScalarLong(connection, "SELECT COUNT(*) FROM work_entries WHERE linked_idle_contact_event_id = 1;"));
            Assert.Equal(2L, ScalarLong(connection, "PRAGMA user_version;"));

            Assert.Equal(47.5m, restarted.GetIdleThreshold());
            Assert.True(new ThemePreferenceStore(databasePath).GetDarkMode());
            var fleetDriver = Assert.Single(restarted.LoadFleet().Drivers);
            Assert.Equal("LEG001", fleetDriver.DriverCode);
            Assert.Equal("LEADER0001", fleetDriver.DriverLeader);
            Assert.Equal(IdleContactOutcome.Attempted, fleetDriver.LatestOutcome);
            Assert.Equal(1, fleetDriver.OpenWorkCount);
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

    private static void CreatePreWorkLogDatabase(string databasePath)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA foreign_keys = ON;

            CREATE TABLE app_state (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );

            CREATE TABLE settings (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );

            CREATE TABLE imports (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                source_file_name TEXT NOT NULL,
                source_path TEXT NOT NULL,
                source_hash TEXT NOT NULL UNIQUE,
                report_cycle_date TEXT NOT NULL,
                source_last_write_utc TEXT NOT NULL,
                imported_utc TEXT NOT NULL
            );

            CREATE TABLE drivers (
                driver_code TEXT PRIMARY KEY COLLATE NOCASE,
                driver_name TEXT NOT NULL,
                raw_label TEXT NOT NULL,
                last_seen_cycle TEXT NOT NULL,
                is_current INTEGER NOT NULL,
                current_unit_code TEXT NOT NULL,
                current_driver_leader TEXT NOT NULL,
                driver_terminal TEXT NOT NULL,
                fleet_leader TEXT NOT NULL,
                cost_center TEXT NOT NULL,
                ops_lob TEXT NOT NULL
            );

            CREATE TABLE weekly_observations (
                driver_code TEXT NOT NULL COLLATE NOCASE,
                week_date TEXT NOT NULL,
                engine_hours REAL NOT NULL,
                idle_hours REAL NOT NULL,
                unit_code TEXT NOT NULL,
                driver_leader TEXT NOT NULL,
                driver_terminal TEXT NOT NULL,
                fleet_leader TEXT NOT NULL,
                cost_center TEXT NOT NULL,
                ops_lob TEXT NOT NULL,
                source_import_id INTEGER NOT NULL,
                PRIMARY KEY (driver_code, week_date),
                FOREIGN KEY (driver_code) REFERENCES drivers(driver_code),
                FOREIGN KEY (source_import_id) REFERENCES imports(id)
            );

            CREATE TABLE current_driver_snapshots (
                driver_code TEXT PRIMARY KEY COLLATE NOCASE,
                report_cycle_date TEXT NOT NULL,
                unit_code TEXT NOT NULL,
                driver_leader TEXT NOT NULL,
                engine_hours_7d REAL NOT NULL,
                idle_hours_7d REAL NOT NULL,
                idle_percent_7d REAL NULL,
                engine_hours_28d REAL NOT NULL,
                idle_hours_28d REAL NOT NULL,
                idle_percent_28d REAL NULL,
                coverage_28d INTEGER NOT NULL,
                is_complete_28d INTEGER NOT NULL,
                source_import_id INTEGER NOT NULL,
                FOREIGN KEY (driver_code) REFERENCES drivers(driver_code),
                FOREIGN KEY (source_import_id) REFERENCES imports(id)
            );

            CREATE TABLE idle_contact_events (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                driver_code TEXT NOT NULL COLLATE NOCASE,
                report_cycle_date TEXT NOT NULL,
                outcome TEXT NOT NULL CHECK (outcome IN ('Attempted', 'Spoke', 'SpokeFollowUp')),
                note TEXT NULL,
                created_utc TEXT NOT NULL,
                idle_percent_7d REAL NULL,
                idle_percent_28d REAL NULL,
                coverage_28d INTEGER NOT NULL,
                threshold_snapshot REAL NOT NULL,
                unit_code_snapshot TEXT NOT NULL,
                driver_leader_snapshot TEXT NOT NULL,
                source_import_id INTEGER NOT NULL,
                FOREIGN KEY (driver_code) REFERENCES drivers(driver_code),
                FOREIGN KEY (source_import_id) REFERENCES imports(id)
            );

            INSERT INTO settings(key, value) VALUES ('idle_threshold', '47.5');
            INSERT INTO settings(key, value) VALUES ('appearance_theme', 'dark');
            INSERT INTO app_state(key, value) VALUES ('current_report_cycle', '2026-08-23');
            INSERT INTO app_state(key, value) VALUES ('last_import_file', 'rolling 7 day_data-synthetic.csv');
            INSERT INTO app_state(key, value) VALUES ('last_import_utc', '2026-08-24T14:00:00.0000000+00:00');

            INSERT INTO imports(
                id, source_file_name, source_path, source_hash, report_cycle_date,
                source_last_write_utc, imported_utc)
            VALUES (
                1, 'rolling 7 day_data-synthetic.csv', 'C:\\Synthetic\\rolling 7 day_data-synthetic.csv',
                'SYNTHETIC-LEGACY-HASH', '2026-08-23',
                '2026-08-24T13:59:00.0000000+00:00', '2026-08-24T14:00:00.0000000+00:00');

            INSERT INTO drivers(
                driver_code, driver_name, raw_label, last_seen_cycle, is_current,
                current_unit_code, current_driver_leader, driver_terminal,
                fleet_leader, cost_center, ops_lob)
            VALUES (
                'LEG001', 'Legacy Example', 'LEG001 Legacy Example', '2026-08-23', 1,
                '270139', 'LEADER0001', 'Synthetic', 'TEST', '611 - Synthetic', 'Line Haul');

            INSERT INTO current_driver_snapshots(
                driver_code, report_cycle_date, unit_code, driver_leader,
                engine_hours_7d, idle_hours_7d, idle_percent_7d,
                engine_hours_28d, idle_hours_28d, idle_percent_28d,
                coverage_28d, is_complete_28d, source_import_id)
            VALUES (
                'LEG001', '2026-08-23', '270139', 'LEADER0001',
                100, 61.2, 61.2,
                300, 173.4, NULL,
                3, 0, 1);

            INSERT INTO idle_contact_events(
                id, driver_code, report_cycle_date, outcome, note, created_utc,
                idle_percent_7d, idle_percent_28d, coverage_28d,
                threshold_snapshot, unit_code_snapshot, driver_leader_snapshot,
                source_import_id)
            VALUES (
                1, 'LEG001', '2026-08-23', 'Attempted', 'Synthetic legacy note',
                '2026-08-24T15:30:00.0000000+00:00',
                61.2, NULL, 3, 47.5, '270139', 'LEADER0001', 1);
            """;
        command.ExecuteNonQuery();
    }

    private static long ScalarLong(SqliteConnection connection, string commandText)
    {
        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }
}
