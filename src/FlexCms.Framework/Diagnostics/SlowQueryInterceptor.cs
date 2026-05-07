using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace FlexCms.Framework.Diagnostics;

/// <summary>
/// EF Core interceptor that logs queries exceeding a configurable threshold
/// (default 1s) and stores the recent slow ones in an in-memory ring buffer.
/// Admin "System → Slow Queries" surfaces them.
///
/// <para>
/// Logs at <c>Warning</c> so default appsettings (Information) capture them.
/// The interceptor is a singleton — keep state cheap (capped buffer of 50).
/// </para>
/// </summary>
public sealed class SlowQueryInterceptor : DbCommandInterceptor
{
    public const int RingBufferCapacity = 50;
    public static readonly TimeSpan DefaultThreshold = TimeSpan.FromMilliseconds(1000);

    private readonly TimeSpan _threshold;
    private readonly ILogger<SlowQueryInterceptor> _logger;
    private readonly ConcurrentQueue<SlowQueryRecord> _recent = new();

    public SlowQueryInterceptor(ILogger<SlowQueryInterceptor> logger, TimeSpan? threshold = null)
    {
        _logger = logger;
        _threshold = threshold ?? DefaultThreshold;
    }

    /// <summary>Snapshot of the recent slow queries, newest first.</summary>
    public IReadOnlyList<SlowQueryRecord> GetRecent() => _recent.Reverse().ToList();

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        Track(command, eventData.Duration);
        return ValueTask.FromResult(result);
    }

    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        Track(command, eventData.Duration);
        return ValueTask.FromResult(result);
    }

    public override ValueTask<object?> ScalarExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        object? result,
        CancellationToken cancellationToken = default)
    {
        Track(command, eventData.Duration);
        return ValueTask.FromResult(result);
    }

    private void Track(DbCommand command, TimeSpan duration)
    {
        if (duration < _threshold) return;

        var sql = command.CommandText ?? "";
        // Truncate very long queries so the buffer doesn't bloat — admins can
        // re-execute against the DB if they need the full text.
        if (sql.Length > 2000) sql = sql[..2000] + "…";

        var record = new SlowQueryRecord(DateTime.UtcNow, duration, sql);
        _recent.Enqueue(record);

        // Cap the ring buffer to RingBufferCapacity. Cheap drain — we don't
        // need exact ordering under contention since it's diagnostic only.
        while (_recent.Count > RingBufferCapacity && _recent.TryDequeue(out _)) { }

        _logger.LogWarning("Slow query ({DurationMs} ms): {Sql}", (int)duration.TotalMilliseconds, sql);
    }
}

public sealed record SlowQueryRecord(DateTime CapturedAt, TimeSpan Duration, string Sql);
