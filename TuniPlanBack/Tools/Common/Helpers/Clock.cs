namespace Common.Helpers;

/// <summary>Abstraction over the current time so business rules can be unit tested.</summary>
public interface IClock
{
    DateTime UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}

public static class TimeZoneHelper
{
    public static TimeZoneInfo Find(string? id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(string.IsNullOrWhiteSpace(id) ? TunisiaReference.DefaultTimeZone : id); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.CreateCustomTimeZone("TN", TimeSpan.FromHours(1), "Tunisia", "Tunisia"); }
    }

    public static DateTime ToUtc(DateOnly date, TimeOnly time, TimeZoneInfo tz) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(date.ToDateTime(time), DateTimeKind.Unspecified), tz);

    public static DateTime ToLocal(DateTime utc, TimeZoneInfo tz) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), tz);
}
