using System.Text;
using Microsoft.Data.Sqlite;
using Waa.App.Data;
using Waa.Core;
using Xunit;

namespace Waa.App.Tests;

public sealed class WaaRepositoryTests
{
    [Fact]
    public void Repository_RoundTripsImportThresholdAndCurrentCycleContact()
    {
        var root = Path.Combine(Path.GetTempPath(), "WaaAppTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var repository = new WaaRepository(Path.Combine(root, "waa.db"));
            repository.Initialize();
            Assert.Equal(50m, repository.GetIdleThreshold());

            var csvBytes = Encoding.UTF8.GetBytes(BuildCsv());
            var import = new RollingSevenDayCsvParser().Parse(csvBytes);
            var imported = repository.ImportReport(
                import,
                "rolling 7 day_data.csv",
                Path.Combine(root, "rolling 7 day_data.csv"),
                "SYNTHETIC-HASH-ONE",
                DateTime.UtcNow);

            Assert.True(imported.Imported);
            Assert.False(imported.AlreadyAccepted);

            var duplicate = repository.ImportReport(
                import,
                "rolling 7 day_data.csv",
                Path.Combine(root, "rolling 7 day_data.csv"),
                "SYNTHETIC-HASH-ONE",
                DateTime.UtcNow);
            Assert.False(duplicate.Imported);
            Assert.True(duplicate.AlreadyAccepted);

            var state = repository.LoadFleet();
            Assert.Equal(new DateOnly(2026, 8, 23), state.ReportCycleDate);
            Assert.Equal(2, state.Drivers.Count);
            Assert.Equal(2, state.IncludedDrivers7Day);
            Assert.Equal(2, state.IncludedDrivers28Day);

            var highIdleDriver = Assert.Single(state.Drivers, driver => driver.DriverCode == "HIGH01");
            Assert.Equal("LEADER0001", highIdleDriver.DriverLeader);
            Assert.Equal(60m, highIdleDriver.IdlePercent7Day);
            Assert.Equal(52.5m, highIdleDriver.IdlePercent28Day);
            Assert.Null(highIdleDriver.LatestOutcome);

            repository.RecordIdleContact(
                highIdleDriver,
                IdleContactOutcome.Spoke,
                "Reviewed idle and expectations.",
                50m);

            var contactedState = repository.LoadFleet();
            var contactedDriver = Assert.Single(
                contactedState.Drivers,
                driver => driver.DriverCode == "HIGH01");
            Assert.Equal("LEADER0001", contactedDriver.DriverLeader);
            Assert.Equal(IdleContactOutcome.Spoke, contactedDriver.LatestOutcome);
            Assert.Equal("Reviewed idle and expectations.", contactedDriver.LatestNote);
            Assert.NotNull(contactedDriver.LatestContactUtc);

            repository.SetIdleThreshold(47.5m);
            Assert.Equal(47.5m, repository.GetIdleThreshold());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    private static string BuildCsv()
    {
        var rows = new List<string> { Header };
        AddDriver(rows, "HIGH01 High Idle Driver", "LEADER0001", "270139", new[]
        {
            ("8/23/2026", "50", "30"),
            ("8/16/2026", "50", "25"),
            ("8/9/2026", "50", "25"),
            ("8/2/2026", "50", "25")
        });
        AddDriver(rows, "LOW001 Low Idle Driver", "LEADER0002", "231540", new[]
        {
            ("8/23/2026", "50", "10"),
            ("8/16/2026", "50", "10"),
            ("8/9/2026", "50", "10"),
            ("8/2/2026", "50", "10")
        });
        return string.Join("\r\n", rows);
    }

    private static void AddDriver(
        ICollection<string> rows,
        string driver,
        string leader,
        string unit,
        IEnumerable<(string Week, string Engine, string Idle)> weeks)
    {
        foreach (var week in weeks)
        {
            rows.Add(Row(driver, leader, unit, "OOR %", week.Week, week.Engine, week.Idle));
            rows.Add(Row(driver, leader, unit, "Idle %", week.Week, week.Engine, week.Idle));
        }
    }

    private const string Header =
        "Group by (copy),Measure Names,Week Start Date,[Rolling 7 Day Engine Time]/60," +
        "[Rolling 7 Day Idle Time]/60,Rolling 7 Day Dispatch Miles,Rolling 7 Day Qualcomm Miles," +
        "Cost Center,Driver Leader,Driver Terminal,Fleet Leader,OPS LOB,Rolling 7 Day Start Date," +
        "Unit Code,Week Start Date,Measure Values";

    private static string Row(
        string driver,
        string leader,
        string unit,
        string measure,
        string week,
        string engine,
        string idle) =>
        $"{driver},{measure},{week},{engine},{idle},1000,1010,611 - Lewiston - Van,{leader},Lewiston,LEW1,Line Haul,{week},{unit},{week},0.5";
}
