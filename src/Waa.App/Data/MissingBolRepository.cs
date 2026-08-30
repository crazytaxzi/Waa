using System.Globalization;
using Microsoft.Data.Sqlite;
using Waa.Core;

namespace Waa.App.Data;

public sealed class MissingBolRepository
{
    private readonly string _databasePath;
    private readonly string _connectionString;

    public MissingBolRepository(string databasePath)
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
                $"WAA could not migrate Missing BOL data at '{_databasePath}'. Existing data was not replaced or discarded. {exception.Message}",
                exception);
        }
    }

    public bool IsHashAccepted(string sourceHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceHash);
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM missing_bol_imports WHERE source_hash = $hash);";
        command.Parameters.AddWithValue("$hash", sourceHash);
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) == 1;
    }

    public MissingBolImportResult ImportWorkbook(
        MissingBolWorkbookImport import,
        string sourceFileName,
        string sourcePath,
        string sourceHash,
        DateTime sourceLastWriteUtc,
        DateTimeOffset? importedUtc = null)
    {
        ArgumentNullException.ThrowIfNull(import);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceHash);

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        using (var accepted = connection.CreateCommand())
        {
            accepted.Transaction = transaction;
            accepted.CommandText = "SELECT id FROM missing_bol_imports WHERE source_hash = $hash LIMIT 1;";
            accepted.Parameters.AddWithValue("$hash", sourceHash);
            var existingId = accepted.ExecuteScalar();
            if (existingId is not null)
            {
                return new MissingBolImportResult(
                    false,
                    true,
                    Convert.ToInt64(existingId, CultureInfo.InvariantCulture),
                    import.Items.Count,
                    0);
            }
        }

        var existingItems = LoadExistingItems(connection, transaction);
        ValidateSourceAssociations(import.Items, existingItems);

        var imported = (importedUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();
        long importId;
        using (var insertImport = connection.CreateCommand())
        {
            insertImport.Transaction = transaction;
            insertImport.CommandText = """
                INSERT INTO missing_bol_imports(
                    source_file_name,
                    source_path,
                    source_hash,
                    source_last_write_utc,
                    imported_utc,
                    row_count)
                VALUES ($fileName, $path, $hash, $lastWrite, $imported, $rowCount);
                SELECT last_insert_rowid();
                """;
            insertImport.Parameters.AddWithValue("$fileName", sourceFileName);
            insertImport.Parameters.AddWithValue("$path", sourcePath);
            insertImport.Parameters.AddWithValue("$hash", sourceHash);
            insertImport.Parameters.AddWithValue(
                "$lastWrite",
                new DateTimeOffset(DateTime.SpecifyKind(sourceLastWriteUtc, DateTimeKind.Utc)).ToString(
                    "O",
                    CultureInfo.InvariantCulture));
            insertImport.Parameters.AddWithValue("$imported", FormatUtc(imported));
            insertImport.Parameters.AddWithValue("$rowCount", import.Items.Count);
            importId = Convert.ToInt64(insertImport.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        using (var markAbsent = connection.CreateCommand())
        {
            markAbsent.Transaction = transaction;
            markAbsent.CommandText = "UPDATE missing_bol_items SET is_present_in_latest_import = 0;";
            markAbsent.ExecuteNonQuery();
        }

        var driverCache = new Dictionary<string, DriverContext?>(StringComparer.OrdinalIgnoreCase);
        var createdTasks = 0;
        foreach (var source in import.Items)
        {
            existingItems.TryGetValue(source.NormalizedOrderNumber, out var existing);
            var matchedContext = existing?.MatchedDriverCode is { Length: > 0 } matchedCode
                ? GetDriverContext(connection, transaction, matchedCode, driverCache)
                : GetDriverContext(
                    connection,
                    transaction,
                    source.NormalizedSourceDriverCode,
                    driverCache);

            if (existing is null)
            {
                var itemId = InsertItem(
                    connection,
                    transaction,
                    source,
                    matchedContext?.DriverCode,
                    importId,
                    imported);
                if (matchedContext is not null)
                {
                    CreateTask(
                        connection,
                        transaction,
                        itemId,
                        source,
                        MissingBolStatus.Open,
                        matchedContext,
                        importId,
                        imported);
                    createdTasks++;
                }

                continue;
            }

            var matchedDriverCode = existing.MatchedDriverCode ?? matchedContext?.DriverCode;
            UpdateItem(
                connection,
                transaction,
                existing,
                source,
                matchedDriverCode,
                importId,
                imported);

            var taskWorkEntryId = existing.TaskWorkEntryId;
            if (taskWorkEntryId is null &&
                matchedContext is not null &&
                existing.Status != MissingBolStatus.Resolved)
            {
                taskWorkEntryId = CreateTask(
                    connection,
                    transaction,
                    existing.Id,
                    source,
                    existing.Status,
                    matchedContext,
                    importId,
                    imported);
                createdTasks++;
            }

            if (taskWorkEntryId is not null && existing.Status != MissingBolStatus.Resolved)
            {
                UpdateTaskText(
                    connection,
                    transaction,
                    taskWorkEntryId.Value,
                    BuildTaskText(source, existing.Status));
            }
        }

        transaction.Commit();
        return new MissingBolImportResult(true, false, importId, import.Items.Count, createdTasks);
    }

    public int AttachExactMatchesAndCreateTasks(DateTimeOffset? createdUtc = null)
    {
        var created = (createdUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var candidates = new List<AttachCandidate>();

        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT
                    item.id,
                    item.normalized_order_number,
                    item.source_order_number,
                    item.tmex_order_number,
                    item.logistics_order_number,
                    item.bill_to,
                    item.division_code,
                    item.empty_call_date,
                    item.origin_city_state,
                    item.destination_city_state,
                    item.revenue_type,
                    item.terminal,
                    item.source_driver_leader,
                    item.source_driver_status,
                    item.source_driver_code,
                    item.normalized_source_driver_code,
                    item.source_driver_name,
                    item.loaded_miles,
                    item.order_level_miles,
                    item.current_status,
                    item.task_work_entry_id,
                    item.last_seen_import_id,
                    driver.driver_code,
                    driver.driver_name,
                    driver.current_unit_code,
                    driver.current_driver_leader,
                    COALESCE(snapshot.report_cycle_date, driver.last_seen_cycle)
                FROM missing_bol_items AS item
                INNER JOIN drivers AS driver
                    ON driver.driver_code = item.normalized_source_driver_code COLLATE NOCASE
                LEFT JOIN current_driver_snapshots AS snapshot
                    ON snapshot.driver_code = driver.driver_code
                WHERE item.matched_driver_code IS NULL
                  AND length(item.normalized_source_driver_code) > 0
                ORDER BY item.id;
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                candidates.Add(new AttachCandidate(
                    ReadSourceItem(reader),
                    ParseMissingBolStatus(reader.GetString(19)),
                    reader.IsDBNull(20) ? null : reader.GetInt64(20),
                    reader.GetInt64(21),
                    new DriverContext(
                        reader.GetString(22),
                        reader.GetString(23),
                        reader.GetString(24),
                        reader.GetString(25),
                        ParseDate(reader.GetString(26)))));
            }
        }

        var createdTasks = 0;
        foreach (var candidate in candidates)
        {
            using (var attach = connection.CreateCommand())
            {
                attach.Transaction = transaction;
                attach.CommandText = """
                    UPDATE missing_bol_items
                    SET matched_driver_code = $driverCode
                    WHERE id = $id
                      AND matched_driver_code IS NULL;
                    """;
                attach.Parameters.AddWithValue("$driverCode", candidate.Driver.DriverCode);
                attach.Parameters.AddWithValue("$id", candidate.Source.Id);
                if (attach.ExecuteNonQuery() != 1)
                {
                    continue;
                }
            }

            if (candidate.TaskWorkEntryId is null && candidate.Status != MissingBolStatus.Resolved)
            {
                CreateTask(
                    connection,
                    transaction,
                    candidate.Source.Id,
                    candidate.Source.ToCoreItem(),
                    candidate.Status,
                    candidate.Driver,
                    candidate.SourceImportId,
                    created);
                createdTasks++;
            }
        }

        transaction.Commit();
        return createdTasks;
    }

    public long RecordAction(
        long missingBolItemId,
        MissingBolActionOutcome outcome,
        string? note,
        DateTimeOffset? createdUtc = null)
    {
        if (missingBolItemId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(missingBolItemId));
        }

        if (!Enum.IsDefined(outcome))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome));
        }

        var created = (createdUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var normalizedNote = string.IsNullOrWhiteSpace(note) ? string.Empty : note.Trim();
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var item = LoadActionItem(connection, transaction, missingBolItemId)
            ?? throw new InvalidOperationException("The Missing BOL item no longer exists.");
        if (item.MatchedDriverCode is null)
        {
            throw new InvalidOperationException(
                "This Missing BOL item is unmatched. It cannot create driver-owned work until the exact Driver Code exists in WAA.");
        }

        if (item.TaskWorkEntryId is null)
        {
            throw new InvalidOperationException("The matched Missing BOL item has no linked task.");
        }

        if (outcome == MissingBolActionOutcome.Reopen)
        {
            if (item.Status != MissingBolStatus.Resolved)
            {
                throw new InvalidOperationException("Only a resolved Missing BOL item can be reopened.");
            }
        }
        else if (item.Status == MissingBolStatus.Resolved)
        {
            throw new InvalidOperationException("Reopen the resolved Missing BOL item before recording another outcome.");
        }

        var newStatus = outcome switch
        {
            MissingBolActionOutcome.Requested => MissingBolStatus.Requested,
            MissingBolActionOutcome.Attempted => MissingBolStatus.Attempted,
            MissingBolActionOutcome.FollowUp => MissingBolStatus.FollowUp,
            MissingBolActionOutcome.Resolved => MissingBolStatus.Resolved,
            MissingBolActionOutcome.Reopen => MissingBolStatus.Open,
            _ => throw new ArgumentOutOfRangeException(nameof(outcome))
        };
        DateTimeOffset? resolvedUtc = newStatus == MissingBolStatus.Resolved ? created : null;

        using (var updateItem = connection.CreateCommand())
        {
            updateItem.Transaction = transaction;
            updateItem.CommandText = """
                UPDATE missing_bol_items
                SET current_status = $status,
                    resolved_utc = $resolvedUtc
                WHERE id = $id;
                """;
            updateItem.Parameters.AddWithValue("$status", newStatus.ToString());
            updateItem.Parameters.AddWithValue(
                "$resolvedUtc",
                resolvedUtc is null ? DBNull.Value : FormatUtc(resolvedUtc.Value));
            updateItem.Parameters.AddWithValue("$id", item.Id);
            updateItem.ExecuteNonQuery();
        }

        using (var updateTask = connection.CreateCommand())
        {
            updateTask.Transaction = transaction;
            updateTask.CommandText = """
                UPDATE work_entries
                SET text = $text,
                    resolved_utc = $resolvedUtc
                WHERE id = $id;
                """;
            updateTask.Parameters.AddWithValue("$text", BuildTaskText(item.ToCoreItem(), newStatus));
            updateTask.Parameters.AddWithValue(
                "$resolvedUtc",
                resolvedUtc is null ? DBNull.Value : FormatUtc(resolvedUtc.Value));
            updateTask.Parameters.AddWithValue("$id", item.TaskWorkEntryId.Value);
            if (updateTask.ExecuteNonQuery() != 1)
            {
                throw new InvalidOperationException("The linked Missing BOL task no longer exists.");
            }
        }

        var activityText = BuildActionText(outcome, item.SourceOrderNumber, normalizedNote);
        var activityWorkEntryId = InsertCompletedActivity(
            connection,
            transaction,
            item,
            activityText,
            created);

        long actionEventId;
        using (var insertAction = connection.CreateCommand())
        {
            insertAction.Transaction = transaction;
            insertAction.CommandText = """
                INSERT INTO missing_bol_action_events(
                    missing_bol_item_id,
                    outcome,
                    note,
                    created_utc,
                    linked_work_entry_id,
                    driver_code_snapshot,
                    unit_code_snapshot,
                    driver_leader_snapshot,
                    source_import_id)
                VALUES (
                    $itemId,
                    $outcome,
                    $note,
                    $created,
                    $workEntryId,
                    $driverCode,
                    $unitCode,
                    $driverLeader,
                    $sourceImportId);
                SELECT last_insert_rowid();
                """;
            insertAction.Parameters.AddWithValue("$itemId", item.Id);
            insertAction.Parameters.AddWithValue("$outcome", outcome.ToString());
            insertAction.Parameters.AddWithValue(
                "$note",
                normalizedNote.Length == 0 ? DBNull.Value : normalizedNote);
            insertAction.Parameters.AddWithValue("$created", FormatUtc(created));
            insertAction.Parameters.AddWithValue("$workEntryId", activityWorkEntryId);
            insertAction.Parameters.AddWithValue("$driverCode", item.MatchedDriverCode);
            insertAction.Parameters.AddWithValue("$unitCode", item.UnitCode);
            insertAction.Parameters.AddWithValue("$driverLeader", item.DriverLeader);
            insertAction.Parameters.AddWithValue("$sourceImportId", item.LastSeenImportId);
            actionEventId = Convert.ToInt64(insertAction.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        transaction.Commit();
        return actionEventId;
    }

    public MissingBolFleetState LoadFleetState()
    {
        using var connection = OpenConnection();
        var summaries = new Dictionary<string, MissingBolDriverSummary>(StringComparer.OrdinalIgnoreCase);
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT
                    matched_driver_code,
                    SUM(CASE WHEN current_status <> 'Resolved' THEN 1 ELSE 0 END) AS open_count,
                    MIN(CASE WHEN current_status <> 'Resolved' THEN empty_call_date ELSE NULL END) AS oldest_open,
                    GROUP_CONCAT(source_order_number, ' ') AS order_search
                FROM missing_bol_items
                WHERE matched_driver_code IS NOT NULL
                GROUP BY matched_driver_code;
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var driverCode = reader.GetString(0);
                summaries[driverCode] = new MissingBolDriverSummary(
                    reader.GetInt32(1),
                    reader.IsDBNull(2) ? null : ParseDate(reader.GetString(2)),
                    reader.IsDBNull(3) ? string.Empty : reader.GetString(3));
            }
        }

        var unmatched = new List<MissingBolUnmatchedRecord>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT
                    id,
                    source_order_number,
                    empty_call_date,
                    source_driver_code,
                    source_driver_name,
                    origin_city_state,
                    destination_city_state,
                    is_present_in_latest_import
                FROM missing_bol_items
                WHERE matched_driver_code IS NULL
                  AND current_status <> 'Resolved'
                ORDER BY empty_call_date, source_order_number COLLATE NOCASE, id;
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                unmatched.Add(new MissingBolUnmatchedRecord(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    ParseDate(reader.GetString(2)),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetString(6),
                    reader.GetInt32(7) == 1));
            }
        }

        return new MissingBolFleetState(
            summaries,
            summaries.Values.Sum(summary => summary.OpenCount),
            unmatched);
    }

    public IReadOnlyList<MissingBolItemRecord> LoadDriverItems(string driverCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(driverCode);
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = MissingBolItemSelect + """
            WHERE item.matched_driver_code = $driverCode COLLATE NOCASE
            ORDER BY
                CASE WHEN item.current_status = 'Resolved' THEN 1 ELSE 0 END,
                item.empty_call_date,
                item.source_order_number COLLATE NOCASE,
                item.id;
            """;
        command.Parameters.AddWithValue("$driverCode", driverCode.Trim());
        return ReadItems(command);
    }

    public MissingBolItemRecord? GetItemByOrder(string orderNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderNumber);
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = MissingBolItemSelect +
            " WHERE item.normalized_order_number = $order COLLATE NOCASE LIMIT 1;";
        command.Parameters.AddWithValue("$order", MissingBolText.NormalizeExact(orderNumber));
        return ReadSingleItem(command);
    }

    public long? GetTaskWorkEntryId(long missingBolItemId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT task_work_entry_id FROM missing_bol_items WHERE id = $id;";
        command.Parameters.AddWithValue("$id", missingBolItemId);
        var value = command.ExecuteScalar();
        return value is null or DBNull
            ? null
            : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    public IReadOnlyList<MissingBolActionRecord> LoadActionHistory(long missingBolItemId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                id,
                missing_bol_item_id,
                outcome,
                COALESCE(note, ''),
                created_utc,
                linked_work_entry_id,
                driver_code_snapshot,
                unit_code_snapshot,
                driver_leader_snapshot,
                source_import_id
            FROM missing_bol_action_events
            WHERE missing_bol_item_id = $itemId
            ORDER BY id;
            """;
        command.Parameters.AddWithValue("$itemId", missingBolItemId);
        var actions = new List<MissingBolActionRecord>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            actions.Add(new MissingBolActionRecord(
                reader.GetInt64(0),
                reader.GetInt64(1),
                ParseMissingBolAction(reader.GetString(2)),
                reader.GetString(3),
                ParseUtc(reader.GetString(4)),
                reader.GetInt64(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.GetString(7),
                reader.GetString(8),
                reader.GetInt64(9)));
        }

        return actions;
    }

    public IReadOnlyList<WorkEntryRecord> ApplyWorkSources(IEnumerable<WorkEntryRecord> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var materialized = entries.ToArray();
        if (materialized.Length == 0)
        {
            return materialized;
        }

        var ids = materialized.Select(entry => entry.Id).ToHashSet();
        var sources = new Dictionary<long, WorkEntrySource>();
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT work_entry_id, source_kind FROM missing_bol_work_links;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var id = reader.GetInt64(0);
            if (ids.Contains(id))
            {
                sources[id] = ParseWorkEntrySource(reader.GetString(1));
            }
        }

        return materialized
            .Select(entry => sources.TryGetValue(entry.Id, out var source)
                ? entry with { Source = source }
                : entry)
            .ToArray();
    }

    private void InitializeCore()
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        using (var schema = connection.CreateCommand())
        {
            schema.Transaction = transaction;
            schema.CommandText = """
                CREATE TABLE IF NOT EXISTS missing_bol_imports (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    source_file_name TEXT NOT NULL,
                    source_path TEXT NOT NULL,
                    source_hash TEXT NOT NULL UNIQUE,
                    source_last_write_utc TEXT NOT NULL,
                    imported_utc TEXT NOT NULL,
                    row_count INTEGER NOT NULL CHECK (row_count >= 0)
                );

                CREATE TABLE IF NOT EXISTS missing_bol_items (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    normalized_order_number TEXT NOT NULL UNIQUE COLLATE NOCASE,
                    source_order_number TEXT NOT NULL,
                    tmex_order_number TEXT NOT NULL,
                    logistics_order_number TEXT NOT NULL,
                    bill_to TEXT NOT NULL,
                    division_code TEXT NOT NULL,
                    empty_call_date TEXT NOT NULL,
                    origin_city_state TEXT NOT NULL,
                    destination_city_state TEXT NOT NULL,
                    revenue_type TEXT NOT NULL,
                    terminal TEXT NOT NULL,
                    source_driver_leader TEXT NOT NULL,
                    source_driver_status TEXT NOT NULL,
                    source_driver_code TEXT NOT NULL,
                    normalized_source_driver_code TEXT NOT NULL COLLATE NOCASE,
                    source_driver_name TEXT NOT NULL,
                    loaded_miles REAL NULL,
                    order_level_miles REAL NULL,
                    matched_driver_code TEXT NULL COLLATE NOCASE,
                    current_status TEXT NOT NULL CHECK (current_status IN ('Open', 'Requested', 'Attempted', 'FollowUp', 'Resolved')),
                    first_seen_import_id INTEGER NOT NULL,
                    last_seen_import_id INTEGER NOT NULL,
                    first_seen_utc TEXT NOT NULL,
                    last_seen_utc TEXT NOT NULL,
                    is_present_in_latest_import INTEGER NOT NULL CHECK (is_present_in_latest_import IN (0, 1)),
                    resolved_utc TEXT NULL,
                    task_work_entry_id INTEGER NULL UNIQUE,
                    returned_after_resolution INTEGER NOT NULL DEFAULT 0 CHECK (returned_after_resolution IN (0, 1)),
                    FOREIGN KEY (matched_driver_code) REFERENCES drivers(driver_code),
                    FOREIGN KEY (first_seen_import_id) REFERENCES missing_bol_imports(id),
                    FOREIGN KEY (last_seen_import_id) REFERENCES missing_bol_imports(id),
                    FOREIGN KEY (task_work_entry_id) REFERENCES work_entries(id)
                );

                CREATE TABLE IF NOT EXISTS missing_bol_work_links (
                    work_entry_id INTEGER PRIMARY KEY,
                    missing_bol_item_id INTEGER NOT NULL,
                    source_kind TEXT NOT NULL CHECK (source_kind IN ('MissingBolTask', 'MissingBolAction')),
                    source_import_id INTEGER NOT NULL,
                    FOREIGN KEY (work_entry_id) REFERENCES work_entries(id),
                    FOREIGN KEY (missing_bol_item_id) REFERENCES missing_bol_items(id),
                    FOREIGN KEY (source_import_id) REFERENCES missing_bol_imports(id)
                );

                CREATE TABLE IF NOT EXISTS missing_bol_action_events (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    missing_bol_item_id INTEGER NOT NULL,
                    outcome TEXT NOT NULL CHECK (outcome IN ('Requested', 'Attempted', 'FollowUp', 'Resolved', 'Reopen')),
                    note TEXT NULL,
                    created_utc TEXT NOT NULL,
                    linked_work_entry_id INTEGER NOT NULL UNIQUE,
                    driver_code_snapshot TEXT NULL COLLATE NOCASE,
                    unit_code_snapshot TEXT NOT NULL,
                    driver_leader_snapshot TEXT NOT NULL,
                    source_import_id INTEGER NOT NULL,
                    FOREIGN KEY (missing_bol_item_id) REFERENCES missing_bol_items(id),
                    FOREIGN KEY (linked_work_entry_id) REFERENCES work_entries(id),
                    FOREIGN KEY (source_import_id) REFERENCES missing_bol_imports(id)
                );

                CREATE INDEX IF NOT EXISTS ix_missing_bol_items_source_driver
                    ON missing_bol_items(normalized_source_driver_code, current_status);
                CREATE INDEX IF NOT EXISTS ix_missing_bol_items_matched_open
                    ON missing_bol_items(matched_driver_code, current_status, empty_call_date);
                CREATE INDEX IF NOT EXISTS ix_missing_bol_items_status
                    ON missing_bol_items(current_status, empty_call_date);
                CREATE INDEX IF NOT EXISTS ix_missing_bol_items_empty_call
                    ON missing_bol_items(empty_call_date, normalized_order_number);
                CREATE INDEX IF NOT EXISTS ix_missing_bol_items_latest_presence
                    ON missing_bol_items(is_present_in_latest_import, current_status);
                CREATE INDEX IF NOT EXISTS ix_missing_bol_actions_item_history
                    ON missing_bol_action_events(missing_bol_item_id, created_utc, id);
                CREATE UNIQUE INDEX IF NOT EXISTS ux_missing_bol_one_task
                    ON missing_bol_work_links(missing_bol_item_id)
                    WHERE source_kind = 'MissingBolTask';

                CREATE TRIGGER IF NOT EXISTS guard_missing_bol_task_resolution
                BEFORE UPDATE OF resolved_utc ON work_entries
                WHEN EXISTS (
                    SELECT 1
                    FROM missing_bol_work_links AS link
                    WHERE link.work_entry_id = OLD.id
                      AND link.source_kind = 'MissingBolTask'
                )
                AND (
                    (
                        NEW.resolved_utc IS NOT NULL
                        AND COALESCE((
                            SELECT item.current_status
                            FROM missing_bol_work_links AS link
                            INNER JOIN missing_bol_items AS item ON item.id = link.missing_bol_item_id
                            WHERE link.work_entry_id = OLD.id
                        ), '') <> 'Resolved'
                    )
                    OR
                    (
                        NEW.resolved_utc IS NULL
                        AND COALESCE((
                            SELECT item.current_status
                            FROM missing_bol_work_links AS link
                            INNER JOIN missing_bol_items AS item ON item.id = link.missing_bol_item_id
                            WHERE link.work_entry_id = OLD.id
                        ), '') = 'Resolved'
                    )
                )
                BEGIN
                    SELECT RAISE(ABORT, 'Use Missing BOL controls to resolve or reopen this linked task.');
                END;
                """;
            schema.ExecuteNonQuery();
        }

        using (var version = connection.CreateCommand())
        {
            version.Transaction = transaction;
            version.CommandText = "PRAGMA user_version = 3;";
            version.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private static Dictionary<string, ExistingItem> LoadExistingItems(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        var items = new Dictionary<string, ExistingItem>(StringComparer.OrdinalIgnoreCase);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT
                id,
                normalized_order_number,
                normalized_source_driver_code,
                matched_driver_code,
                current_status,
                task_work_entry_id,
                is_present_in_latest_import,
                returned_after_resolution
            FROM missing_bol_items;
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var existing = new ExistingItem(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                ParseMissingBolStatus(reader.GetString(4)),
                reader.IsDBNull(5) ? null : reader.GetInt64(5),
                reader.GetInt32(6) == 1,
                reader.GetInt32(7) == 1);
            items.Add(existing.NormalizedOrderNumber, existing);
        }

        return items;
    }

    private static void ValidateSourceAssociations(
        IReadOnlyList<MissingBolSourceItem> sourceItems,
        IReadOnlyDictionary<string, ExistingItem> existingItems)
    {
        foreach (var source in sourceItems)
        {
            if (!existingItems.TryGetValue(source.NormalizedOrderNumber, out var existing) ||
                existing.NormalizedSourceDriverCode.Length == 0)
            {
                continue;
            }

            if (!existing.NormalizedSourceDriverCode.Equals(
                    source.NormalizedSourceDriverCode,
                    StringComparison.OrdinalIgnoreCase))
            {
                var prior = existing.NormalizedSourceDriverCode;
                var current = source.NormalizedSourceDriverCode.Length == 0
                    ? "(blank)"
                    : source.NormalizedSourceDriverCode;
                throw new ReportValidationException(
                    $"Order # '{source.SourceOrderNumber}' changed Last Dispatch Driver cd from '{prior}' to '{current}'. " +
                    "The Missing BOL import was rejected so existing driver-owned work history was not moved.");
            }
        }
    }

    private static long InsertItem(
        SqliteConnection connection,
        SqliteTransaction transaction,
        MissingBolSourceItem source,
        string? matchedDriverCode,
        long importId,
        DateTimeOffset importedUtc)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO missing_bol_items(
                normalized_order_number,
                source_order_number,
                tmex_order_number,
                logistics_order_number,
                bill_to,
                division_code,
                empty_call_date,
                origin_city_state,
                destination_city_state,
                revenue_type,
                terminal,
                source_driver_leader,
                source_driver_status,
                source_driver_code,
                normalized_source_driver_code,
                source_driver_name,
                loaded_miles,
                order_level_miles,
                matched_driver_code,
                current_status,
                first_seen_import_id,
                last_seen_import_id,
                first_seen_utc,
                last_seen_utc,
                is_present_in_latest_import,
                resolved_utc,
                task_work_entry_id,
                returned_after_resolution)
            VALUES (
                $normalizedOrder,
                $sourceOrder,
                $tmex,
                $logistics,
                $billTo,
                $division,
                $emptyCallDate,
                $origin,
                $destination,
                $revenueType,
                $terminal,
                $driverLeader,
                $driverStatus,
                $sourceDriverCode,
                $normalizedDriverCode,
                $sourceDriverName,
                $loadedMiles,
                $orderMiles,
                $matchedDriverCode,
                'Open',
                $importId,
                $importId,
                $importedUtc,
                $importedUtc,
                1,
                NULL,
                NULL,
                0);
            SELECT last_insert_rowid();
            """;
        AddSourceParameters(command, source);
        command.Parameters.AddWithValue(
            "$matchedDriverCode",
            matchedDriverCode is null ? DBNull.Value : matchedDriverCode);
        command.Parameters.AddWithValue("$importId", importId);
        command.Parameters.AddWithValue("$importedUtc", FormatUtc(importedUtc));
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static void UpdateItem(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ExistingItem existing,
        MissingBolSourceItem source,
        string? matchedDriverCode,
        long importId,
        DateTimeOffset importedUtc)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE missing_bol_items
            SET source_order_number = $sourceOrder,
                tmex_order_number = $tmex,
                logistics_order_number = $logistics,
                bill_to = $billTo,
                division_code = $division,
                empty_call_date = $emptyCallDate,
                origin_city_state = $origin,
                destination_city_state = $destination,
                revenue_type = $revenueType,
                terminal = $terminal,
                source_driver_leader = $driverLeader,
                source_driver_status = $driverStatus,
                source_driver_code = $sourceDriverCode,
                normalized_source_driver_code = $normalizedDriverCode,
                source_driver_name = $sourceDriverName,
                loaded_miles = $loadedMiles,
                order_level_miles = $orderMiles,
                matched_driver_code = $matchedDriverCode,
                last_seen_import_id = $importId,
                last_seen_utc = $importedUtc,
                is_present_in_latest_import = 1,
                returned_after_resolution = CASE
                    WHEN current_status = 'Resolved' THEN 1
                    ELSE returned_after_resolution
                END
            WHERE id = $id;
            """;
        AddSourceParameters(command, source);
        command.Parameters.AddWithValue(
            "$matchedDriverCode",
            matchedDriverCode is null ? DBNull.Value : matchedDriverCode);
        command.Parameters.AddWithValue("$importId", importId);
        command.Parameters.AddWithValue("$importedUtc", FormatUtc(importedUtc));
        command.Parameters.AddWithValue("$id", existing.Id);
        command.ExecuteNonQuery();
    }

    private static void AddSourceParameters(SqliteCommand command, MissingBolSourceItem source)
    {
        command.Parameters.AddWithValue("$normalizedOrder", source.NormalizedOrderNumber);
        command.Parameters.AddWithValue("$sourceOrder", source.SourceOrderNumber);
        command.Parameters.AddWithValue("$tmex", source.TmexOrderNumber);
        command.Parameters.AddWithValue("$logistics", source.LogisticsOrderNumber);
        command.Parameters.AddWithValue("$billTo", source.BillTo);
        command.Parameters.AddWithValue("$division", source.DivisionCode);
        command.Parameters.AddWithValue("$emptyCallDate", FormatDate(source.EmptyCallDate));
        command.Parameters.AddWithValue("$origin", source.OriginCityState);
        command.Parameters.AddWithValue("$destination", source.DestinationCityState);
        command.Parameters.AddWithValue("$revenueType", source.RevenueType);
        command.Parameters.AddWithValue("$terminal", source.Terminal);
        command.Parameters.AddWithValue("$driverLeader", source.SourceDriverLeader);
        command.Parameters.AddWithValue("$driverStatus", source.SourceDriverStatus);
        command.Parameters.AddWithValue("$sourceDriverCode", source.SourceDriverCode);
        command.Parameters.AddWithValue("$normalizedDriverCode", source.NormalizedSourceDriverCode);
        command.Parameters.AddWithValue("$sourceDriverName", source.SourceDriverName);
        command.Parameters.AddWithValue(
            "$loadedMiles",
            source.LoadedMiles is null ? DBNull.Value : (double)source.LoadedMiles.Value);
        command.Parameters.AddWithValue(
            "$orderMiles",
            source.OrderLevelMiles is null ? DBNull.Value : (double)source.OrderLevelMiles.Value);
    }

    private static DriverContext? GetDriverContext(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string normalizedDriverCode,
        IDictionary<string, DriverContext?> cache)
    {
        if (normalizedDriverCode.Length == 0)
        {
            return null;
        }

        if (cache.TryGetValue(normalizedDriverCode, out var cached))
        {
            return cached;
        }

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT
                driver.driver_code,
                driver.driver_name,
                driver.current_unit_code,
                driver.current_driver_leader,
                COALESCE(snapshot.report_cycle_date, driver.last_seen_cycle)
            FROM drivers AS driver
            LEFT JOIN current_driver_snapshots AS snapshot
                ON snapshot.driver_code = driver.driver_code
            WHERE driver.driver_code = $driverCode COLLATE NOCASE
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$driverCode", normalizedDriverCode);
        using var reader = command.ExecuteReader();
        DriverContext? context = reader.Read()
            ? new DriverContext(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                ParseDate(reader.GetString(4)))
            : null;
        cache[normalizedDriverCode] = context;
        return context;
    }

    private static long CreateTask(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long itemId,
        MissingBolSourceItem source,
        MissingBolStatus status,
        DriverContext driver,
        long importId,
        DateTimeOffset createdUtc)
    {
        long workEntryId;
        using (var command = connection.CreateCommand())
        {
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
                    'FollowUp',
                    $createdUtc,
                    NULL,
                    'Manual',
                    NULL,
                    $reportCycle,
                    $unitCode,
                    $driverLeader);
                SELECT last_insert_rowid();
                """;
            command.Parameters.AddWithValue("$driverCode", driver.DriverCode);
            command.Parameters.AddWithValue("$text", BuildTaskText(source, status));
            command.Parameters.AddWithValue("$createdUtc", FormatUtc(createdUtc));
            command.Parameters.AddWithValue("$reportCycle", FormatDate(driver.ReportCycleDate));
            command.Parameters.AddWithValue("$unitCode", driver.UnitCode);
            command.Parameters.AddWithValue("$driverLeader", driver.DriverLeader);
            workEntryId = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        using (var link = connection.CreateCommand())
        {
            link.Transaction = transaction;
            link.CommandText = """
                INSERT INTO missing_bol_work_links(
                    work_entry_id,
                    missing_bol_item_id,
                    source_kind,
                    source_import_id)
                VALUES ($workEntryId, $itemId, 'MissingBolTask', $importId);
                """;
            link.Parameters.AddWithValue("$workEntryId", workEntryId);
            link.Parameters.AddWithValue("$itemId", itemId);
            link.Parameters.AddWithValue("$importId", importId);
            link.ExecuteNonQuery();
        }

        using (var updateItem = connection.CreateCommand())
        {
            updateItem.Transaction = transaction;
            updateItem.CommandText = """
                UPDATE missing_bol_items
                SET task_work_entry_id = $workEntryId
                WHERE id = $itemId
                  AND task_work_entry_id IS NULL;
                """;
            updateItem.Parameters.AddWithValue("$workEntryId", workEntryId);
            updateItem.Parameters.AddWithValue("$itemId", itemId);
            if (updateItem.ExecuteNonQuery() != 1)
            {
                throw new InvalidOperationException("The Missing BOL item already has a linked task.");
            }
        }

        return workEntryId;
    }

    private static void UpdateTaskText(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long workEntryId,
        string text)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE work_entries SET text = $text WHERE id = $id;";
        command.Parameters.AddWithValue("$text", text);
        command.Parameters.AddWithValue("$id", workEntryId);
        command.ExecuteNonQuery();
    }

    private static ActionItem? LoadActionItem(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long itemId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT
                item.id,
                item.normalized_order_number,
                item.source_order_number,
                item.tmex_order_number,
                item.logistics_order_number,
                item.bill_to,
                item.division_code,
                item.empty_call_date,
                item.origin_city_state,
                item.destination_city_state,
                item.revenue_type,
                item.terminal,
                item.source_driver_leader,
                item.source_driver_status,
                item.source_driver_code,
                item.normalized_source_driver_code,
                item.source_driver_name,
                item.loaded_miles,
                item.order_level_miles,
                item.matched_driver_code,
                item.current_status,
                item.task_work_entry_id,
                item.last_seen_import_id,
                driver.current_unit_code,
                driver.current_driver_leader,
                COALESCE(snapshot.report_cycle_date, driver.last_seen_cycle)
            FROM missing_bol_items AS item
            LEFT JOIN drivers AS driver ON driver.driver_code = item.matched_driver_code
            LEFT JOIN current_driver_snapshots AS snapshot ON snapshot.driver_code = driver.driver_code
            WHERE item.id = $id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$id", itemId);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new ActionItem(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            ParseDate(reader.GetString(7)),
            reader.GetString(8),
            reader.GetString(9),
            reader.GetString(10),
            reader.GetString(11),
            reader.GetString(12),
            reader.GetString(13),
            reader.GetString(14),
            reader.GetString(15),
            reader.GetString(16),
            ReadNullableDecimal(reader, 17),
            ReadNullableDecimal(reader, 18),
            reader.IsDBNull(19) ? null : reader.GetString(19),
            ParseMissingBolStatus(reader.GetString(20)),
            reader.IsDBNull(21) ? null : reader.GetInt64(21),
            reader.GetInt64(22),
            reader.IsDBNull(23) ? string.Empty : reader.GetString(23),
            reader.IsDBNull(24) ? string.Empty : reader.GetString(24),
            reader.IsDBNull(25) ? null : ParseDate(reader.GetString(25)));
    }

    private static long InsertCompletedActivity(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ActionItem item,
        string text,
        DateTimeOffset createdUtc)
    {
        long workEntryId;
        using (var command = connection.CreateCommand())
        {
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
                    'Done',
                    $createdUtc,
                    $createdUtc,
                    'Manual',
                    NULL,
                    $reportCycle,
                    $unitCode,
                    $driverLeader);
                SELECT last_insert_rowid();
                """;
            command.Parameters.AddWithValue("$driverCode", item.MatchedDriverCode!);
            command.Parameters.AddWithValue("$text", text);
            command.Parameters.AddWithValue("$createdUtc", FormatUtc(createdUtc));
            command.Parameters.AddWithValue(
                "$reportCycle",
                item.ReportCycleDate is null ? DBNull.Value : FormatDate(item.ReportCycleDate.Value));
            command.Parameters.AddWithValue("$unitCode", item.UnitCode);
            command.Parameters.AddWithValue("$driverLeader", item.DriverLeader);
            workEntryId = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        using (var link = connection.CreateCommand())
        {
            link.Transaction = transaction;
            link.CommandText = """
                INSERT INTO missing_bol_work_links(
                    work_entry_id,
                    missing_bol_item_id,
                    source_kind,
                    source_import_id)
                VALUES ($workEntryId, $itemId, 'MissingBolAction', $importId);
                """;
            link.Parameters.AddWithValue("$workEntryId", workEntryId);
            link.Parameters.AddWithValue("$itemId", item.Id);
            link.Parameters.AddWithValue("$importId", item.LastSeenImportId);
            link.ExecuteNonQuery();
        }

        return workEntryId;
    }

    private static IReadOnlyList<MissingBolItemRecord> ReadItems(SqliteCommand command)
    {
        var items = new List<MissingBolItemRecord>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            items.Add(ReadItem(reader));
        }

        return items;
    }

    private static MissingBolItemRecord? ReadSingleItem(SqliteCommand command)
    {
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadItem(reader) : null;
    }

    private static MissingBolItemRecord ReadItem(SqliteDataReader reader)
    {
        var sourceDriverName = reader.GetString(16);
        var matchedDriverName = reader.IsDBNull(20) ? string.Empty : reader.GetString(20);
        return new MissingBolItemRecord(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            ParseDate(reader.GetString(7)),
            reader.GetString(8),
            reader.GetString(9),
            reader.GetString(10),
            reader.GetString(11),
            reader.GetString(12),
            reader.GetString(13),
            reader.GetString(14),
            reader.GetString(15),
            sourceDriverName,
            ReadNullableDecimal(reader, 17),
            ReadNullableDecimal(reader, 18),
            reader.IsDBNull(19) ? null : reader.GetString(19),
            matchedDriverName,
            ParseMissingBolStatus(reader.GetString(21)),
            ParseUtc(reader.GetString(22)),
            ParseUtc(reader.GetString(23)),
            reader.GetInt32(24) == 1,
            reader.IsDBNull(25) ? null : ParseUtc(reader.GetString(25)),
            reader.IsDBNull(26) ? null : reader.GetInt64(26),
            reader.GetInt64(27),
            reader.GetInt32(28) == 1,
            sourceDriverName.Length > 0 &&
            matchedDriverName.Length > 0 &&
            !sourceDriverName.Equals(matchedDriverName, StringComparison.CurrentCultureIgnoreCase));
    }

    private static SourceItemSnapshot ReadSourceItem(SqliteDataReader reader) =>
        new(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            ParseDate(reader.GetString(7)),
            reader.GetString(8),
            reader.GetString(9),
            reader.GetString(10),
            reader.GetString(11),
            reader.GetString(12),
            reader.GetString(13),
            reader.GetString(14),
            reader.GetString(15),
            reader.GetString(16),
            ReadNullableDecimal(reader, 17),
            ReadNullableDecimal(reader, 18));

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;";
        pragma.ExecuteNonQuery();
        return connection;
    }

    private static string BuildTaskText(MissingBolSourceItem item, MissingBolStatus status)
    {
        var text = $"Missing BOL for order {item.SourceOrderNumber}, empty call {item.EmptyCallDate:M/d/yyyy}";
        if (item.OriginCityState.Length > 0 && item.DestinationCityState.Length > 0)
        {
            text += $", {item.OriginCityState} → {item.DestinationCityState}";
        }
        else if (item.OriginCityState.Length > 0)
        {
            text += $", origin {item.OriginCityState}";
        }
        else if (item.DestinationCityState.Length > 0)
        {
            text += $", destination {item.DestinationCityState}";
        }

        return $"{text}. Status: {DisplayStatus(status)}.";
    }

    private static string BuildActionText(
        MissingBolActionOutcome outcome,
        string orderNumber,
        string note)
    {
        var text = outcome switch
        {
            MissingBolActionOutcome.Requested => $"Requested missing BOL for order {orderNumber}.",
            MissingBolActionOutcome.Attempted =>
                $"Attempted contact regarding missing BOL for order {orderNumber}; driver not reached.",
            MissingBolActionOutcome.FollowUp =>
                $"Missing BOL for order {orderNumber} requires follow-up.",
            MissingBolActionOutcome.Resolved => $"Resolved missing BOL for order {orderNumber}.",
            MissingBolActionOutcome.Reopen => $"Reopened missing BOL for order {orderNumber}.",
            _ => throw new ArgumentOutOfRangeException(nameof(outcome))
        };

        return note.Length == 0 ? text : $"{text} Note: {EnsureSentence(note)}";
    }

    private static string DisplayStatus(MissingBolStatus status) =>
        status == MissingBolStatus.FollowUp ? "Follow-up" : status.ToString();

    private static string EnsureSentence(string value) =>
        value.EndsWith(".", StringComparison.Ordinal) ||
        value.EndsWith("!", StringComparison.Ordinal) ||
        value.EndsWith("?", StringComparison.Ordinal)
            ? value
            : value + ".";

    private static MissingBolStatus ParseMissingBolStatus(string value) =>
        Enum.TryParse<MissingBolStatus>(value, out var status) && Enum.IsDefined(status)
            ? status
            : throw new InvalidDataException($"Database Missing BOL item has unknown status '{value}'.");

    private static MissingBolActionOutcome ParseMissingBolAction(string value) =>
        Enum.TryParse<MissingBolActionOutcome>(value, out var outcome) && Enum.IsDefined(outcome)
            ? outcome
            : throw new InvalidDataException($"Database Missing BOL action has unknown outcome '{value}'.");

    private static WorkEntrySource ParseWorkEntrySource(string value) =>
        Enum.TryParse<WorkEntrySource>(value, out var source) && Enum.IsDefined(source)
            ? source
            : throw new InvalidDataException($"Database Missing BOL work link has unknown source '{value}'.");

    private static string FormatDate(DateOnly value) =>
        value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateOnly ParseDate(string value) =>
        DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string FormatUtc(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseUtc(string value) =>
        DateTimeOffset.Parse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind).ToUniversalTime();

    private static decimal? ReadNullableDecimal(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal)
            ? null
            : Convert.ToDecimal(reader.GetDouble(ordinal), CultureInfo.InvariantCulture);

    private const string MissingBolItemSelect = """
        SELECT
            item.id,
            item.normalized_order_number,
            item.source_order_number,
            item.tmex_order_number,
            item.logistics_order_number,
            item.bill_to,
            item.division_code,
            item.empty_call_date,
            item.origin_city_state,
            item.destination_city_state,
            item.revenue_type,
            item.terminal,
            item.source_driver_leader,
            item.source_driver_status,
            item.source_driver_code,
            item.normalized_source_driver_code,
            item.source_driver_name,
            item.loaded_miles,
            item.order_level_miles,
            item.matched_driver_code,
            driver.driver_name,
            item.current_status,
            item.first_seen_utc,
            item.last_seen_utc,
            item.is_present_in_latest_import,
            item.resolved_utc,
            item.task_work_entry_id,
            item.last_seen_import_id,
            item.returned_after_resolution
        FROM missing_bol_items AS item
        LEFT JOIN drivers AS driver ON driver.driver_code = item.matched_driver_code
        """ + "\n";

    private sealed record ExistingItem(
        long Id,
        string NormalizedOrderNumber,
        string NormalizedSourceDriverCode,
        string? MatchedDriverCode,
        MissingBolStatus Status,
        long? TaskWorkEntryId,
        bool WasPresentInLatestImport,
        bool ReturnedAfterResolution);

    private sealed record DriverContext(
        string DriverCode,
        string DriverName,
        string UnitCode,
        string DriverLeader,
        DateOnly ReportCycleDate);

    private sealed record SourceItemSnapshot(
        long Id,
        string NormalizedOrderNumber,
        string SourceOrderNumber,
        string TmexOrderNumber,
        string LogisticsOrderNumber,
        string BillTo,
        string DivisionCode,
        DateOnly EmptyCallDate,
        string OriginCityState,
        string DestinationCityState,
        string RevenueType,
        string Terminal,
        string SourceDriverLeader,
        string SourceDriverStatus,
        string SourceDriverCode,
        string NormalizedSourceDriverCode,
        string SourceDriverName,
        decimal? LoadedMiles,
        decimal? OrderLevelMiles)
    {
        public MissingBolSourceItem ToCoreItem() =>
            new(
                NormalizedOrderNumber,
                SourceOrderNumber,
                TmexOrderNumber,
                LogisticsOrderNumber,
                BillTo,
                DivisionCode,
                EmptyCallDate,
                OriginCityState,
                DestinationCityState,
                RevenueType,
                Terminal,
                SourceDriverLeader,
                SourceDriverStatus,
                SourceDriverCode,
                NormalizedSourceDriverCode,
                SourceDriverName,
                LoadedMiles,
                OrderLevelMiles,
                0);
    }

    private sealed record AttachCandidate(
        SourceItemSnapshot Source,
        MissingBolStatus Status,
        long? TaskWorkEntryId,
        long SourceImportId,
        DriverContext Driver);

    private sealed record ActionItem(
        long Id,
        string NormalizedOrderNumber,
        string SourceOrderNumber,
        string TmexOrderNumber,
        string LogisticsOrderNumber,
        string BillTo,
        string DivisionCode,
        DateOnly EmptyCallDate,
        string OriginCityState,
        string DestinationCityState,
        string RevenueType,
        string Terminal,
        string SourceDriverLeader,
        string SourceDriverStatus,
        string SourceDriverCode,
        string NormalizedSourceDriverCode,
        string SourceDriverName,
        decimal? LoadedMiles,
        decimal? OrderLevelMiles,
        string? MatchedDriverCode,
        MissingBolStatus Status,
        long? TaskWorkEntryId,
        long LastSeenImportId,
        string UnitCode,
        string DriverLeader,
        DateOnly? ReportCycleDate)
    {
        public MissingBolSourceItem ToCoreItem() =>
            new(
                NormalizedOrderNumber,
                SourceOrderNumber,
                TmexOrderNumber,
                LogisticsOrderNumber,
                BillTo,
                DivisionCode,
                EmptyCallDate,
                OriginCityState,
                DestinationCityState,
                RevenueType,
                Terminal,
                SourceDriverLeader,
                SourceDriverStatus,
                SourceDriverCode,
                NormalizedSourceDriverCode,
                SourceDriverName,
                LoadedMiles,
                OrderLevelMiles,
                0);
    }
}
