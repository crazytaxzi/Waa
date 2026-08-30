namespace Waa.App.Data;

public enum IdleContactOutcome
{
    Attempted,
    Spoke,
    SpokeFollowUp
}

public enum WorkEntryStatus
{
    Done,
    Waiting,
    FollowUp
}

public enum WorkEntrySource
{
    Manual,
    IdleContact,
    MissingBolTask,
    MissingBolAction
}

public enum MissingBolStatus
{
    Open,
    Requested,
    Attempted,
    FollowUp,
    Resolved
}

public enum MissingBolActionOutcome
{
    Requested,
    Attempted,
    FollowUp,
    Resolved,
    Reopen
}

public sealed record FleetDriverRecord(
    string DriverCode,
    string DriverName,
    string RawLabel,
    DateOnly ReportCycleDate,
    string UnitCode,
    string DriverLeader,
    decimal EngineHours7Day,
    decimal IdleHours7Day,
    decimal? IdlePercent7Day,
    decimal EngineHours28Day,
    decimal IdleHours28Day,
    decimal? IdlePercent28Day,
    int Coverage28Day,
    bool IsComplete28Day,
    long SourceImportId,
    IdleContactOutcome? LatestOutcome,
    string LatestNote,
    DateTimeOffset? LatestContactUtc,
    int OpenWorkCount);

public sealed record WorkEntryRecord(
    long Id,
    string DriverCode,
    string DriverName,
    string Text,
    WorkEntryStatus Status,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? ResolvedUtc,
    WorkEntrySource Source,
    long? LinkedIdleContactEventId,
    DateOnly? ReportCycleDateSnapshot,
    string UnitCodeSnapshot,
    string DriverLeaderSnapshot)
{
    public bool IsResolved => ResolvedUtc is not null;
}

public sealed record DriverWorkState(
    IReadOnlyList<WorkEntryRecord> OpenEntries,
    IReadOnlyList<WorkEntryRecord> TodayEntries);

public sealed record FleetState(
    DateOnly? ReportCycleDate,
    IReadOnlyList<FleetDriverRecord> Drivers,
    decimal? FleetIdlePercent7Day,
    int IncludedDrivers7Day,
    decimal? FleetIdlePercent28Day,
    int IncludedDrivers28Day,
    string LastImportFile,
    DateTimeOffset? LastImportedUtc);

public sealed record ReportImportResult(bool Imported, bool AlreadyAccepted, long? ImportId);

public sealed record MissingBolImportResult(
    bool Imported,
    bool AlreadyAccepted,
    long? ImportId,
    int ItemCount,
    int CreatedTaskCount);

public sealed record MissingBolItemRecord(
    long Id,
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
    string? MatchedDriverCode,
    string MatchedDriverName,
    MissingBolStatus CurrentStatus,
    DateTimeOffset FirstSeenUtc,
    DateTimeOffset LastSeenUtc,
    bool IsPresentInLatestImport,
    DateTimeOffset? ResolvedUtc,
    long? TaskWorkEntryId,
    long LastSeenImportId,
    bool ReturnedAfterResolution,
    bool SourceNameDiffersFromDriver)
{
    public bool IsResolved => CurrentStatus == MissingBolStatus.Resolved;
}

public sealed record MissingBolUnmatchedRecord(
    long Id,
    string SourceOrderNumber,
    DateOnly EmptyCallDate,
    string SourceDriverCode,
    string SourceDriverName,
    string OriginCityState,
    string DestinationCityState,
    bool IsPresentInLatestImport);

public sealed record MissingBolDriverSummary(
    int OpenCount,
    DateOnly? OldestOpenEmptyCallDate,
    string OrderSearchText);

public sealed record MissingBolFleetState(
    IReadOnlyDictionary<string, MissingBolDriverSummary> DriverSummaries,
    int OpenMatchedCount,
    IReadOnlyList<MissingBolUnmatchedRecord> UnmatchedItems);

public sealed record MissingBolActionRecord(
    long Id,
    long MissingBolItemId,
    MissingBolActionOutcome Outcome,
    string Note,
    DateTimeOffset CreatedUtc,
    long LinkedWorkEntryId,
    string? DriverCodeSnapshot,
    string UnitCodeSnapshot,
    string DriverLeaderSnapshot,
    long SourceImportId);
