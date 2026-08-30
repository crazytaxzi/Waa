using System.Text;
using Waa.App.Data;

namespace Waa.App.Services;

public sealed record HandoffResult(
    string Text,
    int NeedsFollowUpCount,
    int WaitingCount,
    int CompletedTodayCount);

public sealed class HandoffService
{
    public HandoffResult Generate(
        IEnumerable<WorkEntryRecord> workEntries,
        LocalDayRange localDay)
    {
        ArgumentNullException.ThrowIfNull(workEntries);
        ArgumentNullException.ThrowIfNull(localDay);

        var uniqueEntries = workEntries
            .GroupBy(entry => entry.Id)
            .Select(group => group.First())
            .ToArray();

        var needsFollowUp = OrderUnresolved(uniqueEntries.Where(entry =>
                entry.ResolvedUtc is null &&
                entry.Status == WorkEntryStatus.FollowUp))
            .ToArray();
        var waiting = OrderUnresolved(uniqueEntries.Where(entry =>
                entry.ResolvedUtc is null &&
                entry.Status == WorkEntryStatus.Waiting))
            .ToArray();
        var completedToday = uniqueEntries
            .Where(entry =>
                (entry.Status == WorkEntryStatus.Done && localDay.Contains(entry.CreatedUtc)) ||
                (entry.ResolvedUtc is { } resolvedUtc && localDay.Contains(resolvedUtc)))
            .OrderBy(GetCompletionTimestamp)
            .ThenBy(entry => entry.DriverName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.DriverCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Id)
            .ToArray();

        var builder = new StringBuilder();
        AppendSection(builder, "NEEDS FOLLOW-UP", needsFollowUp);
        builder.AppendLine();
        AppendSection(builder, "WAITING / PENDING", waiting);
        builder.AppendLine();
        AppendSection(builder, "COMPLETED TODAY", completedToday);

        return new HandoffResult(
            builder.ToString().TrimEnd(),
            needsFollowUp.Length,
            waiting.Length,
            completedToday.Length);
    }

    private static IEnumerable<WorkEntryRecord> OrderUnresolved(
        IEnumerable<WorkEntryRecord> entries) =>
        entries
            .GroupBy(entry => entry.DriverCode, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                DriverName = group.First().DriverName,
                DriverCode = group.Key,
                Earliest = group.Min(entry => entry.CreatedUtc),
                Entries = group
                    .OrderBy(entry => entry.CreatedUtc)
                    .ThenBy(entry => entry.Id)
                    .ToArray()
            })
            .OrderBy(group => group.Earliest)
            .ThenBy(group => group.DriverName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.DriverCode, StringComparer.OrdinalIgnoreCase)
            .SelectMany(group => group.Entries);

    private static DateTimeOffset GetCompletionTimestamp(WorkEntryRecord entry) =>
        entry.Status == WorkEntryStatus.Done
            ? entry.CreatedUtc
            : entry.ResolvedUtc ?? entry.CreatedUtc;

    private static void AppendSection(
        StringBuilder builder,
        string heading,
        IReadOnlyCollection<WorkEntryRecord> entries)
    {
        builder.AppendLine(heading);
        builder.AppendLine();

        if (entries.Count == 0)
        {
            builder.AppendLine("None.");
            return;
        }

        foreach (var entry in entries)
        {
            builder.AppendLine(FormatLine(entry));
        }
    }

    private static string FormatLine(WorkEntryRecord entry)
    {
        var identity = string.IsNullOrWhiteSpace(entry.UnitCodeSnapshot)
            ? $"{entry.DriverName} [{entry.DriverCode}]"
            : $"{entry.UnitCodeSnapshot} — {entry.DriverName} [{entry.DriverCode}]";
        return $"{identity}: {CollapseWhitespace(entry.Text)}";
    }

    private static string CollapseWhitespace(string value) =>
        string.Join(
            " ",
            value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
