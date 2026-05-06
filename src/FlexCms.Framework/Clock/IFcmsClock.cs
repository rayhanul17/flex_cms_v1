namespace FlexCms.Framework.Clock;

public interface IFcmsClock
{
    /// <summary>UTC — always use this for DB writes.</summary>
    DateTime Now { get; }

    /// <summary>Configured site timezone — use this for display/UI.</summary>
    DateTime LocalNow { get; }

    /// <summary>Today's date in the configured site timezone.</summary>
    DateOnly Today { get; }

    /// <summary>Current time-of-day in the configured site timezone.</summary>
    TimeOnly TimeOfDay { get; }

    /// <summary>Configured site timezone (used for UI conversions).</summary>
    TimeZoneInfo TimeZone { get; }

    /// <summary>Convert a UTC <see cref="DateTime"/> to the site's local timezone.</summary>
    DateTime ToLocal(DateTime utc);
}
