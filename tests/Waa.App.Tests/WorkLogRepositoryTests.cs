using Microsoft.Data.Sqlite;
using Waa.App.Data;
using Waa.App.Services;
using Xunit;

namespace Waa.App.Tests;

public sealed class WorkLogRepositoryTests
{
    [Fact]
    public void ManualDone_PersistsTrimmedAndCompletedImmediately()
    {
        using var fixture = new RepositoryFixture();
        var driver = fixture.Driver("A00001");

        var id = fixture.Repository.RecordManualWork(
            driver,
            WorkEntryStatus.Done,
            "  Confirmed synthetic ETA.  ");

        var entry = Assert.IsType<WorkEntryRecord>(fixture.Repository.GetWorkEntry(id));
        Assert.Equal("Confirmed synthetic ETA.", entry.Text);
        Assert.Equal(WorkEntryStatus.Done, entry.Status);
        Assert.Equal(WorkEntrySource.Manual, entry.Source);
        Assert.NotNull(entry.ResolvedUtc);
        Assert.Equal(entry.CreatedUtc, entry.ResolvedUtc);
        Assert.Equal(driver.UnitCode, entry.UnitCodeSnapshot);
        Assert.Equal(driver.DriverLeader, entry.DriverLeaderSnapshot);
    }

    [Theory]
    [InlineData(WorkEntryStatus.Waiting)]
    [InlineData(WorkEntryStatus.FollowUp)]
    public void ManualUnresolvedWork_PersistsAcrossRepositoryRestart(WorkEntryStatus status)
    {
        using var fixture = new RepositoryFixture();
        var id = fixture.Repository.RecordManualWork(
            fixture.Driver("A00001"),
            status,
            "Synthetic carry-forward item.");

        var restarted = new WaaRepository(fixture.DatabasePath);
        restarted.Initialize();

        var entry = Assert.IsType<WorkEntryRecord>(restarted.GetWorkEntry(id));
        Assert.Equal(status, entry.Status);
        Assert.Null(entry.ResolvedUtc);
        var state = restarted.LoadFleet();
        Assert.Equal(1, state.Drivers.Single(driver => driver.DriverCode == "A00001").OpenWorkCount);
    }

    [Fact]
    public void Resolve_SetsResolvedUtcWithoutChangingOriginalStatusTextOrCreation()
    {
        using var fixture = new RepositoryFixture();
        var created = new DateTimeOffset(2026, 8, 28, 14, 20, 0, TimeSpan.Zero);
        var resolved = new DateTimeOffset(2026, 8, 30, 17, 45, 0, TimeSpan.Zero);
        var id = fixture.Repository.RecordManualWork(
            fixture.Driver("B00002"),
            WorkEntryStatus.Waiting,
            "Waiting on synthetic dispatch detail.",
            created);

        Assert.True(fixture.Repository.ResolveWorkEntry(id, resolved));

        var entry = Assert.IsType<WorkEntryRecord>(fixture.Repository.GetWorkEntry(id));
        Assert.Equal(WorkEntryStatus.Waiting, entry.Status);
        Assert.Equal("Waiting on synthetic dispatch detail.", entry.Text);
        Assert.Equal(created, entry.CreatedUtc);
        Assert.Equal(resolved, entry.ResolvedUtc);
        Assert.Equal(0, fixture.Driver("B00002").OpenWorkCount);
    }

    [Fact]
    public void Reopen_ClearsResolutionAndRestoresOpenCount()
    {
        using var fixture = new RepositoryFixture();
        var id = fixture.Repository.RecordManualWork(
            fixture.Driver("C00003"),
            WorkEntryStatus.FollowUp,
            "Synthetic follow-up.");
        Assert.True(fixture.Repository.ResolveWorkEntry(id));

        Assert.True(fixture.Repository.ReopenWorkEntry(id));

        var entry = Assert.IsType<WorkEntryRecord>(fixture.Repository.GetWorkEntry(id));
        Assert.Equal(WorkEntryStatus.FollowUp, entry.Status);
        Assert.Null(entry.ResolvedUtc);
        Assert.Equal(1, fixture.Driver("C00003").OpenWorkCount);
    }

