using System.Globalization;
using Microsoft.Data.Sqlite;
using Waa.Core;

namespace Waa.App.Data;

public sealed class WaaRepository
{
    private const decimal DefaultIdleThreshold = 50m;
    private readonly string _databasePath;
    private readonly string _connectionString;

    public WaaRepository(string databasePath)
    {
        _databasePath = databasePath;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private
        }.ToString();
    }

    public void Initialize()
    {
        var parent = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;

            CREATE TABLE IF NOT EXISTS app_state (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS settings (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS imports (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                source_file_name TEXT NOT NULL,
                source_path TEXT NOT NULL,
                source_hash TEXT NOT NULL UNIQUE,
                report_cycle_date TEXT NOT NULL,
                source_last_write_utc TEXT NOT NULL,
                imported_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS drivers (
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

            CREATE TABLE IF NOT EXISTS weekly_observations (
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

            CREATE TABLE IF NOT EXISTS current_driver_snapshots (
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

            CREATE TABLE IF NOT EXISTS idle_contact_events (
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

            CREATE INDEX IF NOT EXISTS ix_observations_week
                ON weekly_observations(week_date, driver_code);
            CREATE INDEX IF NOT EXISTS ix_contacts_cycle_driver
                ON idle_contact_events(report_cycle_date, driver_code, id DESC);
            """;
        command.ExecuteNonQuery();

        using var setting = connection.CreateCommand();
        setting.CommandText = """
            INSERT INTO settings(key, value)
            VALUES ('idle_threshold', $value)
            ON CONFLICT(key) DO NOTHING;
            """;
        setting.Parameters.AddWithValue("$value", DefaultIdleThreshold.ToString(CultureInfo.InvariantCulture));
        setting.ExecuteNonQuery();
    }

    public decimal GetIdleThreshold()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM settings WHERE key = 'idle_threshold';";
        var value = command.ExecuteScalar() as string;
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var threshold)
            ? threshold
            : DefaultIdleThreshold;
    }

    public void SetIdleThreshold(decimal threshold)
    {
        if (threshold is < 0m or > 100m)
        {
            throw new ArgumentOutOfRangeException(nameof(threshold), "Idle threshold must be between 0 and 100.");
        }

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO settings(key, value)
            VALUES ('idle_threshold', $value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """;
        command.Parameters.AddWithValue("$value", threshold.ToString(CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }

    public DateOnly? GetCurrentReportCycle()
    {
        using var connection = OpenConnection();
        var value = GetStateValue(connection, "current_report_cycle");
        return TryParseDate(value, out var parsed) ? parsed : null;
    }

    public ReportImportResult ImportReport(
        RollingSevenDayImport import,
        string sourceFileName,
        string sourcePath,
        string sourceHash,
        DateTime sourceLastWriteUtc)
    {
        ArgumentNullException.ThrowIfNull(import);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceHash);

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        using (var existing = connection.CreateCommand())
        {
            existing.Transaction = transaction;
            existing.CommandText = "SELECT id FROM imports WHERE source_hash = $hash LIMIT 1;";
            existing.Parameters.AddWithValue("$hash", sourceHash);
            var existingId = existing.ExecuteScalar();
            if (existingId is not null)
            {
                return new ReportImportResult(false, true, Convert.ToInt64(existingId, CultureInfo.InvariantCulture));
            }
        }

        var importedUtc = DateTimeOffset.UtcNow;
        long importId;
        using (var insertImport = connection.CreateCommand())
        {
            insertImport.Transaction = transaction;
            insertImport.CommandText = """
                INSERT INTO imports(
                    source_file_name,
                    source_path,
                    source_hash,
                    report_cycle_date,
                    source_last_write_utc,
                    imported_utc)
                VALUES ($fileName, $path, $hash, $cycle, $lastWrite, $imported);
                SELECT last_insert_rowid();
                """;
            insertImport.Parameters.AddWithValue("$fileName", sourceFileName);
            insertImport.Parameters.AddWithValue("$path", sourcePath);
            insertImport.Parameters.AddWithValue("$hash", sourceHash);
            insertImport.Parameters.AddWithValue("$cycle", FormatDate(import.ReportCycleDate));
            insertImport.Parameters.AddWithValue("$lastWrite", sourceLastWriteUtc.ToString("O", CultureInfo.InvariantCulture));
            insertImport.Parameters.AddWithValue("$imported", importedUtc.ToString("O", CultureInfo.InvariantCulture));
            importId = Convert.ToInt64(insertImport.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        using (var clearCurrent = connection.CreateCommand())
        {
            clearCurrent.Transaction = transaction;
            clearCurrent.CommandText = "UPDATE drivers SET is_current = 0;";
            clearCurrent.ExecuteNonQuery();
        }

        var latestObservations = import.Observations
            .GroupBy(observation => observation.Driver.DriverCode, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(observation => observation.WeekDate).First())
            .ToArray();

        foreach (var observation in latestObservations)
        {
            var isCurrent = observation.WeekDate == import.ReportCycleDate;
            using var upsertDriver = connection.CreateCommand();
            upsertDriver.Transaction = transaction;
            upsertDriver.CommandText = """
                INSERT INTO drivers(
                    driver_code, driver_name, raw_label, last_seen_cycle, is_current,
                    current_unit_code, current_driver_leader, driver_terminal,
                    fleet_leader, cost_center, ops_lob)
                VALUES (
                    $code, $name, $raw, $lastSeen, $isCurrent,
                    $unit, $leader, $terminal, $fleet, $costCenter, $opsLob)
                ON CONFLICT(driver_code) DO UPDATE SET
                    driver_name = excluded.driver_name,
                    raw_label = excluded.raw_label,
                    last_seen_cycle = excluded.last_seen_cycle,
                    is_current = excluded.is_current,
                    current_unit_code = excluded.current_unit_code,
                    current_driver_leader = excluded.current_driver_leader,
                    driver_terminal = excluded.driver_terminal,
                    fleet_leader = excluded.fleet_leader,
                    cost_center = excluded.cost_center,
                    ops_lob = excluded.ops_lob;
                """;
            upsertDriver.Parameters.AddWithValue("$code", observation.Driver.DriverCode);
            upsertDriver.Parameters.AddWithValue("$name", observation.Driver.DriverName);
            upsertDriver.Parameters.AddWithValue("$raw", observation.Driver.RawLabel);
            upsertDriver.Parameters.AddWithValue("$lastSeen", FormatDate(observation.WeekDate));
            upsertDriver.Parameters.AddWithValue("$isCurrent", isCurrent ? 1 : 0);
            upsertDriver.Parameters.AddWithValue("$unit", observation.UnitCode);
            upsertDriver.Parameters.AddWithValue("$leader", observation.DriverLeader);
            upsertDriver.Parameters.AddWithValue("$terminal", observation.DriverTerminal);
            upsertDriver.Parameters.AddWithValue("$fleet", observation.FleetLeader);
            upsertDriver.Parameters.AddWithValue("$costCenter", observation.CostCenter);
            upsertDriver.Parameters.AddWithValue("$opsLob", observation.OpsLob);
            upsertDriver.ExecuteNonQuery();
        }

        foreach (var observation in import.Observations)
        {
            using var upsertObservation = connection.CreateCommand();
            upsertObservation.Transaction = transaction;
            upsertObservation.CommandText = """
                INSERT INTO weekly_observations(
                    driver_code, week_date, engine_hours, idle_hours, unit_code,
                    driver_leader, driver_terminal, fleet_leader, cost_center,
                    ops_lob, source_import_id)
                VALUES (
                    $code, $week, $engine, $idle, $unit,
                    $leader, $terminal, $fleet, $costCenter, $opsLob, $importId)
                ON CONFLICT(driver_code, week_date) DO UPDATE SET
                    engine_hours = excluded.engine_hours,
                    idle_hours = excluded.idle_hours,
                    unit_code = excluded.unit_code,
                    driver_leader = excluded.driver_leader,
                    driver_terminal = excluded.driver_terminal,
                    fleet_leader = excluded.fleet_leader,
                    cost_center = excluded.cost_center,
                    ops_lob = excluded.ops_lob,
                    source_import_id = excluded.source_import_id;
                """;
            upsertObservation.Parameters.AddWithValue("$code", observation.Driver.DriverCode);
            upsertObservation.Parameters.AddWithValue("$week", FormatDate(observation.WeekDate));
            upsertObservation.Parameters.AddWithValue("$engine", (double)observation.EngineHours);
            upsertObservation.Parameters.AddWithValue("$idle", (double)observation.IdleHours);
            upsertObservation.Parameters.AddWithValue("$unit", observation.UnitCode);
            upsertObservation.Parameters.AddWithValue("$leader", observation.DriverLeader);
            upsertObservation.Parameters.AddWithValue("$terminal", observation.DriverTerminal);
            upsertObservation.Parameters.AddWithValue("$fleet", observation.FleetLeader);
            upsertObservation.Parameters.AddWithValue("$costCenter", observation.CostCenter);
            upsertObservation.Parameters.AddWithValue("$opsLob", observation.OpsLob);
            upsertObservation.Parameters.AddWithValue("$importId", importId);
            upsertObservation.ExecuteNonQuery();
        }

        using (var deleteSnapshots = connection.CreateCommand())
        {
            deleteSnapshots.Transaction = transaction;
            deleteSnapshots.CommandText = "DELETE FROM current_driver_snapshots;";
            deleteSnapshots.ExecuteNonQuery();
        }

        foreach (var snapshot in import.Drivers)
        {
            using var insertSnapshot = connection.CreateCommand();
            insertSnapshot.Transaction = transaction;
            insertSnapshot.CommandText = """
                INSERT INTO current_driver_snapshots(
                    driver_code, report_cycle_date, unit_code, driver_leader,
                    engine_hours_7d, idle_hours_7d, idle_percent_7d,
                    engine_hours_28d, idle_hours_28d, idle_percent_28d,
                    coverage_28d, is_complete_28d, source_import_id)
                VALUES (
                    $code, $cycle, $unit, $leader,
                    $engine7, $idle7, $percent7,
                    $engine28, $idle28, $percent28,
                    $coverage, $complete, $importId);
                """;
            insertSnapshot.Parameters.AddWithValue("$code", snapshot.Driver.DriverCode);
            insertSnapshot.Parameters.AddWithValue("$cycle", FormatDate(snapshot.ReportCycleDate));
            insertSnapshot.Parameters.AddWithValue("$unit", snapshot.UnitCode);
            insertSnapshot.Parameters.AddWithValue("$leader", snapshot.DriverLeader);
            insertSnapshot.Parameters.AddWithValue("$engine7", (double)snapshot.EngineHours7Day);
            insertSnapshot.Parameters.AddWithValue("$idle7", (double)snapshot.IdleHours7Day);
            insertSnapshot.Parameters.AddWithValue("$percent7", ToDbValue(snapshot.IdlePercent7Day));
            insertSnapshot.Parameters.AddWithValue("$engine28", (double)snapshot.EngineHours28Day);
            insertSnapshot.Parameters.AddWithValue("$idle28", (double)snapshot.IdleHours28Day);
            insertSnapshot.Parameters.AddWithValue("$percent28", ToDbValue(snapshot.IdlePercent28Day));
            insertSnapshot.Parameters.AddWithValue("$coverage", snapshot.Coverage28Day);
            insertSnapshot.Parameters.AddWithValue("$complete", snapshot.IsComplete28Day ? 1 : 0);
            insertSnapshot.Parameters.AddWithValue("$importId", importId);
            insertSnapshot.ExecuteNonQuery();
        }

        SetStateValue(connection, transaction, "current_report_cycle", FormatDate(import.ReportCycleDate));
        SetStateValue(connection, transaction, "last_import_file", sourceFileName);
        SetStateValue(connection, transaction, "last_import_utc", importedUtc.ToString("O", CultureInfo.InvariantCulture));
        SetStateValue(connection, transaction, "last_import_hash", sourceHash);

        transaction.Commit();
        return new ReportImportResult(true, false, importId);
    }

    public FleetState LoadFleet()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            WITH latest_contact AS (
                SELECT event.*
                FROM idle_contact_events AS event
                INNER JOIN (
                    SELECT driver_code, report_cycle_date, MAX(id) AS latest_id
                    FROM idle_contact_events
                    GROUP BY driver_code, report_cycle_date
                ) AS latest ON latest.latest_id = event.id
            )
            SELECT
                snapshot.driver_code,
                driver.driver_name,
                driver.raw_label,
                snapshot.report_cycle_date,
                snapshot.unit_code,
                snapshot.driver_leader,
                snapshot.engine_hours_7d,
                snapshot.idle_hours_7d,
                snapshot.idle_percent_7d,
                snapshot.engine_hours_28d,
                snapshot.idle_hours_28d,
                snapshot.idle_percent_28d,
                snapshot.coverage_28d,
                snapshot.is_complete_28d,
                snapshot.source_import_id,
                contact.outcome,
                COALESCE(contact.note, ''),
                contact.created_utc
            FROM current_driver_snapshots AS snapshot
            INNER JOIN drivers AS driver ON driver.driver_code = snapshot.driver_code
            LEFT JOIN latest_contact AS contact
                ON contact.driver_code = snapshot.driver_code
                AND contact.report_cycle_date = snapshot.report_cycle_date
            ORDER BY driver.driver_name COLLATE NOCASE, snapshot.driver_code COLLATE NOCASE;
            """;

        var drivers = new List<FleetDriverRecord>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                var outcomeText = reader.IsDBNull(15) ? null : reader.GetString(15);
                IdleContactOutcome? outcome = null;
                if (outcomeText is not null && Enum.TryParse<IdleContactOutcome>(outcomeText, out var parsedOutcome))
                {
                    outcome = parsedOutcome;
                }

                drivers.Add(new FleetDriverRecord(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    ParseDate(reader.GetString(3)),
                    reader.GetString(4),
                    reader.GetString(5),
                    ToDecimal(reader.GetDouble(6)),
                    ToDecimal(reader.GetDouble(7)),
                    ReadNullableDecimal(reader, 8),
                    ToDecimal(reader.GetDouble(9)),
                    ToDecimal(reader.GetDouble(10)),
                    ReadNullableDecimal(reader, 11),
                    reader.GetInt32(12),
                    reader.GetInt32(13) == 1,
                    reader.GetInt64(14),
                    outcome,
                    reader.GetString(16),
                    reader.IsDBNull(17) ? null : DateTimeOffset.Parse(reader.GetString(17), CultureInfo.InvariantCulture)));
            }
        }

        var valid7Day = drivers.Where(driver => driver.EngineHours7Day > 0m).ToArray();
        var fleet7Day = Percentage(
            valid7Day.Sum(driver => driver.IdleHours7Day),
            valid7Day.Sum(driver => driver.EngineHours7Day));

        var valid28Day = drivers
            .Where(driver => driver.IsComplete28Day && driver.EngineHours28Day > 0m)
            .ToArray();
        var fleet28Day = Percentage(
            valid28Day.Sum(driver => driver.IdleHours28Day),
            valid28Day.Sum(driver => driver.EngineHours28Day));

        var cycle = drivers.Count > 0
            ? drivers[0].ReportCycleDate
            : GetStateDate(connection, "current_report_cycle");
        var lastFile = GetStateValue(connection, "last_import_file") ?? string.Empty;
        var lastImportedText = GetStateValue(connection, "last_import_utc");
        DateTimeOffset? lastImported = DateTimeOffset.TryParse(
            lastImportedText,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var parsedImported)
            ? parsedImported
            : null;

        return new FleetState(
            cycle,
            drivers,
            fleet7Day,
            valid7Day.Length,
            fleet28Day,
            valid28Day.Length,
            lastFile,
            lastImported);
    }

    public void RecordIdleContact(
        FleetDriverRecord driver,
        IdleContactOutcome outcome,
        string? note,
        decimal threshold)
    {
        ArgumentNullException.ThrowIfNull(driver);
        if (!Enum.IsDefined(outcome))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome));
        }

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO idle_contact_events(
                driver_code, report_cycle_date, outcome, note, created_utc,
                idle_percent_7d, idle_percent_28d, coverage_28d,
                threshold_snapshot, unit_code_snapshot,
                driver_leader_snapshot, source_import_id)
            VALUES (
                $code, $cycle, $outcome, $note, $created,
                $percent7, $percent28, $coverage,
                $threshold, $unit, $leader, $importId);
            """;
        command.Parameters.AddWithValue("$code", driver.DriverCode);
        command.Parameters.AddWithValue("$cycle", FormatDate(driver.ReportCycleDate));
        command.Parameters.AddWithValue("$outcome", outcome.ToString());
        command.Parameters.AddWithValue("$note", string.IsNullOrWhiteSpace(note) ? DBNull.Value : note.Trim());
        command.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$percent7", ToDbValue(driver.IdlePercent7Day));
        command.Parameters.AddWithValue("$percent28", ToDbValue(driver.IdlePercent28Day));
        command.Parameters.AddWithValue("$coverage", driver.Coverage28Day);
        command.Parameters.AddWithValue("$threshold", (double)threshold);
        command.Parameters.AddWithValue("$unit", driver.UnitCode);
        command.Parameters.AddWithValue("$leader", driver.DriverLeader);
        command.Parameters.AddWithValue("$importId", driver.SourceImportId);
        command.ExecuteNonQuery();
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;";
        pragma.ExecuteNonQuery();
        return connection;
    }

    private static void SetStateValue(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string key,
        string value)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO app_state(key, value)
            VALUES ($key, $value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }

    private static string? GetStateValue(SqliteConnection connection, string key)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM app_state WHERE key = $key;";
        command.Parameters.AddWithValue("$key", key);
        return command.ExecuteScalar() as string;
    }

    private static DateOnly? GetStateDate(SqliteConnection connection, string key)
    {
        var value = GetStateValue(connection, key);
        return TryParseDate(value, out var parsed) ? parsed : null;
    }

    private static string FormatDate(DateOnly date) =>
        date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateOnly ParseDate(string value) =>
        DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static bool TryParseDate(string? value, out DateOnly parsed) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed);

    private static object ToDbValue(decimal? value) =>
        value is null ? DBNull.Value : (double)value.Value;

    private static decimal ToDecimal(double value) => Convert.ToDecimal(value, CultureInfo.InvariantCulture);

    private static decimal? ReadNullableDecimal(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : ToDecimal(reader.GetDouble(ordinal));

    private static decimal? Percentage(decimal numerator, decimal denominator) =>
        denominator <= 0m ? null : numerator / denominator * 100m;
}
