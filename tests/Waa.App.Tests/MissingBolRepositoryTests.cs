using Microsoft.Data.Sqlite;
using Waa.App.Data;
using Waa.Core;
using Xunit;

namespace Waa.App.Tests;

public sealed class MissingBolRepositoryTests
{
    private static readonly DateTimeOffset FirstImportUtc =
        new(2026, 8, 30, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void FirstImport_PersistsMatchedAndUnmatchedItemsAndCreatesOnlyMatchedTasks()
    {
        using var fixture = new RepositoryFixture();
        var bol = CreateRepository(fixture);

        var result = Import(
            bol,
            "HASH-FIRST",
            Item("SYN2001", "A00001"),
            Item("SYN2002", "A00001", date: new DateOnly(2026, 8, 28)),
            Item("SYN2003", "UNKNOWN", sourceName: "Unknown Synthetic"),
            Item("SYN2004", string.Empty, sourceName: "Blank Code Synthetic"));

        Assert.True(result.Imported);
        Assert.Equal(4, result.ItemCount);
        Assert.Equal(2, result.CreatedTaskCount);
        Assert.Equal(4, fixture.ScalarLong("SELECT COUNT(*) FROM missing_bol_items;"));
        Assert.Equal(2, fixture.ScalarLong("SELECT COUNT(*) FROM missing_bol_work_links WHERE source_kind = 'MissingBolTask';"));
        Assert.Equal(2, fixture.Driver("A00001").OpenWorkCount);
        var fleet = bol.LoadFleetState();
        Assert.Equal(2, fleet.OpenMatchedCount);
        Assert.Equal(2, fleet.UnmatchedItems.Count);
    }

    [Fact]
    public void SameHash_IsIdempotentAndDoesNotDuplicateItemsOrTasks()
    {
        using var fixture = new RepositoryFixture();
        var bol = CreateRepository(fixture);
        var source = Item("SYN2010", "A00001");

        var first = Import(bol, "HASH-SAME", source);
        var second = Import(bol, "HASH-SAME", source);

        Assert.True(first.Imported);
        Assert.True(second.AlreadyAccepted);
        Assert.False(second.Imported);
        Assert.Equal(1, fixture.ScalarLong("SELECT COUNT(*) FROM missing_bol_imports;"));
        Assert.Equal(1, fixture.ScalarLong("SELECT COUNT(*) FROM missing_bol_items;"));
        Assert.Equal(1, fixture.ScalarLong("SELECT COUNT(*) FROM missing_bol_work_links WHERE source_kind = 'MissingBolTask';"));
    }

    [Fact]
    public void NewSnapshotSameOrder_UpdatesSourceContextWithoutResettingLocalStatusOrTask()
    {
        using var fixture = new RepositoryFixture();
        var bol = CreateRepository(fixture);
        Import(bol, "HASH-CONTEXT-1", Item("SYN2020", "A00001"));
        var original = Assert.IsType<MissingBolItemRecord>(bol.GetItemByOrder("SYN2020"));
        bol.RecordAction(original.Id, MissingBolActionOutcome.Requested, "Synthetic request sent", FirstImportUtc.AddMinutes(5));

        var updated = Item("SYN2020", "A00001") with
        {
            OriginCityState = "Portland, OR",
            DestinationCityState = "Spokane, WA",
            BillTo = "Updated Synthetic Customer"
        };
        Import(bol, "HASH-CONTEXT-2", updated, FirstImportUtc.AddDays(1));

        var current = Assert.IsType<MissingBolItemRecord>(bol.GetItemByOrder("syn2020"));
        Assert.Equal(MissingBolStatus.Requested, current.CurrentStatus);
        Assert.Equal(original.TaskWorkEntryId, current.TaskWorkEntryId);
        Assert.Equal("Updated Synthetic Customer", current.BillTo);
        var task = Assert.IsType<WorkEntryRecord>(fixture.Repository.GetWorkEntry(current.TaskWorkEntryId!.Value));
        Assert.Contains("Portland, OR → Spokane, WA", task.Text, StringComparison.Ordinal);
        Assert.Contains("Status: Requested", task.Text, StringComparison.Ordinal);
        Assert.Single(bol.LoadActionHistory(current.Id));
        Assert.Equal(1, fixture.ScalarLong("SELECT COUNT(*) FROM missing_bol_work_links WHERE source_kind = 'MissingBolTask';"));
    }

    [Fact]
    public void DisappearingItem_IsMarkedAbsentButRemainsUnresolvedWithItsTask()
    {
        using var fixture = new RepositoryFixture();
        var bol = CreateRepository(fixture);
        Import(
            bol,
            "HASH-ABSENT-1",
            Item("SYN2030", "A00001"),
            Item("SYN2031", "B00002"));
        var first = Assert.IsType<MissingBolItemRecord>(bol.GetItemByOrder("SYN2030"));

        Import(bol, "HASH-ABSENT-2", Item("SYN2031", "B00002"), FirstImportUtc.AddDays(1));

        var missing = Assert.IsType<MissingBolItemRecord>(bol.GetItemByOrder("SYN2030"));
        Assert.False(missing.IsPresentInLatestImport);
        Assert.False(missing.IsResolved);
        Assert.Equal(first.TaskWorkEntryId, missing.TaskWorkEntryId);
        var task = Assert.IsType<WorkEntryRecord>(fixture.Repository.GetWorkEntry(missing.TaskWorkEntryId!.Value));
        Assert.Null(task.ResolvedUtc);
        Assert.Equal(1, fixture.Driver("A00001").OpenWorkCount);
    }

    [Fact]
    public void ExactMatching_HandlesNumericLookingAndLeadingZeroDriverCodesAsText()
    {
        using var fixture = new RepositoryFixture();
        fixture.ImportFleet(
            new DateOnly(2026, 9, 6),
            new SyntheticDriver("000123", "Leading Zero Example", "LEAD000005", "270505", 33m),
            new SyntheticDriver("123456", "Numeric Example", "LEAD000006", "270606", 34m));
        var bol = CreateRepository(fixture);

        Import(
            bol,
            "HASH-NUMERIC-CODES",
            Item("SYN2040", "000123"),
            Item("SYN2041", "123456"));

        Assert.Equal("000123", bol.GetItemByOrder("SYN2040")?.MatchedDriverCode);
        Assert.Equal("123456", bol.GetItemByOrder("SYN2041")?.MatchedDriverCode);
        Assert.Equal(1, fixture.Driver("000123").OpenWorkCount);
        Assert.Equal(1, fixture.Driver("123456").OpenWorkCount);
    }

    [Fact]
    public void ExactCodeMatch_SurvivesSourceNameMismatchWithoutOverwritingDurableDriverName()
    {
        using var fixture = new RepositoryFixture();
        var bol = CreateRepository(fixture);

        Import(
            bol,
            "HASH-NAME-MISMATCH",
            Item("SYN2050", "a00001", sourceName: "Different Source Name"));

        var item = Assert.IsType<MissingBolItemRecord>(bol.GetItemByOrder("SYN2050"));
        Assert.Equal("A00001", item.MatchedDriverCode);
        Assert.Equal("Alex Example", item.MatchedDriverName);
        Assert.Equal("Different Source Name", item.SourceDriverName);
        Assert.True(item.SourceNameDiffersFromDriver);
        Assert.Equal("Alex Example", fixture.Driver("A00001").DriverName);
    }

    [Fact]
    public void UnknownAndBlankCodes_RemainUnmatchedAndCreateNoDriverOwnedTask()
    {
        using var fixture = new RepositoryFixture();
        var bol = CreateRepository(fixture);

        Import(
            bol,
            "HASH-UNMATCHED",
            Item("SYN2060", "ZZ9999"),
            Item("SYN2061", string.Empty));

        Assert.Null(bol.GetItemByOrder("SYN2060")?.MatchedDriverCode);
        Assert.Null(bol.GetItemByOrder("SYN2061")?.MatchedDriverCode);
        Assert.Equal(2, bol.LoadFleetState().UnmatchedItems.Count);
        Assert.Equal(0, fixture.ScalarLong("SELECT COUNT(*) FROM missing_bol_work_links WHERE source_kind = 'MissingBolTask';"));
    }

    [Fact]
    public void Matching_DoesNotUseIdenticalDriverNameWhenCodeIsUnknown()
    {
        using var fixture = new RepositoryFixture();
        var bol = CreateRepository(fixture);

        Import(
            bol,
            "HASH-NO-NAME-MATCH",
            Item("SYN2070", "NOPE01", sourceName: "Alex Example"));

        var item = Assert.IsType<MissingBolItemRecord>(bol.GetItemByOrder("SYN2070"));
        Assert.Null(item.MatchedDriverCode);
        Assert.Null(item.TaskWorkEntryId);
    }

    [Fact]
    public void LaterExactRosterCode_AttachesPreviouslyUnmatchedItemExactlyOnce()
    {
        using var fixture = new RepositoryFixture();
        var bol = CreateRepository(fixture);
        Import(bol, "HASH-LATER-MATCH", Item("SYN2080", "Z00005"));
        Assert.Null(bol.GetItemByOrder("SYN2080")?.MatchedDriverCode);

        fixture.ImportFleet(
            new DateOnly(2026, 9, 6),
            new SyntheticDriver("A00001", "Alex Example", "LEAD000001", "270101", 44m),
            new SyntheticDriver("B00002", "Blair Example", "LEAD000002", "270202", 42m),
            new SyntheticDriver("C00003", "Casey Example", "LEAD000003", "270303", 30m),
            new SyntheticDriver("D00004", "Drew Example", "LEAD000004", "270404", 20m),
            new SyntheticDriver("Z00005", "Later Exact Example", "LEAD000005", "270505", 25m));

        Assert.Equal(1, bol.AttachExactMatchesAndCreateTasks(FirstImportUtc.AddDays(7)));
        Assert.Equal(0, bol.AttachExactMatchesAndCreateTasks(FirstImportUtc.AddDays(7).AddMinutes(1)));
        var item = Assert.IsType<MissingBolItemRecord>(bol.GetItemByOrder("SYN2080"));
        Assert.Equal("Z00005", item.MatchedDriverCode);
        Assert.NotNull(item.TaskWorkEntryId);
        Assert.Equal(1, fixture.ScalarLong("SELECT COUNT(*) FROM missing_bol_work_links WHERE source_kind = 'MissingBolTask';"));
    }

    [Fact]
    public void ConflictingDriverCodeReassignment_RejectsCompleteSnapshotAndPreservesPriorState()
    {
        using var fixture = new RepositoryFixture();
        var bol = CreateRepository(fixture);
        Import(bol, "HASH-CONFLICT-1", Item("SYN2090", "A00001"));

        var exception = Assert.Throws<ReportValidationException>(() =>
            Import(
                bol,
                "HASH-CONFLICT-2",
                [Item("SYN2090", "B00002"), Item("SYN2091", "B00002")],
                FirstImportUtc.AddDays(1)));

        Assert.Contains("changed Last Dispatch Driver cd", exception.Message, StringComparison.Ordinal);
        Assert.Equal("A00001", bol.GetItemByOrder("SYN2090")?.MatchedDriverCode);
        Assert.Null(bol.GetItemByOrder("SYN2091"));
        Assert.Equal(1, fixture.ScalarLong("SELECT COUNT(*) FROM missing_bol_imports;"));
        Assert.Equal(1, fixture.ScalarLong("SELECT COUNT(*) FROM missing_bol_items;"));
    }

    [Fact]
    public void LinkedTask_SnapshotsUnitLeaderAndSourceImportAndSurvivesRestart()
    {
        using var fixture = new RepositoryFixture();
        var bol = CreateRepository(fixture);
        Import(bol, "HASH-SNAPSHOT", Item("SYN2100", "A00001"));
        var item = Assert.IsType<MissingBolItemRecord>(bol.GetItemByOrder("SYN2100"));
        var task = Assert.IsType<WorkEntryRecord>(fixture.Repository.GetWorkEntry(item.TaskWorkEntryId!.Value));

        Assert.Equal("270101", task.UnitCodeSnapshot);
        Assert.Equal("LEAD000001", task.DriverLeaderSnapshot);
        Assert.Equal(
            1,
            fixture.ScalarLong($"SELECT source_import_id FROM missing_bol_work_links WHERE work_entry_id = {task.Id};"));

        var restartedWork = new WaaRepository(fixture.DatabasePath);
        restartedWork.Initialize();
        var restartedBol = new MissingBolRepository(fixture.DatabasePath);
        restartedBol.Initialize();
        var restartedItem = Assert.IsType<MissingBolItemRecord>(restartedBol.GetItemByOrder("SYN2100"));
        var restartedTask = Assert.IsType<WorkEntryRecord>(restartedWork.GetWorkEntry(restartedItem.TaskWorkEntryId!.Value));
        Assert.Null(restartedTask.ResolvedUtc);
        Assert.Equal(WorkEntrySource.MissingBolTask, restartedBol.ApplyWorkSources([restartedTask]).Single().Source);
    }

    [Theory]
    [InlineData(MissingBolActionOutcome.Requested, MissingBolStatus.Requested, "Requested missing BOL")]
    [InlineData(MissingBolActionOutcome.Attempted, MissingBolStatus.Attempted, "Attempted contact regarding missing BOL")]
    [InlineData(MissingBolActionOutcome.FollowUp, MissingBolStatus.FollowUp, "requires follow-up")]
    public void UnresolvedActions_UpdateStatusKeepTaskOpenAndCreateOneCompletedActivity(
        MissingBolActionOutcome outcome,
        MissingBolStatus expectedStatus,
        string expectedText)
    {
        using var fixture = new RepositoryFixture();
        var bol = CreateRepository(fixture);
        Import(bol, $"HASH-ACTION-{outcome}", Item("SYN2110", "A00001"));
        var before = Assert.IsType<MissingBolItemRecord>(bol.GetItemByOrder("SYN2110"));

        bol.RecordAction(before.Id, outcome, "Synthetic note", FirstImportUtc.AddHours(1));

        var item = Assert.IsType<MissingBolItemRecord>(bol.GetItemByOrder("SYN2110"));
        Assert.Equal(expectedStatus, item.CurrentStatus);
        Assert.Null(item.ResolvedUtc);
        var task = Assert.IsType<WorkEntryRecord>(fixture.Repository.GetWorkEntry(item.TaskWorkEntryId!.Value));
        Assert.Null(task.ResolvedUtc);
        var displayStatus = expectedStatus == MissingBolStatus.FollowUp ? "Follow-up" : expectedStatus.ToString();
        Assert.Contains($"Status: {displayStatus}", task.Text, StringComparison.Ordinal);
        var action = Assert.Single(bol.LoadActionHistory(item.Id));
        var activity = Assert.IsType<WorkEntryRecord>(fixture.Repository.GetWorkEntry(action.LinkedWorkEntryId));
        Assert.Equal(WorkEntrySource.MissingBolAction, bol.ApplyWorkSources([activity]).Single().Source);
        Assert.Equal(WorkEntryStatus.Done, activity.Status);
        Assert.NotNull(activity.ResolvedUtc);
        Assert.Contains(expectedText, activity.Text, StringComparison.Ordinal);
        Assert.Contains("Synthetic note.", activity.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolved_AtomicallyResolvesItemAndTaskAndCreatesCompletedActivity()
    {
        using var fixture = new RepositoryFixture();
        var bol = CreateRepository(fixture);
        Import(bol, "HASH-RESOLVE", Item("SYN2120", "A00001"));
        var item = Assert.IsType<MissingBolItemRecord>(bol.GetItemByOrder("SYN2120"));
        var resolvedAt = FirstImportUtc.AddHours(2);

        bol.RecordAction(item.Id, MissingBolActionOutcome.Resolved, null, resolvedAt);

        var resolved = Assert.IsType<MissingBolItemRecord>(bol.GetItemByOrder("SYN2120"));
        Assert.Equal(MissingBolStatus.Resolved, resolved.CurrentStatus);
        Assert.Equal(resolvedAt, resolved.ResolvedUtc);
        var task = Assert.IsType<WorkEntryRecord>(fixture.Repository.GetWorkEntry(resolved.TaskWorkEntryId!.Value));
        Assert.Equal(resolvedAt, task.ResolvedUtc);
        Assert.Equal(0, fixture.Driver("A00001").OpenWorkCount);
        var action = Assert.Single(bol.LoadActionHistory(resolved.Id));
        var activity = Assert.IsType<WorkEntryRecord>(fixture.Repository.GetWorkEntry(action.LinkedWorkEntryId));
        Assert.Contains("Resolved missing BOL", activity.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Reopen_ReopensSameTaskWithoutCreatingAnotherTask()
    {
        using var fixture = new RepositoryFixture();
        var bol = CreateRepository(fixture);
        Import(bol, "HASH-REOPEN", Item("SYN2130", "A00001"));
        var original = Assert.IsType<MissingBolItemRecord>(bol.GetItemByOrder("SYN2130"));
        bol.RecordAction(original.Id, MissingBolActionOutcome.Resolved, null, FirstImportUtc.AddHours(1));

        bol.RecordAction(original.Id, MissingBolActionOutcome.Reopen, "Needs another synthetic pass", FirstImportUtc.AddHours(2));

        var reopened = Assert.IsType<MissingBolItemRecord>(bol.GetItemByOrder("SYN2130"));
        Assert.Equal(MissingBolStatus.Open, reopened.CurrentStatus);
        Assert.Null(reopened.ResolvedUtc);
        Assert.Equal(original.TaskWorkEntryId, reopened.TaskWorkEntryId);
        var task = Assert.IsType<WorkEntryRecord>(fixture.Repository.GetWorkEntry(reopened.TaskWorkEntryId!.Value));
        Assert.Null(task.ResolvedUtc);
        Assert.Equal(1, fixture.Driver("A00001").OpenWorkCount);
        Assert.Equal(1, fixture.ScalarLong("SELECT COUNT(*) FROM missing_bol_work_links WHERE source_kind = 'MissingBolTask';"));
        Assert.Equal(2, bol.LoadActionHistory(reopened.Id).Count);
    }

    [Fact]
    public void Actions_AppendHistoryInsteadOfOverwritingPriorEvents()
    {
        using var fixture = new RepositoryFixture();
        var bol = CreateRepository(fixture);
        Import(bol, "HASH-HISTORY", Item("SYN2140", "A00001"));
        var item = Assert.IsType<MissingBolItemRecord>(bol.GetItemByOrder("SYN2140"));

        bol.RecordAction(item.Id, MissingBolActionOutcome.Requested, "First", FirstImportUtc.AddMinutes(1));
        bol.RecordAction(item.Id, MissingBolActionOutcome.Attempted, "Second", FirstImportUtc.AddMinutes(2));
        bol.RecordAction(item.Id, MissingBolActionOutcome.FollowUp, "Third", FirstImportUtc.AddMinutes(3));

        var history = bol.LoadActionHistory(item.Id);
        Assert.Equal(3, history.Count);
        Assert.Equal(
            new[]
            {
                MissingBolActionOutcome.Requested,
                MissingBolActionOutcome.Attempted,
                MissingBolActionOutcome.FollowUp
            },
            history.Select(action => action.Outcome));
        Assert.Equal(new[] { "First", "Second", "Third" }, history.Select(action => action.Note));
    }

    [Fact]
    public void ActionFailure_RollsBackItemTaskEventAndActivityTogether()
    {
        using var fixture = new RepositoryFixture();
        var bol = CreateRepository(fixture);
        Import(bol, "HASH-ROLLBACK", Item("SYN2150", "A00001"));
        var item = Assert.IsType<MissingBolItemRecord>(bol.GetItemByOrder("SYN2150"));
        fixture.ExecuteSql("""
            CREATE TRIGGER fail_synthetic_bol_action
            BEFORE INSERT ON missing_bol_action_events
            BEGIN
                SELECT RAISE(ABORT, 'synthetic BOL action failure');
            END;
            """);

        Assert.Throws<SqliteException>(() => bol.RecordAction(
            item.Id,
            MissingBolActionOutcome.Resolved,
            "Must survive",
            FirstImportUtc.AddHours(1)));

        var unchanged = Assert.IsType<MissingBolItemRecord>(bol.GetItemByOrder("SYN2150"));
        Assert.Equal(MissingBolStatus.Open, unchanged.CurrentStatus);
        Assert.Null(unchanged.ResolvedUtc);
        var task = Assert.IsType<WorkEntryRecord>(fixture.Repository.GetWorkEntry(unchanged.TaskWorkEntryId!.Value));
        Assert.Null(task.ResolvedUtc);
        Assert.Empty(bol.LoadActionHistory(unchanged.Id));
        Assert.Equal(1, fixture.ScalarLong("SELECT COUNT(*) FROM work_entries;"));
        Assert.Equal(1, fixture.Driver("A00001").OpenWorkCount);
    }

    [Fact]
    public void GenericWorkResolution_IsBlockedForLinkedMissingBolTask()
    {
        using var fixture = new RepositoryFixture();
        var bol = CreateRepository(fixture);
        Import(bol, "HASH-GUARD", Item("SYN2160", "A00001"));
        var item = Assert.IsType<MissingBolItemRecord>(bol.GetItemByOrder("SYN2160"));

        var exception = Assert.Throws<SqliteException>(() =>
            fixture.Repository.ResolveWorkEntry(item.TaskWorkEntryId!.Value));

        Assert.Contains("Missing BOL controls", exception.Message, StringComparison.Ordinal);
        Assert.Equal(MissingBolStatus.Open, bol.GetItemByOrder("SYN2160")?.CurrentStatus);
        Assert.Null(fixture.Repository.GetWorkEntry(item.TaskWorkEntryId.Value)?.ResolvedUtc);
    }

    [Fact]
    public void FleetState_UsesAggregateCountsOldestDatesAndOrderSearch()
    {
        using var fixture = new RepositoryFixture();
        var bol = CreateRepository(fixture);
        Import(
            bol,
            "HASH-FLEET",
            Item("SYN2170", "A00001", date: new DateOnly(2026, 8, 29)),
            Item("SYN2171", "A00001", date: new DateOnly(2026, 8, 27)),
            Item("SYN2172", "B00002", date: new DateOnly(2026, 8, 28)));

        var fleet = bol.LoadFleetState();

        Assert.Equal(3, fleet.OpenMatchedCount);
        Assert.Equal(2, fleet.DriverSummaries["A00001"].OpenCount);
        Assert.Equal(new DateOnly(2026, 8, 27), fleet.DriverSummaries["A00001"].OldestOpenEmptyCallDate);
        Assert.Contains("SYN2170", fleet.DriverSummaries["A00001"].OrderSearchText, StringComparison.Ordinal);
        Assert.Contains("SYN2171", fleet.DriverSummaries["A00001"].OrderSearchText, StringComparison.Ordinal);
        Assert.Equal(1, fleet.DriverSummaries["B00002"].OpenCount);
    }

    [Fact]
    public void DriverLoad_ReturnsMultipleItemsWithUnresolvedItemsFirst()
    {
        using var fixture = new RepositoryFixture();
        var bol = CreateRepository(fixture);
        Import(
            bol,
            "HASH-MULTIPLE",
            Item("SYN2180", "A00001", date: new DateOnly(2026, 8, 28)),
            Item("SYN2181", "A00001", date: new DateOnly(2026, 8, 27)));
        var resolved = Assert.IsType<MissingBolItemRecord>(bol.GetItemByOrder("SYN2181"));
        bol.RecordAction(resolved.Id, MissingBolActionOutcome.Resolved, null, FirstImportUtc.AddHours(1));

        var items = bol.LoadDriverItems("A00001");

        Assert.Equal(2, items.Count);
        Assert.Equal("SYN2180", items[0].SourceOrderNumber);
        Assert.False(items[0].IsResolved);
        Assert.Equal("SYN2181", items[1].SourceOrderNumber);
        Assert.True(items[1].IsResolved);
    }

    [Fact]
    public void ResolvedItemPresentAgain_KeepsExplicitResolutionAndMarksWarningState()
    {
        using var fixture = new RepositoryFixture();
        var bol = CreateRepository(fixture);
        Import(bol, "HASH-RETURN-1", Item("SYN2190", "A00001"));
        var item = Assert.IsType<MissingBolItemRecord>(bol.GetItemByOrder("SYN2190"));
        bol.RecordAction(item.Id, MissingBolActionOutcome.Resolved, null, FirstImportUtc.AddHours(1));

        Import(bol, "HASH-RETURN-2", Item("SYN2190", "A00001"), FirstImportUtc.AddDays(1));

        var returned = Assert.IsType<MissingBolItemRecord>(bol.GetItemByOrder("SYN2190"));
        Assert.Equal(MissingBolStatus.Resolved, returned.CurrentStatus);
        Assert.NotNull(returned.ResolvedUtc);
        Assert.True(returned.IsPresentInLatestImport);
        Assert.True(returned.ReturnedAfterResolution);
        Assert.Equal(item.TaskWorkEntryId, returned.TaskWorkEntryId);
        Assert.NotNull(fixture.Repository.GetWorkEntry(returned.TaskWorkEntryId!.Value)?.ResolvedUtc);
    }

    private static MissingBolRepository CreateRepository(RepositoryFixture fixture)
    {
        var repository = new MissingBolRepository(fixture.DatabasePath);
        repository.Initialize();
        return repository;
    }

    private static MissingBolImportResult Import(
        MissingBolRepository repository,
        string hash,
        params MissingBolSourceItem[] items) =>
        Import(repository, hash, items, FirstImportUtc);

    private static MissingBolImportResult Import(
        MissingBolRepository repository,
        string hash,
        MissingBolSourceItem item,
        DateTimeOffset importedUtc) =>
        Import(repository, hash, [item], importedUtc);

    private static MissingBolImportResult Import(
        MissingBolRepository repository,
        string hash,
        MissingBolSourceItem[] items,
        DateTimeOffset importedUtc) =>
        repository.ImportWorkbook(
            new MissingBolWorkbookImport("Synthetic Sheet", items),
            "Order Details Missing BOL-synthetic.xlsx",
            @"C:\Synthetic\Order Details Missing BOL-synthetic.xlsx",
            hash,
            importedUtc.UtcDateTime,
            importedUtc);

    private static MissingBolSourceItem Item(
        string orderNumber,
        string driverCode,
        string sourceName = "Alex Source Name",
        DateOnly? date = null) =>
        new(
            MissingBolText.NormalizeExact(orderNumber),
            orderNumber,
            $"TMEX-{orderNumber}",
            $"LOG-{orderNumber}",
            "Synthetic Customer",
            "0611",
            date ?? new DateOnly(2026, 8, 27),
            "Boise, ID",
            "Auburn, WA",
            "Linehaul",
            "Synthetic Terminal",
            "LEAD-BOL",
            "Active",
            driverCode.Trim(),
            MissingBolText.NormalizeExact(driverCode),
            sourceName,
            125m,
            130m,
            2);
}
