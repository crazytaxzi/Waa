namespace Waa.Core;

public static class IdleCalculator
{
    public static DriverIdleSnapshot CalculateDriver(
        string driverCode,
        DateOnly reportCycleDate,
        IReadOnlyCollection<WeeklyDriverObservation> observations)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(driverCode);
        ArgumentNullException.ThrowIfNull(observations);

        var driverObservations = observations
            .Where(observation => string.Equals(
                observation.Driver.DriverCode,
                driverCode,
                StringComparison.OrdinalIgnoreCase))
            .ToDictionary(observation => observation.WeekDate);

        if (!driverObservations.TryGetValue(reportCycleDate, out var current))
        {
            throw new ReportValidationException(
                $"Driver '{driverCode}' has no observation for current report cycle {reportCycleDate:yyyy-MM-dd}.");
        }

        var idlePercent7Day = Percentage(current.IdleHours, current.EngineHours);
        var expectedDates = Enumerable.Range(0, 4)
            .Select(offset => reportCycleDate.AddDays(offset * -7))
            .ToArray();

        var covered = expectedDates
            .Where(driverObservations.ContainsKey)
            .Select(date => driverObservations[date])
            .ToArray();

        var engineHours28Day = covered.Sum(observation => observation.EngineHours);
        var idleHours28Day = covered.Sum(observation => observation.IdleHours);
        var isComplete = covered.Length == expectedDates.Length;
        var idlePercent28Day = isComplete
            ? Percentage(idleHours28Day, engineHours28Day)
            : null;

        return new DriverIdleSnapshot(
            current.Driver,
            reportCycleDate,
            current.UnitCode,
            current.DriverLeader,
            current.EngineHours,
            current.IdleHours,
            idlePercent7Day,
            engineHours28Day,
            idleHours28Day,
            idlePercent28Day,
            covered.Length,
            isComplete);
    }

    public static FleetIdleSnapshot CalculateFleet(
        DateOnly reportCycleDate,
        IReadOnlyCollection<DriverIdleSnapshot> drivers)
    {
        ArgumentNullException.ThrowIfNull(drivers);

        var eligible7Day = drivers
            .Where(driver => driver.EngineHours7Day > 0)
            .ToArray();
        var engineHours7Day = eligible7Day.Sum(driver => driver.EngineHours7Day);
        var idleHours7Day = eligible7Day.Sum(driver => driver.IdleHours7Day);

        var eligible28Day = drivers
            .Where(driver => driver.IsComplete28Day && driver.EngineHours28Day > 0)
            .ToArray();
        var engineHours28Day = eligible28Day.Sum(driver => driver.EngineHours28Day);
        var idleHours28Day = eligible28Day.Sum(driver => driver.IdleHours28Day);

        return new FleetIdleSnapshot(
            reportCycleDate,
            Percentage(idleHours7Day, engineHours7Day),
            eligible7Day.Length,
            drivers.Count,
            Percentage(idleHours28Day, engineHours28Day),
            eligible28Day.Length);
    }

    private static decimal? Percentage(decimal numerator, decimal denominator) =>
        denominator <= 0 ? null : numerator / denominator * 100m;
}
