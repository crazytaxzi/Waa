using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using Waa.App.Data;
using Waa.Core;

namespace Waa.App.Tests;

internal sealed record SyntheticDriver(
    string DriverCode,
    string DriverName,
    string DriverLeader,
    string UnitCode,
    decimal IdlePercent);

internal sealed class RepositoryFixture : IDisposable
{
    private bool _disposed;

    public RepositoryFixture(bool importDefaultFleet = true)
    {
        Root = Path.Combine(Path.GetTempPath(), "WaaAppTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        DatabasePath = Path.Combine(Root, "waa.db");
        Repository = new WaaRepository(DatabasePath);
        Repository.Initialize();

        if (importDefaultFleet)
        {
            ImportFleet(
                new DateOnly(2026, 8, 30),
                new SyntheticDriver("A00001", "Alex Example", "LEAD000001", "270101", 62m),
                new SyntheticDriver("B00002", "Blair Example", "LEAD000002", "270202", 56m),
                new SyntheticDriver("C00003", "Casey Example", "LEAD000003", "270303", 32m),
                new SyntheticDriver("D00004", "Drew Example", "LEAD000004", "270404", 18m));
        }
    }

    public string Root { get; }
    public string DatabasePath { get; }
    public WaaRepository Repository { get; }

    public void ImportFleet(DateOnly cycle, params SyntheticDriver[] drivers)
    {
        var csvBytes = Encoding.UTF8.GetBytes(BuildCsv(cycle, drivers));
        var import = new RollingSevenDayCsvParser().Parse(csvBytes);
        var hash = $"SYNTHETIC-{Guid.NewGuid():N}";
        var result = Repository.ImportReport(
            import,
            "rolling 7 day_data-synthetic.csv",
            Path.Combine(Root, "rolling 7 day_data-synthetic.csv"),
            hash,
            DateTime.UtcNow);
        if (!result.Imported)
        {
            throw new InvalidOperationException("Synthetic report import did not complete.");
        }
    }

    public FleetDriverRecord Driver(string driverCode) =>
        Repository.LoadFleet().Drivers.Single(driver =>
            driver.DriverCode.Equals(driverCode, StringComparison.OrdinalIgnoreCase));

    public long ScalarLong(string commandText)
    {
        using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    public void ExecuteSql(string commandText)
    {
        using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.ExecuteNonQuery();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        for (var attempt = 0; attempt < 80 && Directory.Exists(Root); attempt++)
        {
            SqliteConnection.ClearAllPools();
            try
            {
                Directory.Delete(Root, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 79)
            {
                Thread.Sleep(25);
            }
            catch (UnauthorizedAccessException) when (attempt < 79)
            {
                Thread.Sleep(25);
            }
        }

        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }

    private static string BuildCsv(DateOnly cycle, IReadOnlyCollection<SyntheticDriver> drivers)
    {
        var rows = new List<string> { Header };
        foreach (var driver in drivers)
        {
            for (var weekOffset = 0; weekOffset < 4; weekOffset++)
            {
                var week = cycle.AddDays(-7 * weekOffset);
                rows.Add(Row(driver, "OOR %", week));
                rows.Add(Row(driver, "Idle %", week));
            }
        }

        return string.Join("\r\n", rows);
    }

    private static string Row(SyntheticDriver driver, string measure, DateOnly week)
    {
        var date = week.ToString("M/d/yyyy", CultureInfo.InvariantCulture);
        var idle = driver.IdlePercent.ToString(CultureInfo.InvariantCulture);
        return $"{driver.DriverCode} {driver.DriverName},{measure},{date},100,{idle},1000,1010," +
               $"611 - Synthetic - Van,{driver.DriverLeader},Synthetic,TEST,Line Haul,{date}," +
               $"{driver.UnitCode},{date},0.5";
    }

    private const string Header =
        "Group by (copy),Measure Names,Week Start Date,[Rolling 7 Day Engine Time]/60," +
        "[Rolling 7 Day Idle Time]/60,Rolling 7 Day Dispatch Miles,Rolling 7 Day Qualcomm Miles," +
        "Cost Center,Driver Leader,Driver Terminal,Fleet Leader,OPS LOB,Rolling 7 Day Start Date," +
        "Unit Code,Week Start Date,Measure Values";
}

internal static class WorkEntryTestData
{
    public static WorkEntryRecord Entry(
        long id,
        string driverCode,
        string driverName,
        WorkEntryStatus status,
        DateTimeOffset createdUtc,
        DateTimeOffset? resolvedUtc = null,
        WorkEntrySource source = WorkEntrySource.Manual,
        long? linkedIdleContactEventId = null,
        string unitCode = "270101",
        string text = "Synthetic work item.") =>
        new(
            id,
            driverCode,
            driverName,
            text,
            status,
            createdUtc,
            resolvedUtc,
            source,
            linkedIdleContactEventId,
            new DateOnly(2026, 8, 30),
            unitCode,
            "LEAD000001");

    public static FleetDriverRecord FleetDriver(
        string code,
        string name,
        decimal idlePercent,
        IdleContactOutcome? outcome,
        int openWorkCount,
        bool complete28Day = true) =>
        new(
            code,
            name,
            $"{code} {name}",
            new DateOnly(2026, 8, 30),
            $"27{code[^3..]}",
            "LEAD000001",
            100m,
            idlePercent,
            idlePercent,
            400m,
            idlePercent * 4m,
            complete28Day ? idlePercent : null,
            complete28Day ? 4 : 3,
            complete28Day,
            1,
            outcome,
            string.Empty,
            null,
            openWorkCount);
}

internal sealed class RecordingClipboard : Waa.App.Services.IClipboardService
{
    public string? Text { get; private set; }

    public void SetText(string text) => Text = text;
}
