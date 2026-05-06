namespace FlexCms.Framework.Clock;

public sealed class FcmsClock : IFcmsClock
{
    /// <summary>Default instance uses the system local timezone. Replaced by DI after startup.</summary>
    public static readonly FcmsClock Instance = new(TimeZoneInfo.Local);

    private readonly TimeZoneInfo _timeZone;

    public FcmsClock(TimeZoneInfo timeZone)
    {
        _timeZone = timeZone;
    }

    public DateTime Now => DateTime.UtcNow;

    public DateTime LocalNow => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _timeZone);

    public DateOnly Today => DateOnly.FromDateTime(LocalNow);

    public TimeOnly TimeOfDay => TimeOnly.FromDateTime(LocalNow);

    public TimeZoneInfo TimeZone => _timeZone;

    public DateTime ToLocal(DateTime utc)
    {
        // Treat unspecified Kind as UTC (our convention — entities store UTC).
        var asUtc = utc.Kind switch
        {
            DateTimeKind.Utc => utc,
            DateTimeKind.Local => utc.ToUniversalTime(),
            _ => DateTime.SpecifyKind(utc, DateTimeKind.Utc)
        };
        return TimeZoneInfo.ConvertTimeFromUtc(asUtc, _timeZone);
    }
}
