using Waa.App.Data;
using Waa.App.Services;
using Waa.App.ViewModels;
using Waa.Core;
using Xunit;

namespace Waa.App.Tests;

public sealed class DriverQueueTests
{
    [Fact]
    public void Order_PutsOpenIdleFollowUpThenAttemptedThenNotContacted()
    {
        var rows = new[]
        {
            Row("N00001", "Not Contacted Example", 70m, null, 0),
            Row("A00002", "Attempted Example", 60m, IdleContactOutcome.Attempted, 0),
            Row("F00003", "Follow-up Example", 55m, IdleContactOutcome.SpokeFollowUp, 0),
            Row("F00004", "Higher Follow-up Example", 80m, IdleContactOutcome.SpokeFollowUp, 0)
        };

        var ordered = DriverQueueOrderer.Order(rows);

        Assert.Equal(
            new[] { "F00004", "F00003", "A00002", "N00001" },
            ordered.Select(driver => driver.DriverCode));
    }

    [Fact]
    public void Order_PutsAboveThresholdSpokeDriversBeforeOrdinaryRemainingFleet()
    {
        var highCompleted = Row(
            "H00001",
            "High Completed Example",
            64m,
            IdleContactOutcome.Spoke,
            0);
        var lowOpen = Row(
            "L00002",
            "Low Open Example",
            25m,
            null,
            2);

        var ordered = DriverQueueOrderer.Order(new[] { lowOpen, highCompleted });

        Assert.Equal("H00001", ordered[0].DriverCode);
        Assert.Equal("L00002", ordered[1].DriverCode);
    }

    [Fact]
    public void Order_PutsOpenOrdinaryWorkBeforeClearRemainingDrivers()
    {
        var lowClear = Row("C00001", "Clear Example", 20m, null, 0);
        var lowOpen = Row("O00002", "Open Example", 21m, null, 1);

        var ordered = DriverQueueOrderer.Order(new[] { lowClear, lowOpen });

        Assert.Equal("O00002", ordered[0].DriverCode);
        Assert.Equal("C00001", ordered[1].DriverCode);
    }

    [Fact]
    public void Order_WithinAboveThresholdSpokeBandPutsOpenWorkFirst()
    {
        var highClear = Row(
            "C00001",
            "High Clear Example",
            75m,
            IdleContactOutcome.Spoke,
            0);
        var highOpen = Row(
            "O00002",
            "High Open Example",
            55m,
            IdleContactOutcome.Spoke,
            1);

        var ordered = DriverQueueOrderer.Order(new[] { highClear, highOpen });

        Assert.Equal("O00002", ordered[0].DriverCode);
        Assert.Equal("C00001", ordered[1].DriverCode);
    }

    [Fact]
    public void Order_ReevaluatesImmediatelyForDifferentThresholdWithoutChangingRecords()
    {
        var record = WorkEntryTestData.FleetDriver(
            "T00001",
            "Threshold Example",
            55m,
            IdleContactOutcome.Spoke,
            0);
        var lowThreshold = new DriverRowViewModel(record, 50m);
        var highThreshold = new DriverRowViewModel(record, 60m);

        Assert.Equal(1, lowThreshold.PriorityBand);
        Assert.Equal(3, highThreshold.PriorityBand);
        Assert.Equal(IdleContactOutcome.Spoke, record.LatestOutcome);
        Assert.Equal(0, record.OpenWorkCount);
    }

    [Fact]
    public async Task NextNeedingAttention_PrefersVisibleUnfinishedIdleBeforeOrdinaryOpenWork()
    {
        using var fixture = new RepositoryFixture();
        fixture.Repository.RecordManualWork(
            fixture.Driver("C00003"),
            WorkEntryStatus.Waiting,
            "Visible ordinary work.");
        var fleet = fixture.Repository.LoadFleet();
        var current = new DriverRowViewModel(
            fleet.Drivers.Single(driver => driver.DriverCode == "D00004"),
            50m);
        var ordinary = new DriverRowViewModel(
            fleet.Drivers.Single(driver => driver.DriverCode == "C00003"),
            50m);
        var highIdle = new DriverRowViewModel(
            fleet.Drivers.Single(driver => driver.DriverCode == "A00001"),
            50m);
        var viewModel = CreateMainViewModel(fixture);
        viewModel.Drivers.Add(current);
        viewModel.Drivers.Add(ordinary);
        viewModel.Drivers.Add(highIdle);
        viewModel.SelectedDriver = current;
        await WaitUntilAsync(() => !viewModel.Work.IsBusy);

        viewModel.NextNeedingAttentionCommand.Execute(null);
        await WaitUntilAsync(() => viewModel.SelectedDriver?.DriverCode == "A00001");

        Assert.Equal("A00001", viewModel.SelectedDriver?.DriverCode);
    }

    [Fact]
    public async Task NextNeedingAttention_UsesOnlyCurrentVisibleQueue()
    {
        using var fixture = new RepositoryFixture();
        fixture.Repository.RecordManualWork(
            fixture.Driver("C00003"),
            WorkEntryStatus.Waiting,
            "Visible ordinary work.");
        var fleet = fixture.Repository.LoadFleet();
        var current = new DriverRowViewModel(
            fleet.Drivers.Single(driver => driver.DriverCode == "D00004"),
            50m);
        var visibleOrdinary = new DriverRowViewModel(
            fleet.Drivers.Single(driver => driver.DriverCode == "C00003"),
            50m);
        var viewModel = CreateMainViewModel(fixture);
        viewModel.Drivers.Add(current);
        viewModel.Drivers.Add(visibleOrdinary);
        viewModel.SelectedDriver = current;
        await WaitUntilAsync(() => !viewModel.Work.IsBusy);

        viewModel.NextNeedingAttentionCommand.Execute(null);
        await WaitUntilAsync(() => viewModel.SelectedDriver?.DriverCode == "C00003");

        Assert.Equal("C00003", viewModel.SelectedDriver?.DriverCode);
        Assert.DoesNotContain(viewModel.Drivers, driver => driver.DriverCode == "A00001");
    }

    [Fact]
    public async Task NextNeedingAttention_KeepsSelectionWhenNoOtherVisibleDriverNeedsWork()
    {
        using var fixture = new RepositoryFixture();
        var current = new DriverRowViewModel(fixture.Driver("D00004"), 50m);
        var viewModel = CreateMainViewModel(fixture);
        viewModel.Drivers.Add(current);
        viewModel.SelectedDriver = current;
        await WaitUntilAsync(() => !viewModel.Work.IsBusy);

        viewModel.NextNeedingAttentionCommand.Execute(null);
        await Task.Delay(50);

        Assert.Same(current, viewModel.SelectedDriver);
        Assert.Equal(
            "No other visible drivers currently need attention.",
            viewModel.StatusMessage);
    }

    private static DriverRowViewModel Row(
        string code,
        string name,
        decimal idlePercent,
        IdleContactOutcome? outcome,
        int openWorkCount) =>
        new(
            WorkEntryTestData.FleetDriver(
                code,
                name,
                idlePercent,
                outcome,
                openWorkCount),
            50m);

    private static MainViewModel CreateMainViewModel(RepositoryFixture fixture) =>
        new(
            fixture.Repository,
            new ReportUpdateService(fixture.Repository, new RollingSevenDayCsvParser()),
            new RecordingClipboard());

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }
}
