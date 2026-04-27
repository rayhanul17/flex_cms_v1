namespace FlexCms.Framework.Clock;

/// <summary>
/// Static access point for the current time. Swap <see cref="Clock"/> in tests to control time.
/// </summary>
public static class FcmsTime
{
    private static IFcmsClock _clock = FcmsClock.Instance;

    public static IFcmsClock Clock
    {
        get => _clock;
        set => _clock = value ?? throw new ArgumentNullException(nameof(value));
    }

    public static DateTime Now => _clock.Now;
    public static DateOnly Today => _clock.Today;
    public static TimeOnly TimeOfDay => _clock.TimeOfDay;
}
