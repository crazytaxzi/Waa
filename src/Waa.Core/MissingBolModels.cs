namespace Waa.Core;

public sealed record MissingBolSourceItem(
    string NormalizedOrderNumber,
    string SourceOrderNumber,
    string TmexOrderNumber,
    string LogisticsOrderNumber,
    string BillTo,
    string DivisionCode,
    DateOnly EmptyCallDate,
    string OriginCityState,
    string DestinationCityState,
    string RevenueType,
    string Terminal,
    string SourceDriverLeader,
    string SourceDriverStatus,
    string SourceDriverCode,
    string NormalizedSourceDriverCode,
    string SourceDriverName,
    decimal? LoadedMiles,
    decimal? OrderLevelMiles,
    int SourceRowNumber);

public sealed record MissingBolWorkbookImport(
    string WorksheetName,
    IReadOnlyList<MissingBolSourceItem> Items);

public static class MissingBolText
{
    public static string NormalizeExact(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToUpperInvariant();
}
