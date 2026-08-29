namespace Waa.App.Data;

public enum IdleContactOutcome
{
    Attempted,
    Spoke,
    SpokeFollowUp
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
    DateTimeOffset? LatestContactUtc);

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
