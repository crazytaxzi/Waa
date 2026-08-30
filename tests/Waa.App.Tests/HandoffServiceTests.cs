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
    public void Generate_UsesCompactDriverGroupedLayout()
    {
        var day = LocalDayRange.Create(TestNow, TestTimeZone);
        var entries = new[]
        {
            WorkEntryTestData.Entry(
                1,
                "A00001",
                "Alex Example",
                WorkEntryStatus.Done,
                day.StartUtc.AddHours(1),
                day.StartUtc.AddHours(1),
                WorkEntrySource.MissingBolAction,
                unitCode: "OLD-A",
                text: "Resolved missing BOL for order SYN1001. Note: Driver will upload it tonight."),
            WorkEntryTestData.Entry(
                2,
                "A00001",
                "Alex Example",
                WorkEntryStatus.Done,
                day.StartUtc.AddHours(2),
                day.StartUtc.AddHours(2),
                WorkEntrySource.IdleContact,
                linkedIdleContactEventId: 44,
                unitCode: "OLD-A",
                text: "Spoke with driver regarding idle — 28D 46.3%, 7D 59.1%. Note: Discussed the rolling 7 day idle and parking in shade."),
            WorkEntryTestData.Entry(
                3,
                "B00002",
                "Blair Example",
                WorkEntryStatus.Waiting,
                day.StartUtc.AddDays(-1),
                unitCode: "OLD-B",
                text: "Waiting on updated ETA")
        };
        var drivers = new[]
        {
            WorkEntryTestData.FleetDriver("A00001", "Alex Example", 46m, IdleContactOutcome.Spoke, 0) with { UnitCode = "261535" },
            WorkEntryTestData.FleetDriver("B00002", "Blair Example", 35m, null, 1) with { UnitCode = "240307" }
        };

        var result = new HandoffService().Generate(entries, drivers, day);

        Assert.StartsWith("No open ACE/ACI's\n\n", NormalizeNewlines(result.Text), StringComparison.Ordinal);
        Assert.Contains(
            "261535 — Alex Example [A00001]: Driver will upload it tonight. Spoke with driver regarding high idle. Discussed the rolling 7 day idle and parking in shade.",
            result.Text,
            StringComparison.Ordinal);
        Assert.Contains(
            "240307 — Blair Example [B00002]: Waiting on updated ETA.",
            result.Text,
            StringComparison.Ordinal);
        Assert.DoesNotContain("28D 46.3%", result.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Note:", result.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("NEEDS FOLLOW-UP", result.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("WAITING / PENDING", result.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("COMPLETED TODAY", result.Text, StringComparison.Ordinal);
        Assert.Equal(2, result.DriverLineCount);
        Assert.Equal(0, result.MissingBolDriverCount);
        Assert.Equal(0, result.MissingBolOrderCount);
    }

    [Fact]
    public void Generate_GroupsOpenMissingBolOrdersOncePerDriver()
    {
        var day = LocalDayRange.Create(TestNow, TestTimeZone);
        var entries = new[]
        {
            MissingBolTask(1, "A00001", "Alex Example", "SYN-LATE", "8/29/2026", day.StartUtc.AddHours(1)),
            MissingBolTask(2, "A00001", "Alex Example", "SYN-EARLY", "8/27/2026", day.StartUtc.AddHours(2)),
            MissingBolTask(3, "B00002", "Blair Example", "SYN-ONLY", "8/28/2026", day.StartUtc.AddHours(3))
        };
        var drivers = new[]
        {
            WorkEntryTestData.FleetDriver("A00001", "Alex Example", 40m, null, 2) with { UnitCode = "242163" },
            WorkEntryTestData.FleetDriver("B00002", "Blair Example", 40m, null, 1) with { UnitCode = "260811" }
        };

        var result = new HandoffService().Generate(entries, drivers, day);

        Assert.Contains("Missing BOLs:", result.Text, StringComparison.Ordinal);
        Assert.Contains(
            "242163 — Alex Example [A00001]: Missing BOL for orders SYN-EARLY, SYN-LATE",
            result.Text,
            StringComparison.Ordinal);
        Assert.Contains(
            "260811 — Blair Example [B00002]: Missing BOL for order SYN-ONLY",
            result.Text,
            StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(result.Text, "Alex Example [A00001]"));
        Assert.Equal(1, CountOccurrences(result.Text, "Blair Example [B00002]"));
        Assert.DoesNotContain("empty call", result.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Boise, ID", result.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Status:", result.Text, StringComparison.Ordinal);
        Assert.Equal(2, result.MissingBolDriverCount);
        Assert.Equal(3, result.MissingBolOrderCount);
    }

    [Fact]
    public void Generate_UsesCurrentFleetUnitInsteadOfStaleTaskSnapshot()
    {
        var day = LocalDayRange.Create(TestNow, TestTimeZone);
        var task = MissingBolTask(
            1,
            "A00001",
            "Alex Example",
            "SYN1001",
            "8/29/2026",
            day.StartUtc.AddHours(1),
            unitCode: "*");
        var driver = WorkEntryTestData.FleetDriver("A00001", "Alex Example", 40m, null, 1) with
        {
            UnitCode = "260811"
        };

        var result = new HandoffService().Generate(new[] { task }, new[] { driver }, day);

        Assert.Contains(
            "260811 — Alex Example [A00001]: Missing BOL for order SYN1001",
            result.Text,
            StringComparison.Ordinal);
        Assert.DoesNotContain("* —", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_OrdersDriverLinesAlphabetically()
    {
        var day = LocalDayRange.Create(TestNow, TestTimeZone);
        var entries = new[]
        {
            WorkEntryTestData.Entry(
                1,
                "W00003",
                "William Example",
                WorkEntryStatus.Waiting,
                day.StartUtc.AddDays(-1),
                text: "William note."),
            WorkEntryTestData.Entry(
                2,
                "A00001",
                "Andrew Example",
                WorkEntryStatus.Waiting,
                day.StartUtc.AddDays(-1),
                text: "Andrew note."),
            WorkEntryTestData.Entry(
                3,
                "C00002",
                "Clarence Example",
                WorkEntryStatus.Waiting,
                day.StartUtc.AddDays(-1),
                text: "Clarence note.")
        };

        var result = new HandoffService().Generate(entries, Array.Empty<FleetDriverRecord>(), day);

        var andrew = result.Text.IndexOf("Andrew Example", StringComparison.Ordinal);
        var clarence = result.Text.IndexOf("Clarence Example", StringComparison.Ordinal);
        var william = result.Text.IndexOf("William Example", StringComparison.Ordinal);
        Assert.True(andrew >= 0);
        Assert.True(clarence > andrew);
        Assert.True(william > clarence);
    }

    [Fact]
    public void Generate_UsesLocalCalendarBoundaryForCompletedNarrative()
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

        var result = new HandoffService().Generate(entries, Array.Empty<FleetDriverRecord>(), day);

        Assert.Equal(1, result.CompletedTodayCount);
        Assert.DoesNotContain("Completed before local midnight.", result.Text, StringComparison.Ordinal);
        Assert.Contains("Completed at local midnight.", result.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Resolved at next local midnight.", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_DeduplicatesWorkEntryIds()
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
            text: "Attempted idle contact — driver not reached — 28D 58.4%, 7D 61.2%. Note: Try again tomorrow.");

        var result = new HandoffService().Generate(
            new[] { linked, linked },
            Array.Empty<FleetDriverRecord>(),
            day);

        Assert.Equal(1, result.NeedsFollowUpCount);
        Assert.Equal(1, CountOccurrences(result.Text, "Try again tomorrow."));
        Assert.Contains(
            "279911 — Alex Example [A00001]: Attempted contact with driver regarding high idle; driver not reached. Try again tomorrow.",
            result.Text,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_OmitsInventedUnitWhenNoUsefulUnitExists()
    {
        var day = LocalDayRange.Create(TestNow, TestTimeZone);
        var entry = WorkEntryTestData.Entry(
            1,
            "A00001",
            "Alex Example",
            WorkEntryStatus.Waiting,
            day.StartUtc.AddHours(1),
            unitCode: "*",
            text: "Synthetic unit unavailable.");

        var result = new HandoffService().Generate(
            new[] { entry },
            Array.Empty<FleetDriverRecord>(),
            day);

        Assert.Contains(
            "Alex Example [A00001]: Synthetic unit unavailable.",
            result.Text,
            StringComparison.Ordinal);
        Assert.DoesNotContain("* —", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_EmptyStateKeepsEditableAceAciOpeningAndMissingBolSection()
    {
        var day = LocalDayRange.Create(TestNow, TestTimeZone);

        var result = new HandoffService().Generate(
            Array.Empty<WorkEntryRecord>(),
            Array.Empty<FleetDriverRecord>(),
            day);

        Assert.Equal(
            "No open ACE/ACI's\n\nMissing BOLs:\nNone.",
            NormalizeNewlines(result.Text));
        Assert.Equal(0, result.NeedsFollowUpCount);
        Assert.Equal(0, result.WaitingCount);
        Assert.Equal(0, result.CompletedTodayCount);
        Assert.Equal(0, result.DriverLineCount);
        Assert.Equal(0, result.MissingBolDriverCount);
        Assert.Equal(0, result.MissingBolOrderCount);
    }

    private static WorkEntryRecord MissingBolTask(
        long id,
        string driverCode,
        string driverName,
        string orderNumber,
        string emptyCallDate,
        DateTimeOffset createdUtc,
        string unitCode = "270101") =>
        WorkEntryTestData.Entry(
            id,
            driverCode,
            driverName,
            WorkEntryStatus.FollowUp,
            createdUtc,
            source: WorkEntrySource.MissingBolTask,
            unitCode: unitCode,
            text: $"Missing BOL for order {orderNumber}, empty call {emptyCallDate}, Boise, ID → Auburn, WA. Status: Open.");

    private static string NormalizeNewlines(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal);

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
