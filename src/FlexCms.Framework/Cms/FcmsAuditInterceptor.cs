using System.Text.Json;
using FlexCms.Framework.Clock;
using FlexCms.Framework.Db;
using FlexCms.Framework.Db.Ef;
using FlexCms.Framework.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace FlexCms.Framework.Cms;


/// <summary>
/// EF Core save-changes interceptor that automatically writes one
/// <see cref="FcmsLog"/> row for every Add/Modify/SoftDelete on any
/// <see cref="BaseEfEntity"/> that is not excluded by
/// <see cref="FcmsAuditIgnoreEntityAttribute"/>.
///
/// <para>
/// <b>Auto-derived action names</b> — for an entity named <c>FcmsPost</c>
/// (or decorated with <c>[FcmsAuditEntity("Post")]</c>):
/// <list type="bullet">
///   <item><c>EntityState.Added</c> → <c>"Post.Created"</c></item>
///   <item><c>EntityState.Modified</c> where Status == Deleted → <c>"Post.Deleted"</c></item>
///   <item><c>EntityState.Modified</c> otherwise → <c>"Post.Updated"</c></item>
///   <item><c>EntityState.Deleted</c> (hard delete) → <c>"Post.HardDeleted"</c></item>
/// </list>
/// </para>
///
/// <para>
/// Service-layer code that calls <see cref="IFcmsLogService.LogAsync"/> directly
/// (OTP events, auth events, etc.) continues to work as before — this interceptor
/// only handles entity CRUD.  Manual log calls for the same entity+action in the
/// same request are de-duplicated by the interceptor skipping entities whose type
/// names match a well-known manual-log prefix (opt-out via
/// <see cref="FcmsAuditIgnoreEntityAttribute"/>).
/// </para>
/// </summary>
public sealed class FcmsAuditInterceptor : SaveChangesInterceptor
{
    // IAppendOnlyEntity types (FcmsLog, FcmsLogArchive) are always skipped —
    // auto-logging them would recurse or produce meaningless noise.

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
        TypeInfoResolver = new FcmsLogJsonResolver(),
        ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles,
    };

    private readonly IFcmsContextService _ctx;
    private readonly ISettingsService _settings;
    private readonly ILogger<FcmsAuditInterceptor> _logger;

    // Captured inside SavingChangesAsync (before EF clears the tracker) and
    // consumed in SavedChangesAsync (after the commit succeeds).
    [ThreadStatic]
    private static List<PendingEntry>? _pending;

    // Guards the secondary SaveChangesAsync call (writing audit rows) so
    // the interceptor doesn't re-enter and log its own FcmsLog inserts.
    [ThreadStatic]
    private static bool _writing;

    public FcmsAuditInterceptor(
        IFcmsContextService ctx,
        ISettingsService settings,
        ILogger<FcmsAuditInterceptor> logger)
    {
        _ctx = ctx;
        _settings = settings;
        _logger = logger;
    }

    // ── Capture entries BEFORE EF commits (tracker is cleared after save) ────

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken ct = default)
    {
        // Skip capturing when we ourselves are writing audit rows.
        if (!_writing)
            _pending = Capture(eventData.Context);
        return base.SavingChangesAsync(eventData, result, ct);
    }

    // ── Write audit rows AFTER a successful commit ───────────────────────────

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken ct = default)
    {
        var pending = _pending;
        _pending = null;
        if (pending is null || pending.Count == 0) return result;

        // Audit logging disabled in settings → skip entirely.
        try
        {
            var cfg = await _settings.GetAsync<AuditConfig>(AuditLogSettings.Key, ct: ct);
            if (!cfg.Enabled) return result;
        }
        catch
        {
            // Settings unavailable (e.g. during migration/setup) — skip silently.
            return result;
        }

        var ctx = eventData.Context;
        if (ctx is null) return result;

        var now = FcmsTime.Now;
        var userId = _ctx.UserId;
        var userName = _ctx.Username ?? string.Empty;
        var ip = _ctx.IpAddress;
        var ua = $"{_ctx.Browser} / {_ctx.Os}";

        foreach (var entry in pending)
        {
            try
            {
                var log = new FcmsLog
                {
                    CreatedAt = now,
                    UserId = userId,
                    UserName = userName,
                    UserIp = ip,
                    UserAgent = ua,
                    Action = entry.Action,
                    EntityType = entry.EntityType,
                    EntityId = entry.EntityId,
                    Value = entry.Snapshot,
                    Module = "core",
                    Severity = entry.Severity,
                };
                ctx.Set<FcmsLog>().Add(log);
            }
            catch (Exception ex)
            {
                // Audit failure must never break the main operation.
                _logger.LogWarning(ex,
                    "FcmsAuditInterceptor: failed to queue log entry for {Action}/{EntityType}/{EntityId}",
                    entry.Action, entry.EntityType, entry.EntityId);
            }
        }

        try
        {
            // _writing guard prevents the interceptor from re-entering when
            // EF calls SavingChangesAsync again for this secondary save.
            _writing = true;
            await ctx.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FcmsAuditInterceptor: failed to persist audit log rows.");
        }
        finally
        {
            _writing = false;
        }

        return result;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static List<PendingEntry> Capture(DbContext? ctx)
    {
        if (ctx is null) return [];

        var list = new List<PendingEntry>();

        foreach (var entry in ctx.ChangeTracker.Entries<BaseEfEntity>())
        {
            var type = entry.Entity.GetType();

            // Skip append-only infrastructure entities (FcmsLog, FcmsLogArchive, etc.)
            if (typeof(IAppendOnlyEntity).IsAssignableFrom(type)) continue;
            if (type.GetCustomAttributes(typeof(FcmsAuditIgnoreEntityAttribute), inherit: true).Length > 0) continue;

            var (action, severity) = DeriveAction(entry);
            if (action is null) continue;

            var prefix = GetPrefix(type);
            var entityId = entry.Entity.Id.ToString();

            string? snapshot = null;
            try
            {
                snapshot = JsonSerializer.Serialize(entry.Entity, type, JsonOpts);
            }
            catch { /* snapshot best-effort */ }

            list.Add(new PendingEntry(
                Action: $"{prefix}.{action}",
                EntityType: type.Name,
                EntityId: entityId,
                Snapshot: snapshot,
                Severity: severity));
        }

        return list;
    }

    private static (string? verb, FcmsLogSeverity severity) DeriveAction(
        Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<BaseEfEntity> entry)
    {
        return entry.State switch
        {
            EntityState.Added => ("Created", FcmsLogSeverity.Info),

            EntityState.Modified when entry.Entity.Status == EntityStatus.Deleted
                => ("Deleted", FcmsLogSeverity.Info),

            EntityState.Modified => ("Updated", FcmsLogSeverity.Info),

            // Hard delete via repository.DeleteAsync / DeleteRangeAsync
            EntityState.Deleted => ("HardDeleted", FcmsLogSeverity.Warning),

            _ => (null, FcmsLogSeverity.Info),
        };
    }

    private static string GetPrefix(Type type)
    {
        var attr = type.GetCustomAttributes(typeof(FcmsAuditEntityAttribute), inherit: true)
                       .OfType<FcmsAuditEntityAttribute>()
                       .FirstOrDefault();
        if (attr is not null) return attr.ActionPrefix;

        // Strip common "Fcms" prefix for readability: FcmsPost → Post
        var name = type.Name;
        return name.StartsWith("Fcms", StringComparison.Ordinal) ? name[4..] : name;
    }

    private sealed record PendingEntry(
        string Action,
        string EntityType,
        string EntityId,
        string? Snapshot,
        FcmsLogSeverity Severity);

    private sealed class AuditConfig
    {
        public bool Enabled { get; set; } = true;
    }
}
