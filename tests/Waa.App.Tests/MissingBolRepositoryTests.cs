using Waa.App.Data;
using Waa.App.ViewModels;
using Waa.Core;
using Xunit;

namespace Waa.App.Tests;

public sealed class MissingBolRepositoryTests
{
    private static readonly DateTimeOffset LoadedUtc =
        new(2026, 9, 1, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CurrentWorkbook_IsHeldOnlyInMemoryAndDoesNotCreateBolTablesOrWork()
    {
        using var fixture = new RepositoryFixture();
        var bol = CreateRepository(fixture);

        var result = Import(
            bol,
            "HASH-CURRENT",
            Item("SYN2001", "A00001"),
            Item("SYN2002", "UNKNOWN"));

        Assert.True(result.Imported);
        Assert.Equal(2, result.ItemCount);
        Assert.Equal(0, result.CreatedTaskCount);
        Assert.Equal(1, bol.LoadFleetState().OpenMatchedCount);
        Assert.Single(bol.LoadFleetState().UnmatchedItems);
        Assert.Equal(
            0,
            fixture.ScalarLong(
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name LIKE 'missing_bol_%';"));
        Assert.Equal(0, fixture.Driver("A00001").OpenWorkCount);
    }

    [Fact]
    public void Restart_HasNoMissingBolStateUntilWorkbookIsLoadedAgain()
    {
        using var fixture = new RepositoryFixture();
        var first = CreateRepository(fixture);
        Import(first, "HASH-SESSION", Item("SYN2010", "A00001"));
        Assert.Equal(1, first.LoadFleetState().OpenMatchedCount);

        var restarted = CreateRepository(fixture);

        Assert.False(restarted.HasCurrentSnapshot);
        Assert.Equal(0, restarted.LoadFleetState().OpenMatchedCount);
        Assert.Empty(restarted.LoadDriverItems("A00001"));
        Assert.False(restarted.IsHashAccepted("HASH-SESSION"));
    }

    [Fact]
    public void NewWorkbookSnapshot_ReplacesRatherThanCarriesForwardPriorRows()
    {
        using var fixture = new RepositoryFixture();
        var bol = CreateRepository(fixture);
        Import(
            bol,
            "HASH-ONE",
            Item("SYN2020", "A00001"),
            Item("SYN2021", "A00001"));
        Assert.Equal(2, bol.LoadDriverItems("A00001").Count);

        Import(bol, "HASH-TWO", Item("SYN2021", "A00001"), LoadedUtc.AddMinutes(5));

        var current = bol.LoadDriverItems("A00001");
        var only = Assert.Single(current);
        Assert.Equal("SYN2021", only.SourceOrderNumber);
        Assert.Null(bol.GetItemByOrder("SYN2020"));
    }

    [Fact]
    public void SameHash_IsCurrentOnlyInsideTheCurrentSession()
    {
        using var fixture = new RepositoryFixture();
        var bol = CreateRepository(fixture);
        var first = Import(bol, "HASH-SAME", Item("SYN2030", "A00001"));
        var second = Import(bol, "HASH-SAME", Item("SYN2030", "A00001"));

        Assert.True(first.Imported);
        Assert.True(second.AlreadyAccepted);
        Assert.False(second.Imported);

        var restarted = CreateRepository(fixture);
        Assert.False(restarted.IsHashAccepted("HASH-SAME"));
    }

    [Fact]
    public void ExactDriverCodeMatch_PreservesLeadingZerosAndNeverUsesNameFallback()
    {
        using var fixture = new RepositoryFixture();
        fixture.ImportFleet(
            new DateOnly(2026, 9, 1),
            new SyntheticDriver("000123", "Leading Zero Example", "LEAD000005", "270505", 33m),
            new SyntheticDriver("A00001", "Alex Example", "LEAD000001", "270101", 40m));
        var bol = CreateRepository(fixture);

        Import(
            bol,
            "HASH-EXACT",
            Item("SYN2040", "000123"),
            Item("SYN2041", "NOPE01", sourceName: "Alex Example"));

        Assert.Equal("000123", bol.GetItemByOrder("SYN2040")?.MatchedDriverCode);
        Assert.Null(bol.GetItemByOrder("SYN2041")?.MatchedDriverCode);
        Assert.Single(bol.LoadFleetState().UnmatchedItems);
    }

    [Fact]
    public void ExactCodeMatch_AllowsSourceNameMismatchWithoutChangingRosterName()
    {
        using var fixture = new RepositoryFixture();
        var bol = CreateRepository(fixture);
        Import(
            bol,
            "HASH-NAME",
            Item("SYN2050", "a00001", sourceName: "Different Source Name"));

        var item = Assert.IsType<MissingBolItemRecord>(bol.GetItemByOrder("SYN2050"));
        Assert.Equal("A00001", item.MatchedDriverCode);
        Assert.Equal("Alex Example", item.MatchedDriverName);
        Assert.True(item.SourceNameDiffersFromDriver);
        Assert.Equal("Alex Example", fixture.Driver("A00001").DriverName);
    }

    [Fact]
    public void ActionsAreNotAvailableForSourceOnlyRows()
    {
        using var fixture = new RepositoryFixture();
        var bol = CreateRepository(fixture);
        Import(bol, "HASH-READONLY", Item("SYN2060", "A00001"));
        var item = Assert.IsType<MissingBolItemRecord>(bol.GetItemByOrder("SYN2060"));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            bol.RecordAction(item.Id, MissingBolActionOutcome.Resolved, "Should not persist"));

        Assert.Contains("read-only report view", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(bol.LoadActionHistory(item.Id));
        Assert.Null(bol.GetTaskWorkEntryId(item.Id));
    }

    [Fact]
    public void ClearCurrent_RemovesVisibleRowsWithoutTouchingWorkDatabase()
    {
        using var fixture = new RepositoryFixture();
        var bol = CreateRepository(fixture);
        Import(bol, "HASH-CLEAR", Item("SYN2070", "A00001"));
        var manualId = fixture.Repository.RecordManualWork(
            fixture.Driver("A00001"),
            WorkEntryStatus.Waiting,
            "Synthetic manual work");

        Assert.True(bol.ClearCurrent());

        Assert.False(bol.HasCurrentSnapshot);
        Assert.Equal(0, bol.LoadFleetState().OpenMatchedCount);
        Assert.NotNull(fixture.Repository.GetWorkEntry(manualId));
    }

    [Fact]
    public void LegacyBolLinks_AreClassifiedAndExcludedFromCurrentOpenWorkCountWithoutDeletion()
    {
        using var fixture = new RepositoryFixture();
        var legacyWorkId = fixture.Repository.RecordManualWork(
            fixture.Driver("A00001"),
            WorkEntryStatus.FollowUp,
            "Legacy generated BOL task");
        fixture.ExecuteSql("""
            CREATE TABLE missing_bol_work_links (
                work_entry_id INTEGER PRIMARY KEY,
                source_kind TEXT NOT NULL
            );
            """);
        fixture.ExecuteSql(
            $"INSERT INTO missing_bol_work_links(work_entry_id, source_kind) VALUES ({legacyWorkId}, 'MissingBolTask');");
        var bol = CreateRepository(fixture);

        var classified = bol.ApplyWorkSources([
            Assert.IsType<WorkEntryRecord>(fixture.Repository.GetWorkEntry(legacyWorkId))]);
        var summary = bol.LoadFleetState().DriverSummaries["A00001"];
        var row = new DriverRowViewModel(fixture.Driver("A00001"), 50m, summary);

        Assert.Equal(WorkEntrySource.MissingBolTask, Assert.Single(classified).Source);
        Assert.Equal(1, summary.LegacyOpenTaskCount);
        Assert.Equal(0, row.OpenWorkCount);
        Assert.NotNull(fixture.Repository.GetWorkEntry(legacyWorkId));
    }

    [Fact]
    public void CurrentWorkbook_CreatesTransientHandoffRowsWithoutDatabaseWorkRows()
    {
        using var fixture = new RepositoryFixture();
        var bol = CreateRepository(fixture);
        Import(bol, "HASH-HANDOFF", Item("SYN2080", "A00001"));
        var before = fixture.ScalarLong("SELECT COUNT(*) FROM work_entries;");

        var entries = bol.BuildCurrentHandoffEntries(fixture.Repository.LoadFleet().Drivers);

        var entry = Assert.Single(entries);
        Assert.Equal(WorkEntrySource.MissingBolTask, entry.Source);
        Assert.Contains("SYN2080", entry.Text, StringComparison.Ordinal);
        Assert.True(entry.Id < 0);
        Assert.Equal(before, fixture.ScalarLong("SELECT COUNT(*) FROM work_entries;"));
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
        Import(repository, hash, items, LoadedUtc);

    private static MissingBolImportResult Import(
        MissingBolRepository repository,
        string hash,
        IReadOnlyList<MissingBolSourceItem> items,
        DateTimeOffset loadedUtc) =>
        repository.ImportWorkbook(
            new MissingBolWorkbookImport("Synthetic", items),
            "Order Details Missing BOL synthetic.xlsx",
            "C:\\Synthetic\\Order Details Missing BOL synthetic.xlsx",
            hash,
            loadedUtc.UtcDateTime,
            loadedUtc);

    private static MissingBolSourceItem Item(
        string order,
        string driverCode,
        string? sourceName = null,
        DateOnly? date = null) =>
        new(
            MissingBolText.NormalizeExact(order),
            order,
            $"TMEX-{order}",
            $"LOG-{order}",
            "Synthetic Customer",
            "611",
            date ?? new DateOnly(2026, 8, 30),
            "Portland, OR",
            "Spokane, WA",
            "LH",
            "TEST",
            "LEAD000001",
            "Active",
            driverCode,
            MissingBolText.NormalizeExact(driverCode),
            sourceName ?? "Synthetic Driver",
            123m,
            456m,
            2);
}