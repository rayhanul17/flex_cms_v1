using System.Text.Json;
using FlexCms.Framework.Clock;
using FlexCms.Framework.Db;
using FlexCms.Framework.Services;

namespace FlexCms.Framework.Cms;

public static class AuditLogSettings
{
    public const string Key = "audit:enabled";
}

public class FcmsLogService : IFcmsLogService
{
    /// <summary>
    /// Pages through old logs in fixed-size chunks rather than loading the
    /// full result set into memory. Old impl hit OOM on large log tables —
    /// a busy site accumulating ~10k log rows / hour would push hundreds
    /// of thousands of rows through one ToListAsync per archive tick.
    /// </summary>
    public const int ArchiveBatchSize = 1000;

    // Logs (and their archive) are append-only — IAppendOnlyEntity opts
    // both backends out of the soft-delete query filter, so direct
    // IRepository<T> usage is safe and runs identically on EF + Mongo.
    private readonly IRepository<FcmsLog> _logs;
    private readonly IRepository<FcmsLogArchive> _archives;
    private readonly IFcmsUnitOfWork _uow;
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
        IRepository<FcmsLog> logs,
        IRepository<FcmsLogArchive> archives,
        IFcmsUnitOfWork uow,
        IFcmsContextService context,
        ISettingsService settings)
    {
        _logs = logs;
        _archives = archives;
        _uow = uow;
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

        await _logs.AddAsync(log, ct);
        await _uow.SaveChangesAsync(ct);
    }

    public async Task ArchiveOlderThanAsync(TimeSpan age, CancellationToken ct = default)
    {
        var cutoff = FcmsTime.Now - age;

        while (!ct.IsCancellationRequested)
        {
            // Page-and-move: oldest-first so newly-arriving logs don't get
            // dragged into the same window. We rely on FindAsync returning
            // a snapshot list (both EF + Mongo do); for huge log tables the
            // ArchiveBatchSize cap (1000) keeps each iteration cheap.
            var batch = (await _logs.FindAsync(l => l.CreatedAt < cutoff, ct))
                .OrderBy(l => l.CreatedAt)
                .Take(ArchiveBatchSize)
                .ToList();
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

            await _archives.AddRangeAsync(archiveEntries, ct);
            // Hard delete (DeleteRangeAsync) — append-only logs use real
            // delete, not soft-delete; both backends honor that here.
            await _logs.DeleteRangeAsync(batch, ct);
            await _uow.SaveChangesAsync(ct);

            // Last partial batch — nothing more to archive.
            if (batch.Count < ArchiveBatchSize) break;
        }
    }

    public async Task ClearArchiveAsync(CancellationToken ct = default)
    {
        // IRepository abstracts the bulk delete: EF impl uses
        // ExecuteDeleteAsync where supported; Mongo uses DeleteManyAsync.
        // Same call site, no backend-specific branches.
        var all = await _archives.GetAllAsync(ct);
        if (all.Count == 0) return;
        await _archives.DeleteRangeAsync(all, ct);
        await _uow.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<FcmsLog>> GetRecentAsync(int count = 100, CancellationToken ct = default)
    {
        var rows = await _logs.GetAllAsync(ct);
        return rows.OrderByDescending(l => l.CreatedAt).Take(count).ToList();
    }

    public async Task<IReadOnlyList<FcmsLogArchive>> GetArchiveAsync(int count = 100, CancellationToken ct = default)
    {
        var rows = await _archives.GetAllAsync(ct);
        return rows.OrderByDescending(l => l.CreatedAt).Take(count).ToList();
    }

    private sealed class AuditConfig
    {
        public bool Enabled { get; set; } = true;
    }
}
