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

    /// <summary>Convert a UTC <see cref="DateTime"/> to the configured site timezone.</summary>
    public static DateTime ToLocal(DateTime utc) => _clock.ToLocal(utc);

    /// <summary>
    /// Convert a UTC <see cref="DateTime"/> to the site timezone and format using
    /// the supplied pattern (defaults to <c>yyyy-MM-dd HH:mm</c>). Use this for
    /// any UI display of an entity timestamp — never <c>.ToString()</c> directly.
    /// </summary>
    public static string Format(DateTime utc, string? pattern = null)
        => _clock.ToLocal(utc).ToString(pattern ?? "yyyy-MM-dd HH:mm");

    /// <summary>Nullable variant — returns empty string when null.</summary>
    public static string Format(DateTime? utc, string? pattern = null)
        => utc.HasValue ? Format(utc.Value, pattern) : "";
}
