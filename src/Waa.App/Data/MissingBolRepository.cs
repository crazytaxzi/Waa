using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Waa.Core;

namespace Waa.App.Data;

/// <summary>
/// Session-only Missing BOL report store.
///
/// v0.4.6 intentionally does not persist workbook rows, import hashes, item state,
/// action state, or linked Missing BOL work. The current workbook is the source of
/// truth for what is shown. The database is read only here for current roster
/// identity and for classifying legacy BOL-generated work from older WAA releases.
/// </summary>
public sealed class MissingBolRepository
{
    private readonly string _connectionString;
    private readonly object _gate = new();
    private IReadOnlyList<MissingBolSourceItem> _items = Array.Empty<MissingBolSourceItem>();
    private string? _sourceHash;
    private DateTimeOffset _loadedUtc = DateTimeOffset.MinValue;

    public MissingBolRepository(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Cache = SqliteCacheMode.Private
        }.ToString();
    }

    // Kept for host/test compatibility. Missing BOL no longer owns DB schema.
    public void Initialize()
    {
    }

    public bool HasCurrentSnapshot
    {
        get
        {
            lock (_gate)
            {
                return _sourceHash is not null;
            }
        }
    }

    public bool IsHashAccepted(string sourceHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceHash);
        lock (_gate)
        {
            return string.Equals(_sourceHash, sourceHash, StringComparison.OrdinalIgnoreCase);
        }
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

        lock (_gate)
        {
            if (string.Equals(_sourceHash, sourceHash, StringComparison.OrdinalIgnoreCase))
            {
                return new MissingBolImportResult(false, true, null, _items.Count, 0);
            }

            _items = import.Items.ToArray();
            _sourceHash = sourceHash;
            _loadedUtc = (importedUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();
            return new MissingBolImportResult(true, false, null, _items.Count, 0);
        }
    }

    public bool ClearCurrent()
    {
        lock (_gate)
        {
            var changed = _sourceHash is not null || _items.Count > 0;
            _items = Array.Empty<MissingBolSourceItem>();
            _sourceHash = null;
            _loadedUtc = DateTimeOffset.MinValue;
            return changed;
        }
    }

    // Compatibility method from the old persisted workflow. There is nothing to attach.
    public int AttachExactMatchesAndCreateTasks(DateTimeOffset? createdUtc = null) => 0;

    public long RecordAction(
        long missingBolItemId,
        MissingBolActionOutcome outcome,
        string? note,
        DateTimeOffset? createdUtc = null) =>
        throw new InvalidOperationException(
            "Missing BOL is a read-only report view. Outcomes are not stored by WAA.");

    public MissingBolFleetState LoadFleetState()
    {
        var currentDrivers = LoadCurrentDrivers();
        var items = SnapshotItems();
        var summaries = new Dictionary<string, MissingBolDriverSummary>(StringComparer.OrdinalIgnoreCase);
        var unmatched = new List<MissingBolUnmatchedRecord>();

        foreach (var source in items)
        {
            if (source.NormalizedSourceDriverCode.Length > 0 &&
                currentDrivers.TryGetValue(source.NormalizedSourceDriverCode, out var driver))
            {
                if (!summaries.TryGetValue(driver.DriverCode, out var summary))
                {
                    summaries[driver.DriverCode] = new MissingBolDriverSummary(
                        1,
                        source.EmptyCallDate,
                        source.SourceOrderNumber);
                }
                else
                {
                    summaries[driver.DriverCode] = summary with
                    {
                        OpenCount = summary.OpenCount + 1,
                        OldestOpenEmptyCallDate = summary.OldestOpenEmptyCallDate is null ||
                                                  source.EmptyCallDate < summary.OldestOpenEmptyCallDate.Value
                            ? source.EmptyCallDate
                            : summary.OldestOpenEmptyCallDate,
                        OrderSearchText = string.Join(
                            ' ',
                            new[] { summary.OrderSearchText, source.SourceOrderNumber }
                                .Where(value => !string.IsNullOrWhiteSpace(value)))
                    };
                }
            }
            else
            {
                unmatched.Add(ToUnmatchedRecord(source));
            }
        }

        return new MissingBolFleetState(
            summaries,
            summaries.Values.Sum(summary => summary.OpenCount),
            unmatched
                .OrderBy(item => item.EmptyCallDate)
                .ThenBy(item => item.SourceOrderNumber, StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    public IReadOnlyList<MissingBolItemRecord> LoadDriverItems(string driverCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(driverCode);
        var normalized = MissingBolText.NormalizeExact(driverCode);
        var currentDrivers = LoadCurrentDrivers();
        if (!currentDrivers.TryGetValue(normalized, out var driver))
        {
            return Array.Empty<MissingBolItemRecord>();
        }

        return SnapshotItems()
            .Where(item => item.NormalizedSourceDriverCode.Equals(normalized, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.EmptyCallDate)
            .ThenBy(item => item.SourceOrderNumber, StringComparer.OrdinalIgnoreCase)
            .Select(item => ToItemRecord(item, driver))
            .ToArray();
    }

    public MissingBolItemRecord? GetItemByOrder(string orderNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderNumber);
        var normalizedOrder = MissingBolText.NormalizeExact(orderNumber);
        var source = SnapshotItems().FirstOrDefault(item =>
            item.NormalizedOrderNumber.Equals(normalizedOrder, StringComparison.OrdinalIgnoreCase));
        if (source is null)
        {
            return null;
        }

        var currentDrivers = LoadCurrentDrivers();
        currentDrivers.TryGetValue(source.NormalizedSourceDriverCode, out var driver);
        return ToItemRecord(source, driver);
    }

    public long? GetTaskWorkEntryId(long missingBolItemId) => null;

    public IReadOnlyList<MissingBolActionRecord> LoadActionHistory(long missingBolItemId) =>
        Array.Empty<MissingBolActionRecord>();

    /// <summary>
    /// Classifies legacy v0.3-v0.4.5 generated BOL work so callers can exclude it
    /// from current manual work/Handoff. No Missing BOL rows are written or restored.
    /// </summary>
    public IReadOnlyList<WorkEntryRecord> ApplyWorkSources(IEnumerable<WorkEntryRecord> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var materialized = entries.ToArray();
        if (materialized.Length == 0)
        {
            return materialized;
        }

        using var connection = OpenConnection();
        if (!TableExists(connection, "missing_bol_work_links"))
        {
            return materialized;
        }

        var ids = materialized.Select(entry => entry.Id).Distinct().ToArray();
        var sources = new Dictionary<long, WorkEntrySource>();
        const int batchSize = 400;
        for (var offset = 0; offset < ids.Length; offset += batchSize)
        {
            var batch = ids.Skip(offset).Take(batchSize).ToArray();
            using var command = connection.CreateCommand();
            var parameterNames = new string[batch.Length];
            for (var index = 0; index < batch.Length; index++)
            {
                parameterNames[index] = $"$id{index}";
                command.Parameters.AddWithValue(parameterNames[index], batch[index]);
            }

            command.CommandText = $"""
                SELECT work_entry_id, source_kind
                FROM missing_bol_work_links
                WHERE work_entry_id IN ({string.Join(",", parameterNames)});
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var source = reader.GetString(1) switch
                {
                    "MissingBolTask" => WorkEntrySource.MissingBolTask,
                    "MissingBolAction" => WorkEntrySource.MissingBolAction,
                    _ => (WorkEntrySource?)null
                };
                if (source is not null)
                {
                    sources[reader.GetInt64(0)] = source.Value;
                }
            }
        }

        return materialized
            .Select(entry => sources.TryGetValue(entry.Id, out var source)
                ? entry with { Source = source }
                : entry)
            .ToArray();
    }

    public IReadOnlyDictionary<string, int> LoadLegacyOpenTaskCountByDriver()
    {
        using var connection = OpenConnection();
        if (!TableExists(connection, "missing_bol_work_links"))
        {
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT work.driver_code, COUNT(*)
            FROM missing_bol_work_links AS link
            INNER JOIN work_entries AS work ON work.id = link.work_entry_id
            WHERE link.source_kind = 'MissingBolTask'
              AND work.resolved_utc IS NULL
            GROUP BY work.driver_code;
            """;
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result[reader.GetString(0)] = reader.GetInt32(1);
        }

        return result;
    }

    /// <summary>
    /// Creates transient Handoff rows from the current workbook only. These objects
    /// never enter SQLite; they merely reuse the established deterministic formatter.
    /// </summary>
    public IReadOnlyList<WorkEntryRecord> BuildCurrentHandoffEntries(
        IEnumerable<FleetDriverRecord> currentDrivers)
    {
        ArgumentNullException.ThrowIfNull(currentDrivers);
        var drivers = currentDrivers
            .GroupBy(driver => driver.DriverCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var entries = new List<WorkEntryRecord>();
        foreach (var source in SnapshotItems())
        {
            if (source.NormalizedSourceDriverCode.Length == 0 ||
                !drivers.TryGetValue(source.NormalizedSourceDriverCode, out var driver))
            {
                continue;
            }

            entries.Add(new WorkEntryRecord(
                SyntheticWorkId(source.NormalizedOrderNumber),
                driver.DriverCode,
                driver.DriverName,
                $"Missing BOL for order {source.SourceOrderNumber}, empty call {source.EmptyCallDate:M/d/yyyy}.",
                WorkEntryStatus.FollowUp,
                _loadedUtc == DateTimeOffset.MinValue ? DateTimeOffset.UtcNow : _loadedUtc,
                null,
                WorkEntrySource.MissingBolTask,
                null,
                driver.ReportCycleDate,
                driver.UnitCode,
                driver.DriverLeader));
        }

        return entries;
    }

    private IReadOnlyList<MissingBolSourceItem> SnapshotItems()
    {
        lock (_gate)
        {
            return _items.ToArray();
        }
    }

    private MissingBolItemRecord ToItemRecord(MissingBolSourceItem source, CurrentDriver? driver)
    {
        var loaded = _loadedUtc == DateTimeOffset.MinValue ? DateTimeOffset.UtcNow : _loadedUtc;
        var sourceNameDiffers = driver is not null &&
                                source.SourceDriverName.Length > 0 &&
                                !source.SourceDriverName.Equals(driver.DriverName, StringComparison.OrdinalIgnoreCase);
        return new MissingBolItemRecord(
            StableItemId(source.NormalizedOrderNumber),
            source.NormalizedOrderNumber,
            source.SourceOrderNumber,
            source.TmexOrderNumber,
            source.LogisticsOrderNumber,
            source.BillTo,
            source.DivisionCode,
            source.EmptyCallDate,
            source.OriginCityState,
            source.DestinationCityState,
            source.RevenueType,
            source.Terminal,
            source.SourceDriverLeader,
            source.SourceDriverStatus,
            source.SourceDriverCode,
            source.NormalizedSourceDriverCode,
            source.SourceDriverName,
            source.LoadedMiles,
            source.OrderLevelMiles,
            driver?.DriverCode,
            driver?.DriverName ?? string.Empty,
            MissingBolStatus.Open,
            loaded,
            loaded,
            true,
            null,
            null,
            0,
            false,
            sourceNameDiffers);
    }

    private static MissingBolUnmatchedRecord ToUnmatchedRecord(MissingBolSourceItem source) =>
        new(
            StableItemId(source.NormalizedOrderNumber),
            source.SourceOrderNumber,
            source.EmptyCallDate,
            source.SourceDriverCode,
            source.SourceDriverName,
            source.OriginCityState,
            source.DestinationCityState,
            true);

    private Dictionary<string, CurrentDriver> LoadCurrentDrivers()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT driver.driver_code, driver.driver_name
            FROM current_driver_snapshots AS snapshot
            INNER JOIN drivers AS driver ON driver.driver_code = snapshot.driver_code;
            """;
        var result = new Dictionary<string, CurrentDriver>(StringComparer.OrdinalIgnoreCase);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var driver = new CurrentDriver(reader.GetString(0), reader.GetString(1));
            result[driver.DriverCode] = driver;
        }

        return result;
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private static bool TableExists(SqliteConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name);";
        command.Parameters.AddWithValue("$name", tableName);
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) == 1;
    }

    private static long StableItemId(string normalizedOrderNumber)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedOrderNumber));
        var value = BitConverter.ToInt64(bytes, 0) & long.MaxValue;
        return value == 0 ? 1 : value;
    }

    private static long SyntheticWorkId(string normalizedOrderNumber)
    {
        var stable = StableItemId(normalizedOrderNumber);
        return stable == long.MaxValue ? long.MaxValue - 1 : stable + 1;
    }

    private sealed record CurrentDriver(string DriverCode, string DriverName);
}
