namespace FlexCms.Framework.Clock;

/// <summary>
/// Static access point for the current time.
/// Use <see cref="Now"/> for DB writes (UTC).
/// Use <see cref="LocalNow"/> for display (configured site timezone).
/// Swap <see cref="Clock"/> in tests to control time.
/// </summary>
public static class FcmsTime
{
    private static IFcmsClock _clock = FcmsClock.Instance;

    public static IFcmsClock Clock
    {
        get => _clock;
        set => _clock = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>UTC — for DB writes, timestamps, auditing.</summary>
    public static DateTime Now => _clock.Now;

    /// <summary>Site-configured local time — for display, UI, reports.</summary>
    public static DateTime LocalNow => _clock.LocalNow;

    public static DateOnly Today => _clock.Today;

    public static TimeOnly TimeOfDay => _clock.TimeOfDay;
}
