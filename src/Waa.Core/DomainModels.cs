namespace Waa.Core;

public sealed record DriverIdentity(
    string DriverCode,
    string DriverName,
    string RawLabel);

public sealed record WeeklyDriverObservation(
    DriverIdentity Driver,
    DateOnly WeekDate,
    decimal EngineHours,
    decimal IdleHours,
    string UnitCode,
    string DriverLeader,
    string DriverTerminal,
    string FleetLeader,
    string CostCenter,
    string OpsLob);

public sealed record DriverIdleSnapshot(
    DriverIdentity Driver,
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
    bool IsComplete28Day)
{
    public decimal? HighestValidIdlePercent =>
        IdlePercent28Day is null ? IdlePercent7Day :
        IdlePercent7Day is null ? IdlePercent28Day :
        Math.Max(IdlePercent28Day.Value, IdlePercent7Day.Value);

    public bool IsAboveThreshold(decimal threshold) =>
        (IdlePercent7Day is not null && IdlePercent7Day.Value > threshold) ||
        (IsComplete28Day && IdlePercent28Day is not null && IdlePercent28Day.Value > threshold);
}

public sealed record FleetIdleSnapshot(
    DateOnly ReportCycleDate,
    decimal? IdlePercent7Day,
    int IncludedDrivers7Day,
    int CurrentRosterDrivers,
    decimal? IdlePercent28Day,
    int IncludedDrivers28Day);

public sealed record RollingSevenDayImport(
    DateOnly ReportCycleDate,
    IReadOnlyList<WeeklyDriverObservation> Observations,
    IReadOnlyList<DriverIdleSnapshot> Drivers,
    FleetIdleSnapshot Fleet);

public sealed class ReportValidationException : Exception
{
    public ReportValidationException(string message) : base(message)
    {
    }
}
