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
        WriteIndented = false
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

    public async Task ArchiveOlderThanAsync(TimeSpan age, CancellationToken ct = default)
    {
        var cutoff = FcmsTime.Now - age;
        var old = await _db.Logs.Where(l => l.CreatedAt < cutoff).ToListAsync(ct);
        if (old.Count == 0) return;

        var archiveEntries = old.Select(log => new FcmsLogArchive
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
        // Hard-delete originals — they are now in the archive table.
        _db.Logs.RemoveRange(old);
        await _db.SaveChangesAsync(ct);
    }

    public async Task ClearArchiveAsync(CancellationToken ct = default)
    {
        var all = await _db.LogArchives.ToListAsync(ct);
        if (all.Count > 0)
        {
            _db.LogArchives.RemoveRange(all);
            await _db.SaveChangesAsync(ct);
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