    [Theory]
    [InlineData(IdleContactOutcome.Spoke, WorkEntryStatus.Done, true)]
    [InlineData(IdleContactOutcome.Attempted, WorkEntryStatus.FollowUp, false)]
    [InlineData(IdleContactOutcome.SpokeFollowUp, WorkEntryStatus.FollowUp, false)]
    public void IdleContact_CreatesExactlyOneMappedLinkedWorkEntry(
        IdleContactOutcome outcome,
        WorkEntryStatus expectedStatus,
        bool expectedResolved)
    {
        using var fixture = new RepositoryFixture();
        var driver = fixture.Driver("A00001");

        var eventId = fixture.Repository.RecordIdleContact(
            driver,
            outcome,
            "Synthetic conversation note",
            50m);

        var entry = Assert.IsType<WorkEntryRecord>(
            fixture.Repository.GetWorkEntryForIdleContact(eventId));
        Assert.Equal(expectedStatus, entry.Status);
        Assert.Equal(expectedResolved, entry.ResolvedUtc is not null);
        Assert.Equal(WorkEntrySource.IdleContact, entry.Source);
        Assert.Equal(eventId, entry.LinkedIdleContactEventId);
        Assert.Contains("28D 62.0%", entry.Text, StringComparison.Ordinal);
        Assert.Contains("7D 62.0%", entry.Text, StringComparison.Ordinal);
        Assert.Contains("Synthetic conversation note.", entry.Text, StringComparison.Ordinal);
        Assert.Equal(
            1,
            fixture.ScalarLong($"SELECT COUNT(*) FROM work_entries WHERE linked_idle_contact_event_id = {eventId};"));
    }

