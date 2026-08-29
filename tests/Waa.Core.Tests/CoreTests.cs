using Waa.Core;
using Xunit;

namespace Waa.Core.Tests;

public sealed class DriverLabelParserTests
{
    [Fact]
    public void Parse_UsesLeadingAlphanumericTokenAsCodeAndRemainderAsName()
    {
        var driver = DriverLabelParser.Parse("ab1234   Jamie Example Driver");

        Assert.Equal("AB1234", driver.DriverCode);
        Assert.Equal("Jamie Example Driver", driver.DriverName);
        Assert.Equal("ab1234   Jamie Example Driver", driver.RawLabel);
    }

    [Fact]
    public void Parse_RejectsCodeContainingPunctuation()
    {
        var exception = Assert.Throws<ReportValidationException>(
            () => DriverLabelParser.Parse("AB-123 Jamie Example"));

        Assert.Contains("letters or digits", exception.Message);
    }

    [Fact]
    public void ParseDriverLeader_AcceptsTenCharacterCode()
    {
        Assert.Equal("LEADER0001", DriverLabelParser.ParseDriverLeader("leader0001"));
    }

    [Fact]
    public void ParseDriverLeader_RejectsMoreThanTenCharacters()
    {
        var exception = Assert.Throws<ReportValidationException>(
            () => DriverLabelParser.ParseDriverLeader("LEADER00001"));

        Assert.Contains("10-character maximum", exception.Message);
    }
}

public sealed class RollingSevenDayCsvParserTests
{
    [Fact]
    public void Parse_NormalizesMeasureRowsAndCalculatesWeightedIdle()
    {
        var import = new RollingSevenDayCsvParser().Parse(BuildCsv(
            ("8/23/2026", "40", "20", "270139"),
            ("8/16/2026", "80", "24", "270139"),
            ("8/9/2026", "30", "18", "260001"),
            ("8/2/2026", "50", "10", "260001")));

        var driver = Assert.Single(import.Drivers);
        Assert.Equal(new DateOnly(2026, 8, 23), import.ReportCycleDate);
        Assert.Equal(4, import.Observations.Count);
        Assert.Equal("AB1234", driver.Driver.DriverCode);
        Assert.Equal("Jamie Example", driver.Driver.DriverName);
        Assert.Equal("270139", driver.UnitCode);
        Assert.Equal("LEADER0001", driver.DriverLeader);
        Assert.Equal(50m, driver.IdlePercent7Day);
        Assert.Equal(36m, driver.IdlePercent28Day);
        Assert.True(driver.IsComplete28Day);
        Assert.Equal(4, driver.Coverage28Day);
        Assert.Equal(36m, import.Fleet.IdlePercent28Day);
    }

    [Fact]
    public void Parse_MissingExpectedWeekShowsIncompleteCoverage()
    {
        var import = new RollingSevenDayCsvParser().Parse(BuildCsv(
            ("8/23/2026", "40", "20", "270139"),
            ("8/16/2026", "80", "24", "270139"),
            ("8/2/2026", "50", "10", "260001")));

        var driver = Assert.Single(import.Drivers);
        Assert.False(driver.IsComplete28Day);
        Assert.Equal(3, driver.Coverage28Day);
        Assert.Null(driver.IdlePercent28Day);
        Assert.Null(import.Fleet.IdlePercent28Day);
        Assert.Equal(0, import.Fleet.IncludedDrivers28Day);
    }

    [Fact]
    public void Parse_RejectsConflictingRepeatedMeasureRows()
    {
        var csv = Header + "\n" +
            Row("AB1234 Jamie Example", "OOR %", "8/23/2026", "40", "20", "270139") + "\n" +
            Row("AB1234 Jamie Example", "Idle %", "8/23/2026", "40", "21", "270139");

        var exception = Assert.Throws<ReportValidationException>(
            () => new RollingSevenDayCsvParser().Parse(csv));

        Assert.Contains("Conflicting repeated rows", exception.Message);
        Assert.Contains("Idle Hours", exception.Message);
    }

    [Fact]
    public void Parse_UnitChangeDoesNotCreateAnotherDriver()
    {
        var import = new RollingSevenDayCsvParser().Parse(BuildCsv(
            ("8/23/2026", "40", "20", "270139"),
            ("8/16/2026", "80", "24", "260001"),
            ("8/9/2026", "30", "18", "250001"),
            ("8/2/2026", "50", "10", "240001")));

        Assert.Single(import.Drivers);
        Assert.Equal(4, import.Observations.Count);
        Assert.Equal("270139", import.Drivers[0].UnitCode);
    }

    private const string Header =
        "Group by\u00A0 (copy),Measure Names,Week Start Date,[Rolling 7 Day Engine Time]/60," +
        "[Rolling 7 Day Idle Time]/60,Rolling 7 Day Dispatch Miles,Rolling 7 Day Qualcomm Miles," +
        "Cost Center,Driver Leader,Driver Terminal,Fleet Leader,OPS LOB,Rolling 7 Day Start Date," +
        "Unit Code,Week Start Date,Measure Values";

    private static string BuildCsv(params (string Week, string Engine, string Idle, string Unit)[] weeks)
    {
        var rows = new List<string> { Header };
        foreach (var week in weeks)
        {
            rows.Add(Row("AB1234 Jamie Example", "OOR %", week.Week, week.Engine, week.Idle, week.Unit));
            rows.Add(Row("AB1234 Jamie Example", "Idle %", week.Week, week.Engine, week.Idle, week.Unit));
        }

        return string.Join("\r\n", rows);
    }

    private static string Row(
        string driver,
        string measure,
        string week,
        string engine,
        string idle,
        string unit) =>
        $"{driver},{measure},{week},{engine},{idle},1000,1010,611 - Lewiston - Van,LEADER0001,Lewiston,LEW1,Line Haul,{week},{unit},{week},0.5";
}

public sealed class DriverIdleSnapshotTests
{
    [Fact]
    public void IsAboveThreshold_UsesEitherSevenOrCompleteTwentyEightDayValue()
    {
        var driver = new DriverIdentity("AB1234", "Jamie Example", "AB1234 Jamie Example");
        var snapshot = new DriverIdleSnapshot(
            driver,
            new DateOnly(2026, 8, 23),
            "270139",
            "LEADER0001",
            40m,
            22m,
            55m,
            200m,
            80m,
            40m,
            4,
            true);

        Assert.True(snapshot.IsAboveThreshold(50m));
        Assert.False(snapshot.IsAboveThreshold(55m));
    }
}
