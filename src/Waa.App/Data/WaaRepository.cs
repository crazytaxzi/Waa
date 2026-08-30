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
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

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
        try
        {
            InitializeCore();
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"WAA could not migrate its database at '{_databasePath}'. Existing data was not replaced or discarded. {exception.Message}",
                exception);
        }
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
            insertImport.Parameters.AddWithValue("$imported", FormatUtc(importedUtc));
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
        SetStateValue(connection, transaction, "last_import_utc", FormatUtc(importedUtc));
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
            ),
            open_work AS (
                SELECT driver_code, COUNT(*) AS open_count
                FROM work_entries
                WHERE resolved_utc IS NULL
                  AND status IN ('Waiting', 'FollowUp')
                GROUP BY driver_code
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
                contact.created_utc,
                COALESCE(open_work.open_count, 0)
            FROM current_driver_snapshots AS snapshot
            INNER JOIN drivers AS driver ON driver.driver_code = snapshot.driver_code
            LEFT JOIN latest_contact AS contact
                ON contact.driver_code = snapshot.driver_code
                AND contact.report_cycle_date = snapshot.report_cycle_date
            LEFT JOIN open_work ON open_work.driver_code = snapshot.driver_code
            ORDER BY driver.driver_name COLLATE NOCASE, snapshot.driver_code COLLATE NOCASE;
            """;

        var drivers = new List<FleetDriverRecord>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                var outcome = reader.IsDBNull(15)
                    ? null
                    : ParseIdleOutcome(reader.GetString(15));

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
                    reader.IsDBNull(17) ? null : ParseUtc(reader.GetString(17)),
                    reader.GetInt32(18)));
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
            ? parsedImported.ToUniversalTime()
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

    public long RecordIdleContact(
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

        var createdUtc = DateTimeOffset.UtcNow;
        var normalizedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        long eventId;
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
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
                SELECT last_insert_rowid();
                """;
            command.Parameters.AddWithValue("$code", driver.DriverCode);
            command.Parameters.AddWithValue("$cycle", FormatDate(driver.ReportCycleDate));
            command.Parameters.AddWithValue("$outcome", outcome.ToString());
            command.Parameters.AddWithValue("$note", normalizedNote is null ? DBNull.Value : normalizedNote);
            command.Parameters.AddWithValue("$created", FormatUtc(createdUtc));
            command.Parameters.AddWithValue("$percent7", ToDbValue(driver.IdlePercent7Day));
            command.Parameters.AddWithValue("$percent28", ToDbValue(driver.IdlePercent28Day));
            command.Parameters.AddWithValue("$coverage", driver.Coverage28Day);
            command.Parameters.AddWithValue("$threshold", (double)threshold);
            command.Parameters.AddWithValue("$unit", driver.UnitCode);
            command.Parameters.AddWithValue("$leader", driver.DriverLeader);
            command.Parameters.AddWithValue("$importId", driver.SourceImportId);
            eventId = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        var mapping = MapIdleOutcome(outcome, createdUtc);
        InsertWorkEntry(
            connection,
            transaction,
            driver.DriverCode,
            BuildIdleWorkText(
                outcome,
                driver.IdlePercent28Day,
                driver.Coverage28Day,
                driver.IdlePercent7Day,
                normalizedNote),
            mapping.Status,
            createdUtc,
            mapping.ResolvedUtc,
            WorkEntrySource.IdleContact,
            eventId,
            driver.ReportCycleDate,
            driver.UnitCode,
            driver.DriverLeader);

        transaction.Commit();
        return eventId;
    }

    public long RecordManualWork(
        FleetDriverRecord driver,
        WorkEntryStatus status,
        string text,
        DateTimeOffset? createdUtc = null)
    {
        ArgumentNullException.ThrowIfNull(driver);
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        var normalizedText = NormalizeWorkText(text);
        var created = (createdUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var resolved = status == WorkEntryStatus.Done ? created : null;

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var id = InsertWorkEntry(
            connection,
            transaction,
            driver.DriverCode,
            normalizedText,
            status,
            created,
            resolved,
            WorkEntrySource.Manual,
            null,
            driver.ReportCycleDate,
            driver.UnitCode,
            driver.DriverLeader);
        transaction.Commit();
        return id;
    }

    public bool ResolveWorkEntry(long workEntryId, DateTimeOffset? resolvedUtc = null)
    {
        if (workEntryId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(workEntryId));
        }

        var resolved = (resolvedUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE work_entries
            SET resolved_utc = $resolved
            WHERE id = $id
              AND resolved_utc IS NULL
              AND status IN ('Waiting', 'FollowUp');
            """;
        command.Parameters.AddWithValue("$resolved", FormatUtc(resolved));
        command.Parameters.AddWithValue("$id", workEntryId);
        return command.ExecuteNonQuery() == 1;
    }

    public bool ReopenWorkEntry(long workEntryId)
    {
        if (workEntryId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(workEntryId));
        }

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE work_entries
            SET resolved_utc = NULL
            WHERE id = $id
              AND resolved_utc IS NOT NULL
              AND status IN ('Waiting', 'FollowUp');
            """;
        command.Parameters.AddWithValue("$id", workEntryId);
        return command.ExecuteNonQuery() == 1;
    }

    public DriverWorkState LoadDriverWork(
        string driverCode,
        DateTimeOffset localDayStartUtc,
        DateTimeOffset localDayEndUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(driverCode);
        ValidateTimeRange(localDayStartUtc, localDayEndUtc);

        using var connection = OpenConnection();

        IReadOnlyList<WorkEntryRecord> openEntries;
        using (var openCommand = connection.CreateCommand())
        {
            openCommand.CommandText = WorkEntrySelect + """
                WHERE work.driver_code = $code
                  AND work.resolved_utc IS NULL
                  AND work.status IN ('Waiting', 'FollowUp')
                ORDER BY work.created_utc, work.id;
                """;
            openCommand.Parameters.AddWithValue("$code", driverCode);
            openEntries = ReadWorkEntries(openCommand);
        }

        IReadOnlyList<WorkEntryRecord> todayEntries;
        using (var todayCommand = connection.CreateCommand())
        {
            todayCommand.CommandText = WorkEntrySelect + """
                WHERE work.driver_code = $code
                  AND (
                      (work.created_utc >= $startUtc AND work.created_utc < $endUtc)
                      OR
                      (work.resolved_utc >= $startUtc AND work.resolved_utc < $endUtc)
                  )
                ORDER BY
                    CASE
                        WHEN work.resolved_utc >= $startUtc AND work.resolved_utc < $endUtc
                            THEN work.resolved_utc
                        ELSE work.created_utc
                    END DESC,
                    work.id DESC;
                """;
            todayCommand.Parameters.AddWithValue("$code", driverCode);
            todayCommand.Parameters.AddWithValue("$startUtc", FormatUtc(localDayStartUtc));
            todayCommand.Parameters.AddWithValue("$endUtc", FormatUtc(localDayEndUtc));
            todayEntries = ReadWorkEntries(todayCommand);
        }

        return new DriverWorkState(openEntries, todayEntries);
    }

    public IReadOnlyList<WorkEntryRecord> LoadHandoffEntries(
        DateTimeOffset localDayStartUtc,
        DateTimeOffset localDayEndUtc)
    {
        ValidateTimeRange(localDayStartUtc, localDayEndUtc);

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = WorkEntrySelect + """
            WHERE
                (work.resolved_utc IS NULL AND work.status IN ('Waiting', 'FollowUp'))
                OR
                (work.status = 'Done' AND work.created_utc >= $startUtc AND work.created_utc < $endUtc)
                OR
                (work.resolved_utc >= $startUtc AND work.resolved_utc < $endUtc);
            """;
        command.Parameters.AddWithValue("$startUtc", FormatUtc(localDayStartUtc));
        command.Parameters.AddWithValue("$endUtc", FormatUtc(localDayEndUtc));
        return ReadWorkEntries(command);
    }

    public WorkEntryRecord? GetWorkEntry(long workEntryId)
    {
        if (workEntryId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(workEntryId));
        }

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = WorkEntrySelect + " WHERE work.id = $id LIMIT 1;";
        command.Parameters.AddWithValue("$id", workEntryId);
        return ReadSingleWorkEntry(command);
    }

    public WorkEntryRecord? GetWorkEntryForIdleContact(long idleContactEventId)
    {
        if (idleContactEventId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(idleContactEventId));
        }

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = WorkEntrySelect + " WHERE work.linked_idle_contact_event_id = $eventId LIMIT 1;";
        command.Parameters.AddWithValue("$eventId", idleContactEventId);
        return ReadSingleWorkEntry(command);
    }

    private void InitializeCore()
    {
        var parent = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        using var connection = OpenConnection();
        using (var pragmas = connection.CreateCommand())
        {
            pragmas.CommandText = "PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL;";
            pragmas.ExecuteNonQuery();
        }

        using var transaction = connection.BeginTransaction();
        using (var schema = connection.CreateCommand())
        {
            schema.Transaction = transaction;
            schema.CommandText = """
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

                CREATE TABLE IF NOT EXISTS work_entries (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    driver_code TEXT NOT NULL COLLATE NOCASE,
                    text TEXT NOT NULL CHECK (length(trim(text)) > 0),
                    status TEXT NOT NULL CHECK (status IN ('Done', 'Waiting', 'FollowUp')),
                    created_utc TEXT NOT NULL,
                    resolved_utc TEXT NULL,
                    source TEXT NOT NULL CHECK (source IN ('Manual', 'IdleContact')),
                    linked_idle_contact_event_id INTEGER NULL,
                    report_cycle_date_snapshot TEXT NULL,
                    unit_code_snapshot TEXT NOT NULL,
                    driver_leader_snapshot TEXT NOT NULL,
                    FOREIGN KEY (driver_code) REFERENCES drivers(driver_code),
                    FOREIGN KEY (linked_idle_contact_event_id) REFERENCES idle_contact_events(id)
                );

                CREATE INDEX IF NOT EXISTS ix_observations_week
                    ON weekly_observations(week_date, driver_code);
                CREATE INDEX IF NOT EXISTS ix_contacts_cycle_driver
                    ON idle_contact_events(report_cycle_date, driver_code, id DESC);
                CREATE INDEX IF NOT EXISTS ix_work_entries_driver_history
                    ON work_entries(driver_code, created_utc DESC, id DESC);
                CREATE INDEX IF NOT EXISTS ix_work_entries_open_driver
                    ON work_entries(driver_code, status, created_utc)
                    WHERE resolved_utc IS NULL;
                CREATE INDEX IF NOT EXISTS ix_work_entries_created_time
                    ON work_entries(created_utc, driver_code);
                CREATE INDEX IF NOT EXISTS ix_work_entries_resolved_time
                    ON work_entries(resolved_utc, driver_code)
                    WHERE resolved_utc IS NOT NULL;
                CREATE UNIQUE INDEX IF NOT EXISTS ux_work_entries_linked_idle_contact
                    ON work_entries(linked_idle_contact_event_id)
                    WHERE linked_idle_contact_event_id IS NOT NULL;
                """;
            schema.ExecuteNonQuery();
        }

        using (var setting = connection.CreateCommand())
        {
            setting.Transaction = transaction;
            setting.CommandText = """
                INSERT INTO settings(key, value)
                VALUES ('idle_threshold', $value)
                ON CONFLICT(key) DO NOTHING;
                """;
            setting.Parameters.AddWithValue("$value", DefaultIdleThreshold.ToString(CultureInfo.InvariantCulture));
            setting.ExecuteNonQuery();
        }

        BackfillIdleContactWorkEntries(connection, transaction);

        using (var version = connection.CreateCommand())
        {
            version.Transaction = transaction;
            version.CommandText = "PRAGMA user_version = 2;";
            version.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private static void BackfillIdleContactWorkEntries(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        var events = new List<LegacyIdleContactEvent>();
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT
                    event.id,
                    event.driver_code,
                    event.report_cycle_date,
                    event.outcome,
                    event.note,
                    event.created_utc,
                    event.idle_percent_7d,
                    event.idle_percent_28d,
                    event.coverage_28d,
                    event.unit_code_snapshot,
                    event.driver_leader_snapshot
                FROM idle_contact_events AS event
                LEFT JOIN work_entries AS work
                    ON work.linked_idle_contact_event_id = event.id
                WHERE work.id IS NULL
                ORDER BY event.id;
                """;

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                events.Add(new LegacyIdleContactEvent(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    ParseDate(reader.GetString(2)),
                    ParseIdleOutcome(reader.GetString(3)),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    ParseUtc(reader.GetString(5)),
                    ReadNullableDecimal(reader, 6),
                    ReadNullableDecimal(reader, 7),
                    reader.GetInt32(8),
                    reader.GetString(9),
                    reader.GetString(10)));
            }
        }

        foreach (var contact in events)
        {
            var mapping = MapIdleOutcome(contact.Outcome, contact.CreatedUtc);
            InsertWorkEntry(
                connection,
                transaction,
                contact.DriverCode,
                BuildIdleWorkText(
                    contact.Outcome,
                    contact.IdlePercent28Day,
                    contact.Coverage28Day,
                    contact.IdlePercent7Day,
                    contact.Note),
                mapping.Status,
                contact.CreatedUtc,
                mapping.ResolvedUtc,
                WorkEntrySource.IdleContact,
                contact.Id,
                contact.ReportCycleDate,
                contact.UnitCodeSnapshot,
                contact.DriverLeaderSnapshot);
        }
    }

    private static long InsertWorkEntry(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string driverCode,
        string text,
        WorkEntryStatus status,
        DateTimeOffset createdUtc,
        DateTimeOffset? resolvedUtc,
        WorkEntrySource source,
        long? linkedIdleContactEventId,
        DateOnly? reportCycleDateSnapshot,
        string unitCodeSnapshot,
        string driverLeaderSnapshot)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO work_entries(
                driver_code,
                text,
                status,
                created_utc,
                resolved_utc,
                source,
                linked_idle_contact_event_id,
                report_cycle_date_snapshot,
                unit_code_snapshot,
                driver_leader_snapshot)
            VALUES (
                $driverCode,
                $text,
                $status,
                $createdUtc,
                $resolvedUtc,
                $source,
                $linkedIdleContactEventId,
                $reportCycleDateSnapshot,
                $unitCodeSnapshot,
                $driverLeaderSnapshot);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$driverCode", driverCode);
        command.Parameters.AddWithValue("$text", NormalizeWorkText(text));
        command.Parameters.AddWithValue("$status", status.ToString());
        command.Parameters.AddWithValue("$createdUtc", FormatUtc(createdUtc));
        command.Parameters.AddWithValue(
            "$resolvedUtc",
            resolvedUtc is null ? DBNull.Value : FormatUtc(resolvedUtc.Value));
        command.Parameters.AddWithValue("$source", source.ToString());
        command.Parameters.AddWithValue(
            "$linkedIdleContactEventId",
            linkedIdleContactEventId is null ? DBNull.Value : linkedIdleContactEventId.Value);
        command.Parameters.AddWithValue(
            "$reportCycleDateSnapshot",
            reportCycleDateSnapshot is null ? DBNull.Value : FormatDate(reportCycleDateSnapshot.Value));
        command.Parameters.AddWithValue("$unitCodeSnapshot", unitCodeSnapshot ?? string.Empty);
        command.Parameters.AddWithValue("$driverLeaderSnapshot", driverLeaderSnapshot ?? string.Empty);
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static IReadOnlyList<WorkEntryRecord> ReadWorkEntries(SqliteCommand command)
    {
        var entries = new List<WorkEntryRecord>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            entries.Add(ReadWorkEntry(reader));
        }

        return entries;
    }

    private static WorkEntryRecord? ReadSingleWorkEntry(SqliteCommand command)
    {
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadWorkEntry(reader) : null;
    }

    private static WorkEntryRecord ReadWorkEntry(SqliteDataReader reader)
    {
        var statusText = reader.GetString(4);
        if (!Enum.TryParse<WorkEntryStatus>(statusText, out var status) || !Enum.IsDefined(status))
        {
            throw new InvalidDataException($"Database work entry has unknown status '{statusText}'.");
        }

        var sourceText = reader.GetString(7);
        if (!Enum.TryParse<WorkEntrySource>(sourceText, out var source) || !Enum.IsDefined(source))
        {
            throw new InvalidDataException($"Database work entry has unknown source '{sourceText}'.");
        }

        return new WorkEntryRecord(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            status,
            ParseUtc(reader.GetString(5)),
            reader.IsDBNull(6) ? null : ParseUtc(reader.GetString(6)),
            source,
            reader.IsDBNull(8) ? null : reader.GetInt64(8),
            reader.IsDBNull(9) ? null : ParseDate(reader.GetString(9)),
            reader.GetString(10),
            reader.GetString(11));
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

    private static string BuildIdleWorkText(
        IdleContactOutcome outcome,
        decimal? idlePercent28Day,
        int coverage28Day,
        decimal? idlePercent7Day,
        string? note)
    {
        var action = outcome switch
        {
            IdleContactOutcome.Spoke => "Spoke with driver regarding idle",
            IdleContactOutcome.Attempted => "Attempted idle contact — driver not reached",
            IdleContactOutcome.SpokeFollowUp => "Spoke with driver regarding idle; follow-up required",
            _ => throw new ArgumentOutOfRangeException(nameof(outcome))
        };

        var twentyEightDay = coverage28Day < 4
            ? $"28D incomplete {coverage28Day.ToString(CultureInfo.InvariantCulture)}/4"
            : idlePercent28Day is null
                ? "28D N/A"
                : $"28D {idlePercent28Day.Value.ToString("0.0", CultureInfo.InvariantCulture)}%";
        var sevenDay = idlePercent7Day is null
            ? "7D N/A"
            : $"7D {idlePercent7Day.Value.ToString("0.0", CultureInfo.InvariantCulture)}%";
        var text = $"{action} — {twentyEightDay}, {sevenDay}.";

        if (string.IsNullOrWhiteSpace(note))
        {
            return text;
        }

        return $"{text} Note: {EnsureSentence(note.Trim())}";
    }

    private static string EnsureSentence(string value) =>
        value.EndsWith('.', StringComparison.Ordinal) ||
        value.EndsWith('!', StringComparison.Ordinal) ||
        value.EndsWith('?', StringComparison.Ordinal)
            ? value
            : value + ".";

    private static (WorkEntryStatus Status, DateTimeOffset? ResolvedUtc) MapIdleOutcome(
        IdleContactOutcome outcome,
        DateTimeOffset createdUtc) =>
        outcome == IdleContactOutcome.Spoke
            ? (WorkEntryStatus.Done, createdUtc)
            : (WorkEntryStatus.FollowUp, null);

    private static IdleContactOutcome ParseIdleOutcome(string value) =>
        Enum.TryParse<IdleContactOutcome>(value, out var outcome) && Enum.IsDefined(outcome)
            ? outcome
            : throw new InvalidDataException($"Database idle contact has unknown outcome '{value}'.");

    private static string NormalizeWorkText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Work text cannot be blank.", nameof(text));
        }

        return text.Trim();
    }

    private static void ValidateTimeRange(DateTimeOffset startUtc, DateTimeOffset endUtc)
    {
        if (endUtc <= startUtc)
        {
            throw new ArgumentException("The local-day end must be later than its start.");
        }
    }

    private static string FormatDate(DateOnly date) =>
        date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateOnly ParseDate(string value) =>
        DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static bool TryParseDate(string? value, out DateOnly parsed) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed);

    private static string FormatUtc(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseUtc(string value) =>
        DateTimeOffset.Parse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind).ToUniversalTime();

    private static object ToDbValue(decimal? value) =>
        value is null ? DBNull.Value : (double)value.Value;

    private static decimal ToDecimal(double value) => Convert.ToDecimal(value, CultureInfo.InvariantCulture);

    private static decimal? ReadNullableDecimal(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : ToDecimal(reader.GetDouble(ordinal));

    private static decimal? Percentage(decimal numerator, decimal denominator) =>
        denominator <= 0m ? null : numerator / denominator * 100m;

    private const string WorkEntrySelect = """
        SELECT
            work.id,
            work.driver_code,
            driver.driver_name,
            work.text,
            work.status,
            work.created_utc,
            work.resolved_utc,
            work.source,
            work.linked_idle_contact_event_id,
            work.report_cycle_date_snapshot,
            work.unit_code_snapshot,
            work.driver_leader_snapshot
        FROM work_entries AS work
        INNER JOIN drivers AS driver ON driver.driver_code = work.driver_code
        """;

    private sealed record LegacyIdleContactEvent(
        long Id,
        string DriverCode,
        DateOnly ReportCycleDate,
        IdleContactOutcome Outcome,
        string? Note,
        DateTimeOffset CreatedUtc,
        decimal? IdlePercent7Day,
        decimal? IdlePercent28Day,
        int Coverage28Day,
        string UnitCodeSnapshot,
        string DriverLeaderSnapshot);
}
