namespace Waa.App.Services;

public sealed record LocalDayRange(
    DateOnly LocalDate,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc)
{
    public static LocalDayRange Create(DateTimeOffset currentTime, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        var localTime = TimeZoneInfo.ConvertTime(currentTime, timeZone);
        var localDate = DateOnly.FromDateTime(localTime.DateTime);
        var localStart = DateTime.SpecifyKind(
            localDate.ToDateTime(TimeOnly.MinValue),
            DateTimeKind.Unspecified);
        var localEnd = DateTime.SpecifyKind(
            localDate.AddDays(1).ToDateTime(TimeOnly.MinValue),
            DateTimeKind.Unspecified);

        return new LocalDayRange(
            localDate,
            new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localStart, timeZone)),
            new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localEnd, timeZone)));
    }

    public bool Contains(DateTimeOffset timestamp) =>
        timestamp >= StartUtc && timestamp < EndUtc;
}
