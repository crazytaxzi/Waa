using Waa.App.Data;
using Waa.App.Services;
using Xunit;

namespace Waa.App.Tests;

public sealed class HandoffServiceTests
{
    private static readonly TimeZoneInfo TestTimeZone = TimeZoneInfo.CreateCustomTimeZone(
        "Synthetic Shift Time",
        TimeSpan.FromHours(-7),
        "Synthetic Shift Time",
        "Synthetic Shift Time");

    private static readonly DateTimeOffset TestNow =
        new(2026, 8, 30, 12, 0, 0, TimeSpan.FromHours(-7));

    [Fact]
    public void Generate_PlacesUnresolvedAndCompletedEntriesInRequiredSections()
    {
        var day = LocalDayRange.Create(TestNow, TestTimeZone);
        var entries = new[]
        {
            WorkEntryTestData.Entry(
                1,
                "A00001",
                "Alex Example",
                WorkEntryStatus.FollowUp,
                day.StartUtc.AddDays(-2),
                text: "Needs a synthetic callback."),
            WorkEntryTestData.Entry(
                2,
                "B00002",
                "Blair Example",
                WorkEntryStatus.Waiting,
                day.StartUtc.AddDays(-1),
                text: "Waiting on a synthetic ETA."),
            WorkEntryTestData.Entry(
                3,
                "C00003",
                "Casey Example",
                WorkEntryStatus.Done,
                day.StartUtc.AddHours(1),
                day.StartUtc.AddHours(1),
                text: "Completed a synthetic check."),
            WorkEntryTestData.Entry(
                4,
                "D00004",
                "Drew Example",
                WorkEntryStatus.Waiting,
                day.StartUtc.AddDays(-3),
                day.StartUtc.AddHours(2),
                text: "Old synthetic wait resolved today.")
        };

        var result = new HandoffService().Generate(entries, day);

        Assert.Equal(1, result.NeedsFollowUpCount);
        Assert.Equal(1, result.WaitingCount);
        Assert.Equal(2, result.CompletedTodayCount);
        Assert.Contains("NEEDS FOLLOW-UP", result.Text, StringComparison.Ordinal);
        Assert.Contains("Needs a synthetic callback.", result.Text, StringComparison.Ordinal);
        Assert.Contains("WAITING / PENDING", result.Text, StringComparison.Ordinal);
        Assert.Contains("Waiting on a synthetic ETA.", result.Text, StringComparison.Ordinal);
        Assert.Contains("COMPLETED TODAY", result.Text, StringComparison.Ordinal);
        Assert.Contains("Completed a synthetic check.", result.Text, StringComparison.Ordinal);
        Assert.Contains("Old synthetic wait resolved today.", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_UsesLocalCalendarBoundaryAndExcludesOtherDayCompletion()
    {
        var day = LocalDayRange.Create(TestNow, TestTimeZone);
        var entries = new[]
        {
            WorkEntryTestData.Entry(
                1,
                "A00001",
                "Alex Example",
                WorkEntryStatus.Done,
                day.StartUtc.AddTicks(-1),
                day.StartUtc.AddTicks(-1),
                text: "Completed before local midnight."),
            WorkEntryTestData.Entry(
                2,
                "B00002",
                "Blair Example",
                WorkEntryStatus.Done,
                day.StartUtc,
                day.StartUtc,
                text: "Completed at local midnight."),
            WorkEntryTestData.Entry(
                3,
                "C00003",
                "Casey Example",
                WorkEntryStatus.Waiting,
                day.StartUtc.AddDays(-5),
                day.EndUtc,
                text: "Resolved at next local midnight.")
        };

        var result = new HandoffService().Generate(entries, day);

        Assert.Equal(1, result.CompletedTodayCount);
        Assert.DoesNotContain("Completed before local midnight.", result.Text, StringComparison.Ordinal);
        Assert.Contains("Completed at local midnight.", result.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Resolved at next local midnight.", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_DeduplicatesLinkedIdleWorkAndUsesSnapshotContext()
    {
        var day = LocalDayRange.Create(TestNow, TestTimeZone);
        var linked = WorkEntryTestData.Entry(
            11,
            "A00001",
            "Alex Example",
            WorkEntryStatus.FollowUp,
            day.StartUtc.AddHours(3),
            source: WorkEntrySource.IdleContact,
            linkedIdleContactEventId: 44,
            unitCode: "279911",
            text: "Attempted idle contact — driver not reached — 28D 58.4%, 7D 61.2%.");

        var result = new HandoffService().Generate(new[] { linked, linked }, day);

        Assert.Equal(1, result.NeedsFollowUpCount);
        Assert.Equal(
            1,
            CountOccurrences(result.Text, "Attempted idle contact — driver not reached"));
        Assert.Contains(
            "279911 — Alex Example [A00001]: Attempted idle contact",
            result.Text,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_GroupsUnresolvedPredictablyAndKeepsOldestDriverGroupFirst()
    {
        var day = LocalDayRange.Create(TestNow, TestTimeZone);
        var entries = new[]
        {
            WorkEntryTestData.Entry(
                1,
                "B00002",
                "Blair Example",
                WorkEntryStatus.FollowUp,
                day.StartUtc.AddDays(-5),
                text: "Blair first."),
            WorkEntryTestData.Entry(
                2,
                "A00001",
                "Alex Example",
                WorkEntryStatus.FollowUp,
                day.StartUtc.AddDays(-4),
                text: "Alex only."),
            WorkEntryTestData.Entry(
                3,
                "B00002",
                "Blair Example",
                WorkEntryStatus.FollowUp,
                day.StartUtc.AddDays(-1),
                text: "Blair second.")
        };

        var result = new HandoffService().Generate(entries, day);

        var firstBlair = result.Text.IndexOf("Blair first.", StringComparison.Ordinal);
        var secondBlair = result.Text.IndexOf("Blair second.", StringComparison.Ordinal);
        var alex = result.Text.IndexOf("Alex only.", StringComparison.Ordinal);
        Assert.True(firstBlair >= 0);
        Assert.True(secondBlair > firstBlair);
        Assert.True(alex > secondBlair);
    }

    [Fact]
    public void Generate_OrdersCompletedSectionChronologically()
    {
        var day = LocalDayRange.Create(TestNow, TestTimeZone);
        var entries = new[]
        {
            WorkEntryTestData.Entry(
                1,
                "A00001",
                "Alex Example",
                WorkEntryStatus.Done,
                day.StartUtc.AddHours(5),
                day.StartUtc.AddHours(5),
                text: "Completed later."),
            WorkEntryTestData.Entry(
                2,
                "B00002",
                "Blair Example",
                WorkEntryStatus.Waiting,
                day.StartUtc.AddDays(-2),
                day.StartUtc.AddHours(2),
                text: "Resolved earlier.")
        };

        var result = new HandoffService().Generate(entries, day);

        Assert.True(
            result.Text.IndexOf("Resolved earlier.", StringComparison.Ordinal) <
            result.Text.IndexOf("Completed later.", StringComparison.Ordinal));
    }

    [Fact]
    public void Generate_UsesDriverIdentityWithoutInventingMissingUnit()
    {
        var day = LocalDayRange.Create(TestNow, TestTimeZone);
        var entry = WorkEntryTestData.Entry(
            1,
            "A00001",
            "Alex Example",
            WorkEntryStatus.Waiting,
            day.StartUtc.AddHours(1),
            unitCode: string.Empty,
            text: "Synthetic unit unavailable.");

        var result = new HandoffService().Generate(new[] { entry }, day);

        Assert.Contains(
            "Alex Example [A00001]: Synthetic unit unavailable.",
            result.Text,
            StringComparison.Ordinal);
        Assert.DoesNotContain(" — Alex Example", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_AlwaysIncludesAllHeadingsAndExplicitNoneForEmptySections()
    {
        var day = LocalDayRange.Create(TestNow, TestTimeZone);

        var result = new HandoffService().Generate(Array.Empty<WorkEntryRecord>(), day);

        Assert.Contains("NEEDS FOLLOW-UP", result.Text, StringComparison.Ordinal);
        Assert.Contains("WAITING / PENDING", result.Text, StringComparison.Ordinal);
        Assert.Contains("COMPLETED TODAY", result.Text, StringComparison.Ordinal);
        Assert.Equal(3, CountOccurrences(result.Text, "None."));
        Assert.Equal(0, result.NeedsFollowUpCount);
        Assert.Equal(0, result.WaitingCount);
        Assert.Equal(0, result.CompletedTodayCount);
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
