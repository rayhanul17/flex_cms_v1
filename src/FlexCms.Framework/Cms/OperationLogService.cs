using System.Text.Json;
using FlexCms.Framework.Db;
using FlexCms.Framework.Services;

namespace FlexCms.Framework.Cms;

public static class AuditLogSettings
{
    public const string Key = "audit:enabled";
}

public class OperationLogService : IOperationLogService
{
    private readonly IRepository<FcmsLog> _logs;
    private readonly IRepository<FcmsLogArchive> _archive;
    private readonly IFcmsContextService _context;
    private readonly ISettingsService _settings;
    private readonly IFcmsUnitOfWork _uow;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public OperationLogService(
        IRepository<FcmsLog> logs,
        IRepository<FcmsLogArchive> archive,
        IFcmsContextService context,
        ISettingsService settings,
        IFcmsUnitOfWork uow)
    {
        _logs = logs;
        _archive = archive;
        _context = context;
        _settings = settings;
        _uow = uow;
    }

    public async Task LogAsync(
        string action,
        string entityType,
        string entityId,
        object? newValue = null,
        string module = "core",
        FcmsLogSeverity severity = FcmsLogSeverity.Info,
        CancellationToken ct = default)
    {
        var cfg = await _settings.GetAsync<AuditConfig>(AuditLogSettings.Key, ct: ct);
        if (!cfg.Enabled) return;

        var log = new FcmsLog
        {
            UserId = _context.UserId,
            UserName = _context.Username ?? string.Empty,
            UserIp = _context.IpAddress,
            UserAgent = $"{_context.Browser} / {_context.Os}",
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            NewValue = newValue is null ? null : JsonSerializer.Serialize(newValue, JsonOpts),
            Module = module,
            Severity = severity
        };

        await _logs.AddAsync(log, ct);
        await _uow.SaveChangesAsync(ct);
    }

    public async Task ArchiveOlderThanAsync(TimeSpan age, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow - age;
        var old = await _logs.FindAsync(l => l.CreatedAt < cutoff, ct);
        if (old.Count == 0) return;

        var archiveEntries = old.Select(log => new FcmsLogArchive
        {
            UserId = log.UserId,
            UserName = log.UserName,
            UserIp = log.UserIp,
            UserAgent = log.UserAgent,
            Action = log.Action,
            EntityType = log.EntityType,
            EntityId = log.EntityId,
            NewValue = log.NewValue,
            Module = log.Module,
            Severity = log.Severity,
            CreatedAt = log.CreatedAt,
            UpdatedAt = log.UpdatedAt,
            CreatedBy = log.CreatedBy,
            UpdatedBy = log.UpdatedBy
        }).ToList();

        await _archive.AddRangeAsync(archiveEntries, ct);
        await _logs.SoftDeleteRangeAsync(old, ct);
        await _uow.SaveChangesAsync(ct);
    }

    public async Task ClearArchiveAsync(CancellationToken ct = default)
    {
        var all = await _archive.GetAllAsync(ct);
        if (all.Count > 0)
        {
            await _archive.SoftDeleteRangeAsync(all, ct);
            await _uow.SaveChangesAsync(ct);
        }
    }

    public async Task<IReadOnlyList<FcmsLog>> GetRecentAsync(int count = 100, CancellationToken ct = default)
    {
        var filter = new QueryFilter<FcmsLog>()
            .OrderByDescending(l => l.CreatedAt)
            .Page(1, count);
        return await _logs.FindAsync(filter, ct);
    }

    public async Task<IReadOnlyList<FcmsLogArchive>> GetArchiveAsync(int count = 100, CancellationToken ct = default)
    {
        var filter = new QueryFilter<FcmsLogArchive>()
            .OrderByDescending(l => l.CreatedAt)
            .Page(1, count);
        return await _archive.FindAsync(filter, ct);
    }

    private sealed class AuditConfig
    {
        public bool Enabled { get; set; } = true;
    }
}
