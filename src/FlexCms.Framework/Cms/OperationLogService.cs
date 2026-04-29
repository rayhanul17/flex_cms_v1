using System.Text.Json;
using FlexCms.Framework.Db;
using FlexCms.Framework.Services;

namespace FlexCms.Framework.Cms;

public class OperationLogService : IOperationLogService
{
    private readonly IRepository<FcmsOperationLog> _logs;
    private readonly IRepository<FcmsOperationLogArchive> _archive;
    private readonly IFcmsContextService _context;
    private readonly ISettingsService _settings;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public OperationLogService(
        IRepository<FcmsOperationLog> logs,
        IRepository<FcmsOperationLogArchive> archive,
        IFcmsContextService context,
        ISettingsService settings)
    {
        _logs = logs;
        _archive = archive;
        _context = context;
        _settings = settings;
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
        var cfg = await _settings.GetAsync<AuditSettings>("audit:enabled", ct: ct);
        if (!cfg.Enabled) return;

        var log = new FcmsOperationLog
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
    }

    public async Task ArchiveOlderThanAsync(TimeSpan age, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow - age;
        var all = await _logs.GetAllAsync(ct);
        var old = all.Where(l => l.CreatedAt < cutoff).ToList();

        if (old.Count == 0) return;

        foreach (var log in old)
        {
            var archivedEntry = new FcmsOperationLogArchive
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
            };
            await _archive.AddAsync(archivedEntry, ct);
        }

        foreach (var log in old)
            await _logs.DeleteAsync(log, ct);
    }

    public async Task ClearArchiveAsync(CancellationToken ct = default)
    {
        var all = await _archive.GetAllAsync(ct);
        foreach (var entry in all)
            await _archive.DeleteAsync(entry, ct);
    }

    public async Task<IReadOnlyList<FcmsOperationLog>> GetRecentAsync(int count = 100, CancellationToken ct = default)
    {
        var all = await _logs.GetAllAsync(ct);
        return all.OrderByDescending(l => l.CreatedAt).Take(count).ToList();
    }

    public async Task<IReadOnlyList<FcmsOperationLogArchive>> GetArchiveAsync(int count = 100, CancellationToken ct = default)
    {
        var all = await _archive.GetAllAsync(ct);
        return all.OrderByDescending(l => l.CreatedAt).Take(count).ToList();
    }

    // ── Inner settings POCO ───────────────────────────────────────────────────

    private sealed class AuditSettings
    {
        public bool Enabled { get; set; } = true;
    }
}