    [Fact]
    public void IdleContact_UsesIncompleteCoverageWordingInsteadOfInventingPercentage()
    {
        using var fixture = new RepositoryFixture();
        var source = fixture.Driver("A00001");
        var incomplete = source with
        {
            IdlePercent28Day = null,
            Coverage28Day = 3,
            IsComplete28Day = false
        };

        var eventId = fixture.Repository.RecordIdleContact(
            incomplete,
            IdleContactOutcome.Attempted,
            null,
            50m);

        var entry = Assert.IsType<WorkEntryRecord>(
            fixture.Repository.GetWorkEntryForIdleContact(eventId));
        Assert.Contains("28D incomplete 3/4", entry.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("28D 62.0%", entry.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void IdleEventAndLinkedWorkEntry_AreAtomicWhenWorkInsertFails()
    {
        using var fixture = new RepositoryFixture();
        fixture.ExecuteSql("""
            CREATE TRIGGER fail_synthetic_idle_work
            BEFORE INSERT ON work_entries
            WHEN NEW.source = 'IdleContact'
            BEGIN
                SELECT RAISE(ABORT, 'synthetic linked work failure');
            END;
            """);

        Assert.Throws<SqliteException>(() => fixture.Repository.RecordIdleContact(
            fixture.Driver("A00001"),
            IdleContactOutcome.Spoke,
            null,
            50m));

        Assert.Equal(0, fixture.ScalarLong("SELECT COUNT(*) FROM idle_contact_events;"));
        Assert.Equal(0, fixture.ScalarLong("SELECT COUNT(*) FROM work_entries;"));
    }

    [Fact]
    public void LoadFleet_ReturnsAggregateOpenWorkCountsForAllDrivers()
    {
        using var fixture = new RepositoryFixture();
        fixture.Repository.RecordManualWork(
            fixture.Driver("C00003"),
            WorkEntryStatus.Waiting,
            "First synthetic wait.");
        fixture.Repository.RecordManualWork(
            fixture.Driver("D00004"),
            WorkEntryStatus.Waiting,
            "Second synthetic wait.");
        fixture.Repository.RecordManualWork(
            fixture.Driver("D00004"),
            WorkEntryStatus.FollowUp,
            "Synthetic follow-up.");
        fixture.Repository.RecordManualWork(
            fixture.Driver("A00001"),
            WorkEntryStatus.Done,
            "Completed synthetic item.");

        var fleet = fixture.Repository.LoadFleet();

        Assert.Equal(0, fleet.Drivers.Single(driver => driver.DriverCode == "A00001").OpenWorkCount);
        Assert.Equal(0, fleet.Drivers.Single(driver => driver.DriverCode == "B00002").OpenWorkCount);
        Assert.Equal(1, fleet.Drivers.Single(driver => driver.DriverCode == "C00003").OpenWorkCount);
        Assert.Equal(2, fleet.Drivers.Single(driver => driver.DriverCode == "D00004").OpenWorkCount);
    }

    [Fact]
    public void WorkHistoryRetainsOriginalUnitAndLeaderWhenRosterContextChanges()
    {
        using var fixture = new RepositoryFixture();
        var original = fixture.Driver("A00001");
        var id = fixture.Repository.RecordManualWork(
            original,
            WorkEntryStatus.Waiting,
            "Synthetic item before reassignment.");

        fixture.ImportFleet(
            new DateOnly(2026, 9, 6),
            new SyntheticDriver("A00001", "Alex Example", "LEAD009999", "279999", 44m),
            new SyntheticDriver("B00002", "Blair Example", "LEAD000002", "270202", 48m),
            new SyntheticDriver("C00003", "Casey Example", "LEAD000003", "270303", 28m),
            new SyntheticDriver("D00004", "Drew Example", "LEAD000004", "270404", 16m));

        var current = fixture.Driver("A00001");
        var historical = Assert.IsType<WorkEntryRecord>(fixture.Repository.GetWorkEntry(id));
        Assert.Equal("279999", current.UnitCode);
        Assert.Equal("LEAD009999", current.DriverLeader);
        Assert.Equal(original.UnitCode, historical.UnitCodeSnapshot);
        Assert.Equal(original.DriverLeader, historical.DriverLeaderSnapshot);
    }

    [Fact]
    public void TodayActivity_IncludesOldItemResolvedDuringLocalDay()
    {
        using var fixture = new RepositoryFixture();
        var timeZone = TimeZoneInfo.CreateCustomTimeZone(
            "Synthetic Mountain",
            TimeSpan.FromHours(-7),
            "Synthetic Mountain",
            "Synthetic Mountain");
        var now = new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.FromHours(-7));
        var day = LocalDayRange.Create(now, timeZone);
        var id = fixture.Repository.RecordManualWork(
            fixture.Driver("B00002"),
            WorkEntryStatus.Waiting,
            "Old synthetic wait resolved today.",
            day.StartUtc.AddDays(-2));
        fixture.Repository.ResolveWorkEntry(id, day.StartUtc.AddHours(4));

        var state = fixture.Repository.LoadDriverWork(
            "B00002",
            day.StartUtc,
            day.EndUtc);

        Assert.Empty(state.OpenEntries);
        var activity = Assert.Single(state.TodayEntries);
        Assert.Equal(id, activity.Id);
        Assert.Equal(WorkEntryStatus.Waiting, activity.Status);
        Assert.Equal(day.StartUtc.AddHours(4), activity.ResolvedUtc);
    }

    [Fact]
    public void ManualWork_RejectsBlankTextWithoutCreatingRecord()
    {
        using var fixture = new RepositoryFixture();

        Assert.Throws<ArgumentException>(() => fixture.Repository.RecordManualWork(
            fixture.Driver("A00001"),
            WorkEntryStatus.Waiting,
            "   "));
        Assert.Equal(0, fixture.ScalarLong("SELECT COUNT(*) FROM work_entries;"));
    }
}
