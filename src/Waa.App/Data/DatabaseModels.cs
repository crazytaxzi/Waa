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
    IdleContact
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
