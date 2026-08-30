namespace Waa.App.ViewModels;

public static class DriverQueueOrderer
{
    public static IReadOnlyList<DriverRowViewModel> Order(
        IEnumerable<DriverRowViewModel> drivers)
    {
        ArgumentNullException.ThrowIfNull(drivers);

        return drivers
            .OrderBy(driver => driver.PriorityBand)
            .ThenBy(driver => driver.PriorityWithinBand)
            .ThenByDescending(driver => driver.PriorityConcern ?? decimal.MinValue)
            .ThenBy(driver => driver.OldestMissingBolDate ?? DateOnly.MaxValue)
            .ThenBy(driver => driver.DriverName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(driver => driver.DriverCode, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
