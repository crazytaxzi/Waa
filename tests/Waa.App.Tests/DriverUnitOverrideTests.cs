using Waa.App.Data;
using Xunit;

namespace Waa.App.Tests;

public sealed class DriverUnitOverrideTests
{
    [Fact]
    public void ManualUnitOverride_WinsAcrossReportRefresh_AndClearRestoresReportUnit()
    {
        using var fixture = new RepositoryFixture();

        fixture.Repository.SetDriverUnitOverride("A00001", "299999");
        var assigned = fixture.Driver("A00001");
        var assignment = Assert.IsType<DriverUnitAssignmentRecord>(
            fixture.Repository.GetDriverUnitAssignment("A00001"));

        Assert.Equal("299999", assigned.UnitCode);
        Assert.Equal("270101", assignment.ReportUnitCode);
        Assert.Equal("299999", assignment.ManualUnitCode);
        Assert.True(assignment.HasManualOverride);

        fixture.ImportFleet(
            new DateOnly(2026, 9, 6),
            new SyntheticDriver("A00001", "Alex Example", "LEAD000001", "288888", 44m),
            new SyntheticDriver("B00002", "Blair Example", "LEAD000002", "270202", 48m),
            new SyntheticDriver("C00003", "Casey Example", "LEAD000003", "270303", 28m),
            new SyntheticDriver("D00004", "Drew Example", "LEAD000004", "270404", 16m));

        var afterRefresh = fixture.Driver("A00001");
        assignment = Assert.IsType<DriverUnitAssignmentRecord>(
            fixture.Repository.GetDriverUnitAssignment("A00001"));
        Assert.Equal("299999", afterRefresh.UnitCode);
        Assert.Equal("288888", assignment.ReportUnitCode);
        Assert.Equal("299999", assignment.ManualUnitCode);

        Assert.True(fixture.Repository.ClearDriverUnitOverride("A00001"));
        var restored = fixture.Driver("A00001");
        assignment = Assert.IsType<DriverUnitAssignmentRecord>(
            fixture.Repository.GetDriverUnitAssignment("A00001"));
        Assert.Equal("288888", restored.UnitCode);
        Assert.False(assignment.HasManualOverride);
        Assert.Null(assignment.ManualUnitCode);
    }

    [Fact]
    public void ManualUnitOverride_IsUsedByNewHistoricalWorkSnapshot()
    {
        using var fixture = new RepositoryFixture();
        fixture.Repository.SetDriverUnitOverride("B00002", "277777");
        var assigned = fixture.Driver("B00002");

        var id = fixture.Repository.RecordManualWork(
            assigned,
            WorkEntryStatus.Waiting,
            "Synthetic work under manual truck assignment.");

        var work = Assert.IsType<WorkEntryRecord>(fixture.Repository.GetWorkEntry(id));
        Assert.Equal("277777", work.UnitCodeSnapshot);
        Assert.Equal("277777", assigned.UnitCode);
    }

    [Fact]
    public void ManualUnitOverride_RejectsUnknownDriver()
    {
        using var fixture = new RepositoryFixture();
        Assert.Throws<InvalidOperationException>(() =>
            fixture.Repository.SetDriverUnitOverride("NO-SUCH-DRIVER", "299999"));
    }
}
