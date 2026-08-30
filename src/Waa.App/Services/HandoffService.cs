using System.Globalization;
using System.Text;
using Waa.App.Data;

namespace Waa.App.Services;

public sealed record HandoffResult(
    string Text,
    int NeedsFollowUpCount,
    int WaitingCount,
    int CompletedTodayCount,
    int DriverLineCount,
    int MissingBolDriverCount,
    int MissingBolOrderCount);

public sealed class HandoffService
{
    private const string DefaultAceAciLine = "No open ACE/ACI's";
    private const string MissingBolPrefix = "Missing BOL for order ";
    private const string EmptyCallMarker = ", empty call ";
    private const string NoteMarker = " Note: ";

    public HandoffResult Generate(
        IEnumerable<WorkEntryRecord> workEntries,
        IEnumerable<FleetDriverRecord> currentDrivers,
        LocalDayRange localDay)
    {
        ArgumentNullException.ThrowIfNull(workEntries);
        ArgumentNullException.ThrowIfNull(currentDrivers);
        ArgumentNullException.ThrowIfNull(localDay);

        var uniqueEntries = workEntries
            .GroupBy(entry => entry.Id)
            .Select(group => group.First())
            .ToArray();
        var currentByCode = currentDrivers
            .GroupBy(driver => driver.DriverCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.OrdinalIgnoreCase);

        var needsFollowUp = uniqueEntries
            .Where(entry =>
                entry.ResolvedUtc is null &&
                entry.Status == WorkEntryStatus.FollowUp)
            .ToArray();
        var waiting = uniqueEntries
            .Where(entry =>
                entry.ResolvedUtc is null &&
                entry.Status == WorkEntryStatus.Waiting)
            .ToArray();
        var completedToday = uniqueEntries
            .Where(entry =>
                entry.Source != WorkEntrySource.MissingBolTask &&
                ((entry.Status == WorkEntryStatus.Done && localDay.Contains(entry.CreatedUtc)) ||
                 (entry.ResolvedUtc is { } resolvedUtc && localDay.Contains(resolvedUtc))))
            .ToArray();

        var narrativeGroups = needsFollowUp
            .Concat(waiting)
            .Concat(completedToday)
            .Where(entry => entry.Source != WorkEntrySource.MissingBolTask)
            .GroupBy(entry => entry.Id)
            .Select(group => group.First())
            .GroupBy(entry => entry.DriverCode, StringComparer.OrdinalIgnoreCase)
            .Select(group => BuildNarrativeGroup(group, currentByCode))
            .Where(group => group.Phrases.Count > 0)
            .OrderBy(group => group.DriverName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.DriverCode, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var missingBolGroups = uniqueEntries
            .Where(entry =>
                entry.Source == WorkEntrySource.MissingBolTask &&
                entry.ResolvedUtc is null)
            .GroupBy(entry => entry.DriverCode, StringComparer.OrdinalIgnoreCase)
            .Select(group => BuildMissingBolGroup(group, currentByCode))
            .Where(group => group.Orders.Count > 0)
            .OrderBy(group => group.DriverName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.DriverCode, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var builder = new StringBuilder();
        // WAA does not model ACE/ACI state; this is a user-requested editable opening line.
        builder.AppendLine(DefaultAceAciLine);
        builder.AppendLine();

        foreach (var group in narrativeGroups)
        {
            builder.Append(FormatIdentity(group.UnitCode, group.DriverName, group.DriverCode));
            builder.Append(": ");
            builder.AppendLine(string.Join(" ", group.Phrases));
        }

        if (narrativeGroups.Length > 0)
        {
            builder.AppendLine();
        }

        builder.AppendLine("Missing BOLs:");
        if (missingBolGroups.Length == 0)
        {
            builder.AppendLine("None.");
        }
        else
        {
            foreach (var group in missingBolGroups)
            {
                builder.Append(FormatIdentity(group.UnitCode, group.DriverName, group.DriverCode));
                builder.Append(": Missing BOL for ");
                builder.Append(group.Orders.Count == 1 ? "order " : "orders ");
                builder.AppendLine(string.Join(", ", group.Orders.Select(order => order.OrderNumber)));
            }
        }

        return new HandoffResult(
            builder.ToString().TrimEnd(),
            needsFollowUp.Length,
            waiting.Length,
            completedToday.Length,
            narrativeGroups.Length,
            missingBolGroups.Length,
            missingBolGroups.Sum(group => group.Orders.Count));
    }

    private static NarrativeGroup BuildNarrativeGroup(
        IGrouping<string, WorkEntryRecord> group,
        IReadOnlyDictionary<string, FleetDriverRecord> currentByCode)
    {
        var ordered = group
            .OrderBy(GetNarrativeTimestamp)
            .ThenBy(entry => entry.Id)
            .ToArray();
        currentByCode.TryGetValue(group.Key, out var current);
        var driverName = current?.DriverName ?? ordered.Last().DriverName;
        var unitCode = ChooseUnitCode(current, ordered);
        var phrases = ordered
            .Select(FormatNarrativeText)
            .Where(text => text.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new NarrativeGroup(group.Key, driverName, unitCode, phrases);
    }

    private static MissingBolGroup BuildMissingBolGroup(
        IGrouping<string, WorkEntryRecord> group,
        IReadOnlyDictionary<string, FleetDriverRecord> currentByCode)
    {
        var entries = group
            .OrderBy(entry => entry.CreatedUtc)
            .ThenBy(entry => entry.Id)
            .ToArray();
        currentByCode.TryGetValue(group.Key, out var current);
        var driverName = current?.DriverName ?? entries.Last().DriverName;
        var unitCode = ChooseUnitCode(current, entries);
        var orders = entries
            .Select(entry => ParseMissingBolTask(entry.Text))
            .GroupBy(order => order.OrderNumber, StringComparer.OrdinalIgnoreCase)
            .Select(orderGroup => orderGroup
                .OrderBy(order => order.EmptyCallDate ?? DateOnly.MaxValue)
                .ThenBy(order => order.OrderNumber, StringComparer.OrdinalIgnoreCase)
                .First())
            .OrderBy(order => order.EmptyCallDate ?? DateOnly.MaxValue)
            .ThenBy(order => order.OrderNumber, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new MissingBolGroup(group.Key, driverName, unitCode, orders);
    }

    private static string FormatNarrativeText(WorkEntryRecord entry)
    {
        var text = CollapseWhitespace(entry.Text);
        return entry.Source switch
        {
            WorkEntrySource.IdleContact => FormatIdleNarrative(text),
            WorkEntrySource.MissingBolAction => FormatMissingBolActionNarrative(text),
            _ => EnsureSentence(text)
        };
    }

    private static string FormatIdleNarrative(string text)
    {
        var note = ExtractNote(text);
        string action;
        if (text.StartsWith(
                "Spoke with driver regarding idle; follow-up required",
                StringComparison.OrdinalIgnoreCase))
        {
            action = "Spoke with driver regarding high idle; follow-up required.";
        }
        else if (text.StartsWith(
                     "Spoke with driver regarding idle",
                     StringComparison.OrdinalIgnoreCase))
        {
            action = "Spoke with driver regarding high idle.";
        }
        else if (text.StartsWith(
                     "Attempted idle contact — driver not reached",
                     StringComparison.OrdinalIgnoreCase))
        {
            action = "Attempted contact with driver regarding high idle; driver not reached.";
        }
        else
        {
            return note.Length == 0 ? EnsureSentence(text) : EnsureSentence(note);
        }

        return note.Length == 0
            ? action
            : $"{action} {EnsureSentence(note)}";
    }

    private static string FormatMissingBolActionNarrative(string text)
    {
        var note = ExtractNote(text);
        if (note.Length > 0)
        {
            return EnsureSentence(note);
        }

        return EnsureSentence(text);
    }

    private static string ExtractNote(string text)
    {
        var index = text.IndexOf(NoteMarker, StringComparison.OrdinalIgnoreCase);
        return index < 0
            ? string.Empty
            : text[(index + NoteMarker.Length)..].Trim();
    }

    private static MissingBolOrder ParseMissingBolTask(string text)
    {
        var collapsed = CollapseWhitespace(text);
        var prefixIndex = collapsed.IndexOf(MissingBolPrefix, StringComparison.OrdinalIgnoreCase);
        if (prefixIndex < 0)
        {
            return new MissingBolOrder(collapsed, null);
        }

        var orderStart = prefixIndex + MissingBolPrefix.Length;
        var orderEnd = FindDelimiter(collapsed, orderStart);
        var orderNumber = collapsed[orderStart..orderEnd].Trim();
        if (orderNumber.Length == 0)
        {
            orderNumber = collapsed;
        }

        DateOnly? emptyCallDate = null;
        var dateMarkerIndex = collapsed.IndexOf(
            EmptyCallMarker,
            orderEnd,
            StringComparison.OrdinalIgnoreCase);
        if (dateMarkerIndex >= 0)
        {
            var dateStart = dateMarkerIndex + EmptyCallMarker.Length;
            var dateEnd = FindDelimiter(collapsed, dateStart);
            var dateText = collapsed[dateStart..dateEnd].Trim();
            if (DateOnly.TryParseExact(
                    dateText,
                    ["M/d/yyyy", "MM/dd/yyyy", "M/d/yy", "MM/dd/yy"],
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var parsed))
            {
                emptyCallDate = parsed;
            }
        }

        return new MissingBolOrder(orderNumber, emptyCallDate);
    }

    private static int FindDelimiter(string value, int startIndex)
    {
        var comma = value.IndexOf(',', startIndex);
        var period = value.IndexOf('.', startIndex);
        if (comma < 0)
        {
            return period < 0 ? value.Length : period;
        }

        if (period < 0)
        {
            return comma;
        }

        return Math.Min(comma, period);
    }

    private static string ChooseUnitCode(
        FleetDriverRecord? current,
        IEnumerable<WorkEntryRecord> entries)
    {
        if (IsMeaningfulUnit(current?.UnitCode))
        {
            return current!.UnitCode.Trim();
        }

        return entries
            .OrderByDescending(GetNarrativeTimestamp)
            .Select(entry => entry.UnitCodeSnapshot)
            .FirstOrDefault(IsMeaningfulUnit)?
            .Trim() ?? string.Empty;
    }

    private static bool IsMeaningfulUnit(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        !value.Trim().Equals("*", StringComparison.Ordinal);

    private static DateTimeOffset GetNarrativeTimestamp(WorkEntryRecord entry) =>
        entry.ResolvedUtc ?? entry.CreatedUtc;

    private static string FormatIdentity(string unitCode, string driverName, string driverCode) =>
        IsMeaningfulUnit(unitCode)
            ? $"{unitCode.Trim()} — {driverName} [{driverCode}]"
            : $"{driverName} [{driverCode}]";

    private static string EnsureSentence(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        return trimmed.EndsWith(".", StringComparison.Ordinal) ||
               trimmed.EndsWith("!", StringComparison.Ordinal) ||
               trimmed.EndsWith("?", StringComparison.Ordinal)
            ? trimmed
            : trimmed + ".";
    }

    private static string CollapseWhitespace(string value) =>
        string.Join(
            " ",
            value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private sealed record NarrativeGroup(
        string DriverCode,
        string DriverName,
        string UnitCode,
        IReadOnlyList<string> Phrases);

    private sealed record MissingBolGroup(
        string DriverCode,
        string DriverName,
        string UnitCode,
        IReadOnlyList<MissingBolOrder> Orders);

    private sealed record MissingBolOrder(
        string OrderNumber,
        DateOnly? EmptyCallDate);
}
