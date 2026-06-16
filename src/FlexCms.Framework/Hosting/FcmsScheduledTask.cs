using System.Globalization;

namespace FlexCms.Framework.Hosting;

/// <summary>
/// Minimal cron-style schedule evaluator for module <c>BackgroundService</c>s.
///
/// <para>
/// Supports the standard 5-field crontab format: <c>minute hour day-of-month
/// month day-of-week</c>. Each field accepts:
/// </para>
/// <list type="bullet">
///   <item><c>*</c> — any value</item>
///   <item><c>*/N</c> — every N units within the field's natural range</item>
///   <item><c>A-B</c> — inclusive range</item>
///   <item><c>A,B,C</c> — explicit list</item>
///   <item>Combinations (e.g. <c>0,15,30,45</c> or <c>1-5,10</c>)</item>
/// </list>
///
/// <para>
/// Convenience constants on <see cref="Cron"/> cover the everyday cases
/// (<see cref="Cron.EveryMinute"/>, <see cref="Cron.HourlyAtMinute"/>,
/// <see cref="Cron.DailyAt"/>, etc.) so module authors don't have to remember
/// the crontab syntax.
/// </para>
///
/// <para>
/// Designed for in-process schedulers — the module's <c>BackgroundService</c>
/// owns the <see cref="DateTime"/> ticker and calls
/// <see cref="ShouldRun"/> on each tick. Idempotent: if your service runs on
/// multiple nodes you still need an outer "did anyone already run this minute?"
/// lock — that's outside this helper's scope.
/// </para>
///
/// <example>
/// <code>
/// public sealed class NightlyReportJob : BackgroundService
/// {
///     private readonly FcmsScheduledTask _schedule = new(Cron.DailyAt(hour: 2, minute: 0));
///
///     protected override async Task ExecuteAsync(CancellationToken ct)
///     {
///         while (!ct.IsCancellationRequested)
///         {
///             if (_schedule.ShouldRun(DateTime.UtcNow))
///                 await RunReportAsync(ct);
///             await Task.Delay(TimeSpan.FromMinutes(1), ct);
///         }
///     }
/// }
/// </code>
/// </example>
/// </summary>
public sealed class FcmsScheduledTask
{
    private readonly int[] _minutes;
    private readonly int[] _hours;
    private readonly int[] _daysOfMonth;
    private readonly int[] _months;
    private readonly int[] _daysOfWeek;
    private DateTime _lastRunAt = DateTime.MinValue;

    public string Expression { get; }

    public FcmsScheduledTask(string cronExpression)
    {
        Expression = cronExpression ?? throw new ArgumentNullException(nameof(cronExpression));
        var fields = cronExpression.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != 5)
            throw new ArgumentException(
                $"Cron expression must have 5 fields (minute hour day-of-month month day-of-week). Got: \"{cronExpression}\".",
                nameof(cronExpression));

        _minutes      = ParseField(fields[0], 0, 59);
        _hours        = ParseField(fields[1], 0, 23);
        _daysOfMonth  = ParseField(fields[2], 1, 31);
        _months       = ParseField(fields[3], 1, 12);
        _daysOfWeek   = ParseField(fields[4], 0, 6); // 0 = Sunday
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="now"/> matches the cron
    /// expression AND the previous matching minute has already been consumed
    /// (so a one-minute polling loop fires the task at most once per minute).
    /// </summary>
    public bool ShouldRun(DateTime now)
    {
        // Match the minute the caller is currently inside.
        var current = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, now.Kind);

        // Once-per-matching-minute guard — prevents a tight polling loop from
        // firing twice when called repeatedly inside the same minute.
        if (current <= _lastRunAt) return false;

        if (Array.BinarySearch(_minutes, current.Minute) < 0) return false;
        if (Array.BinarySearch(_hours, current.Hour) < 0) return false;
        if (Array.BinarySearch(_daysOfMonth, current.Day) < 0) return false;
        if (Array.BinarySearch(_months, current.Month) < 0) return false;
        if (Array.BinarySearch(_daysOfWeek, (int)current.DayOfWeek) < 0) return false;

        _lastRunAt = current;
        return true;
    }

    /// <summary>Resets the once-per-minute guard. Useful in tests.</summary>
    public void Reset() => _lastRunAt = DateTime.MinValue;


    private static int[] ParseField(string field, int min, int max)
    {
        var set = new SortedSet<int>();
        foreach (var part in field.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            AddPart(part, min, max, set);
        }
        if (set.Count == 0)
            throw new ArgumentException($"Field '{field}' produced no values.");
        return set.ToArray();
    }

    private static void AddPart(string part, int min, int max, SortedSet<int> set)
    {
        int step = 1;
        var rangePart = part;
        var slash = part.IndexOf('/');
        if (slash >= 0)
        {
            step = int.Parse(part[(slash + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture);
            if (step <= 0) throw new ArgumentException($"Step must be positive: '{part}'.");
            rangePart = part[..slash];
            if (rangePart.Length == 0) rangePart = "*";
        }

        int rangeStart, rangeEnd;
        if (rangePart == "*")
        {
            rangeStart = min; rangeEnd = max;
        }
        else if (rangePart.Contains('-'))
        {
            var dash = rangePart.IndexOf('-');
            rangeStart = int.Parse(rangePart[..dash], NumberStyles.Integer, CultureInfo.InvariantCulture);
            rangeEnd   = int.Parse(rangePart[(dash + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture);
        }
        else
        {
            rangeStart = rangeEnd = int.Parse(rangePart, NumberStyles.Integer, CultureInfo.InvariantCulture);
        }

        if (rangeStart < min || rangeEnd > max || rangeEnd < rangeStart)
            throw new ArgumentException($"Range {rangeStart}-{rangeEnd} is outside [{min},{max}] for part '{part}'.");

        for (var i = rangeStart; i <= rangeEnd; i += step)
            set.Add(i);
    }
}

/// <summary>
/// Pre-built cron expressions for common module schedules. Compose with
/// <see cref="FcmsScheduledTask"/>: <c>new FcmsScheduledTask(Cron.DailyAt(2, 0))</c>.
/// </summary>
public static class Cron
{
    public const string EveryMinute    = "* * * * *";
    public const string EveryFiveMin   = "*/5 * * * *";
    public const string EveryFifteenMin = "*/15 * * * *";
    public const string EveryThirtyMin = "*/30 * * * *";
    public const string Hourly         = "0 * * * *";

    /// <summary>Cron expression that fires at <paramref name="minute"/> past every hour.</summary>
    public static string HourlyAtMinute(int minute) => $"{minute} * * * *";

    /// <summary>Cron expression that fires once a day at the given hour and minute (24-hour clock).</summary>
    public static string DailyAt(int hour, int minute) => $"{minute} {hour} * * *";

    /// <summary>Cron expression that fires every week on <paramref name="dayOfWeek"/> at the given time. 0 = Sunday.</summary>
    public static string WeeklyAt(int dayOfWeek, int hour, int minute) => $"{minute} {hour} * * {dayOfWeek}";

    /// <summary>Cron expression that fires on the given day of the month at the given time.</summary>
    public static string MonthlyAt(int dayOfMonth, int hour, int minute) => $"{minute} {hour} {dayOfMonth} * *";
}
