using Waa.App.Data;
using Waa.App.Services;
using Xunit;

namespace Waa.App.Tests;

public sealed class HandoffLeaderGroupingTests
{
    private static readonly TimeZoneInfo TestTimeZone = TimeZoneInfo.CreateCustomTimeZone(
        "Synthetic Leader Shift Time",
        TimeSpan.FromHours(-7),
        "Synthetic Leader Shift Time",
        "Synthetic Leader Shift Time");

    private static readonly DateTimeOffset TestNow =
        new(2026, 8, 30, 12, 0, 0, TimeSpan.FromHours(-7));

    [Fact]
    public void Generate_GroupsNarrativeDriversUnderCurrentDriverLeader()
    {
        var day = LocalDayRange.Create(TestNow, TestTimeZone);
        var entries = new[]
        {
            WorkEntryTestData.Entry(
                1,
                "B00002",
                "Blair Example",
                WorkEntryStatus.Waiting,
                day.StartUtc.AddHours(1),
                text: "Blair waiting item.") with { DriverLeaderSnapshot = "OLD-LEADER" },
            WorkEntryTestData.Entry(
                2,
                "A00001",
                "Alex Example",
                WorkEntryStatus.FollowUp,
                day.StartUtc.AddHours(2),
                text: "Alex follow-up item.") with { DriverLeaderSnapshot = "OLD-LEADER" },
            WorkEntryTestData.Entry(
                3,
                "C00003",
                "Casey Example",
                WorkEntryStatus.Waiting,
                day.StartUtc.AddHours(3),
                text: "Casey waiting item.") with { DriverLeaderSnapshot = "OLD-LEADER" }
        };
        var drivers = new[]
        {
            WorkEntryTestData.FleetDriver("A00001", "Alex Example", 40m, null, 1) with
            {
                UnitCode = "270101",
                DriverLeader = "LEADER-B"
            },
            WorkEntryTestData.FleetDriver("B00002", "Blair Example", 40m, null, 1) with
            {
                UnitCode = "270202",
                DriverLeader = "LEADER-A"
            },
            WorkEntryTestData.FleetDriver("C00003", "Casey Example", 40m, null, 1) with
            {
                UnitCode = "270303",
                DriverLeader = "LEADER-A"
            }
        };

        var result = new HandoffService().Generate(entries, drivers, day);
        var text = NormalizeNewlines(result.Text);

        var leaderA = text.IndexOf("Driver Leader: LEADER-A", StringComparison.Ordinal);
        var blair = text.IndexOf("270202 — Blair Example [B00002]", StringComparison.Ordinal);
        var casey = text.IndexOf("270303 — Casey Example [C00003]", StringComparison.Ordinal);
        var leaderB = text.IndexOf("Driver Leader: LEADER-B", StringComparison.Ordinal);
        var alex = text.IndexOf("270101 — Alex Example [A00001]", StringComparison.Ordinal);

        Assert.True(leaderA >= 0);
        Assert.True(blair > leaderA);
        Assert.True(casey > blair);
        Assert.True(leaderB > casey);
        Assert.True(alex > leaderB);
        Assert.DoesNotContain("Driver Leader: OLD-LEADER", text, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(text, "Driver Leader: "));
    }

    [Fact]
    public void Generate_GroupsMissingBolDriversByLeaderAndUsesHistoricalFallback()
    {
        var day = LocalDayRange.Create(TestNow, TestTimeZone);
        var taskA = WorkEntryTestData.Entry(
            10,
            "A00001",
            "Alex Example",
            WorkEntryStatus.FollowUp,
            day.StartUtc.AddHours(1),
            source: WorkEntrySource.MissingBolTask,
            unitCode: "270101",
            text: "Missing BOL for order SYN1001, empty call 8/28/2026, Boise, ID → Auburn, WA. Status: Open.") with
        {
            DriverLeaderSnapshot = "LEADER-HIST"
        };
        var taskB = WorkEntryTestData.Entry(
            11,
            "B00002",
            "Blair Example",
            WorkEntryStatus.FollowUp,
            day.StartUtc.AddHours(2),
            source: WorkEntrySource.MissingBolTask,
            unitCode: "270202",
            text: "Missing BOL for order SYN1002, empty call 8/29/2026, Boise, ID → Auburn, WA. Status: Open.") with
        {
            DriverLeaderSnapshot = "OLD-B"
        };
        var currentB = WorkEntryTestData.FleetDriver("B00002", "Blair Example", 40m, null, 1) with
        {
            UnitCode = "270202",
            DriverLeader = "LEADER-CURRENT"
        };

        var result = new HandoffService().Generate(
            new[] { taskA, taskB },
            new[] { currentB },
            day);
        var text = NormalizeNewlines(result.Text);

        Assert.Contains("Missing BOLs:\nDriver Leader: LEADER-CURRENT", text, StringComparison.Ordinal);
        Assert.Contains("270202 — Blair Example [B00002]: Missing BOL for order SYN1002", text, StringComparison.Ordinal);
        Assert.Contains("Driver Leader: LEADER-HIST", text, StringComparison.Ordinal);
        Assert.Contains("270101 — Alex Example [A00001]: Missing BOL for order SYN1001", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Driver Leader: OLD-B", text, StringComparison.Ordinal);
    }

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
