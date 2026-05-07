using System.Text.Json;
using FlexCms.Framework.Clock;
using FlexCms.Framework.Db.Ef;
using FlexCms.Framework.Services;
using Microsoft.EntityFrameworkCore;

namespace FlexCms.Framework.Cms;

public static class AuditLogSettings
{
    public const string Key = "audit:enabled";
}

public class FcmsLogService : IFcmsLogService
{
    // Logs use FcmsDbContext directly (not IRepository<T>) — they are
    // append-only, have no Status / DeletedAt columns, and don't need the
    // soft-delete query filter that IRepository injects on every call.
    private readonly FcmsDbContext _db;
    private readonly IFcmsContextService _context;
    private readonly ISettingsService _settings;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
        // Strip nav properties + Identity sensitive fields automatically — callers
        // can pass full entities directly without anonymous projection boilerplate.
        TypeInfoResolver = new FcmsLogJsonResolver(),
        // Defensive: in case any nav property slips through, prevent infinite loops.
        ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles
    };

    public FcmsLogService(
        FcmsDbContext db,
        IFcmsContextService context,
        ISettingsService settings)
    {
        _db = db;
        _context = context;
        _settings = settings;
    }

    public async Task LogAsync(
        string action,
        string entityType,
        string entityId,
        object? value = null,
        string module = "core",
        FcmsLogSeverity severity = FcmsLogSeverity.Info,
        CancellationToken ct = default)
    {
        var cfg = await _settings.GetAsync<AuditConfig>(AuditLogSettings.Key, ct: ct);
        if (!cfg.Enabled) return;

        var log = new FcmsLog
        {
            CreatedAt = FcmsTime.Now,
            UserId = _context.UserId,
            UserName = _context.Username ?? string.Empty,
            UserIp = _context.IpAddress,
            UserAgent = $"{_context.Browser} / {_context.Os}",
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Value = value is null ? null : JsonSerializer.Serialize(value, JsonOpts),
            Module = module,
            Severity = severity
        };

        _db.Logs.Add(log);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Pages through old logs in fixed-size chunks rather than loading the
    /// full result set into memory. Old impl hit OOM on large log tables —
    /// a busy site accumulating ~10k log rows / hour would push hundreds
    /// of thousands of rows through one ToListAsync per archive tick.
    /// </summary>
    public const int ArchiveBatchSize = 1000;

    public async Task ArchiveOlderThanAsync(TimeSpan age, CancellationToken ct = default)
    {
        var cutoff = FcmsTime.Now - age;

        while (!ct.IsCancellationRequested)
        {
            // Fetch one batch ordered by oldest-first so we never re-scan
            // newly-arriving logs into the same window.
            var batch = await _db.Logs
                .Where(l => l.CreatedAt < cutoff)
                .OrderBy(l => l.CreatedAt)
                .Take(ArchiveBatchSize)
                .ToListAsync(ct);
            if (batch.Count == 0) break;

            var archiveEntries = batch.Select(log => new FcmsLogArchive
            {
                CreatedAt = log.CreatedAt,
                UserId = log.UserId,
                UserName = log.UserName,
                UserIp = log.UserIp,
                UserAgent = log.UserAgent,
                Action = log.Action,
                EntityType = log.EntityType,
                EntityId = log.EntityId,
                Value = log.Value,
                Module = log.Module,
                Severity = log.Severity
            }).ToList();

            _db.LogArchives.AddRange(archiveEntries);
            _db.Logs.RemoveRange(batch);
            await _db.SaveChangesAsync(ct);

            // Detach so the next iteration's tracker stays small. Without
            // this the change tracker would grow to N entries by the end
            // of a multi-batch archive pass.
            foreach (var e in batch) _db.Entry(e).State = EntityState.Detached;
            foreach (var a in archiveEntries) _db.Entry(a).State = EntityState.Detached;

            // Last partial batch — nothing more to archive.
            if (batch.Count < ArchiveBatchSize) break;
        }
    }

    public async Task ClearArchiveAsync(CancellationToken ct = default)
    {
        // Prefer ExecuteDeleteAsync (single server-side DELETE, no tracker
        // rows) on relational providers. EF InMemory throws
        // InvalidOperationException for it, so fall back to a tracked
        // delete — fine for tests, never hit in production.
        try
        {
            await _db.LogArchives.ExecuteDeleteAsync(ct);
        }
        catch (InvalidOperationException)
        {
            var all = await _db.LogArchives.ToListAsync(ct);
            if (all.Count > 0)
            {
                _db.LogArchives.RemoveRange(all);
                await _db.SaveChangesAsync(ct);
            }
        }
    }

    public async Task<IReadOnlyList<FcmsLog>> GetRecentAsync(int count = 100, CancellationToken ct = default)
    {
        return await _db.Logs
            .OrderByDescending(l => l.CreatedAt)
            .Take(count)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<FcmsLogArchive>> GetArchiveAsync(int count = 100, CancellationToken ct = default)
    {
        return await _db.LogArchives
            .OrderByDescending(l => l.CreatedAt)
            .Take(count)
            .ToListAsync(ct);
    }

    private sealed class AuditConfig
    {
        public bool Enabled { get; set; } = true;
    }
}
