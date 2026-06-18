using System.Text.Json;
using FlexCms.Framework.Clock;
using FlexCms.Framework.Db;
using FlexCms.Framework.Db.Ef;
using FlexCms.Framework.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
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
/// </summary>
public sealed class FcmsAuditInterceptor : SaveChangesInterceptor
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
        TypeInfoResolver = new FcmsLogJsonResolver(),
        ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles,
    };

    private readonly IFcmsContextService _ctx;
    // ISettingsService resolved lazily via IServiceProvider — injecting it directly
    // creates a DI cycle: FcmsDbContext → FcmsAuditInterceptor → ISettingsService →
    // IRepository<FcmsSettings> → DbContext (= FcmsDbContext). Lazy lookup breaks the cycle.
    private readonly IServiceProvider _sp;
    private readonly ILogger<FcmsAuditInterceptor> _logger;

    // AsyncLocal is safe across async continuations (unlike [ThreadStatic] which
    // is per-thread and loses its value after an await resumes on a different thread).
    // Each logical call-chain (one SaveChangesAsync call stack) gets its own slot.
    private static readonly AsyncLocal<CallState?> _state = new();

    public FcmsAuditInterceptor(
        IFcmsContextService ctx,
        IServiceProvider sp,
        ILogger<FcmsAuditInterceptor> logger)
    {
        _ctx = ctx;
        _sp = sp;
        _logger = logger;
    }


    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken ct = default)
    {
        // When we are writing our own audit rows we set Writing=true in the state.
        // Skip capture entirely to avoid infinite recursion.
        if (_state.Value?.Writing == true)
            return base.SavingChangesAsync(eventData, result, ct);

        _state.Value = new CallState(Capture(eventData.Context));
        return base.SavingChangesAsync(eventData, result, ct);
    }


    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken ct = default)
    {
        var state = _state.Value;
        if (state is null || state.Writing || state.Pending.Count == 0)
            return result;

        var ctx = eventData.Context;
        if (ctx is null) return result;

        // Check audit enabled — wrapped in try/catch so a settings read failure
        // (e.g. during DB migration/setup) never breaks the main operation.
        try
        {
            var settings = _sp.GetRequiredService<ISettingsService>();
            var cfg = await settings.GetAsync<AuditConfig>(AuditLogSettings.Key, ct: ct);
            if (!cfg.Enabled) return result;
        }
        catch
        {
            return result;
        }

        var now = FcmsTime.Now;
        var userId = _ctx.UserId;
        var userName = _ctx.Username ?? string.Empty;
        var ip = _ctx.IpAddress;
        var ua = $"{_ctx.Browser} / {_ctx.Os}";

        foreach (var entry in state.Pending)
        {
            try
            {
                ctx.Set<FcmsLog>().Add(new FcmsLog
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
                    Module = entry.Module,
                    Severity = entry.Severity,
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "FcmsAuditInterceptor: failed to queue log entry for {Action}/{EntityType}/{EntityId}",
                    entry.Action, entry.EntityType, entry.EntityId);
            }
        }

        try
        {
            // Mark Writing so SavingChangesAsync skips capture for this inner call.
            state.Writing = true;
            await ctx.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FcmsAuditInterceptor: failed to persist audit log rows.");

            // Last-resort fallback: write the dropped rows to an append-only
            // JSONL file under App_Data/logs/ so the trail isn't lost. We
            // resolve the sink lazily so a missing IFcmsAuditFallbackSink
            // registration (e.g. unit-test contexts) doesn't crash the
            // main path. See security-audit-fix-plan §5.2.
            try
            {
                var sink = _sp.GetService<IFcmsAuditFallbackSink>();
                if (sink is not null)
                {
                    foreach (var entry in state.Pending)
                    {
                        await sink.WriteAsync(new FcmsAuditFallbackEntry(
                            CreatedAtUtc: now.ToUniversalTime(),
                            Action: entry.Action,
                            EntityType: entry.EntityType,
                            EntityId: entry.EntityId,
                            UserId: userId?.ToString(),
                            UserName: userName,
                            UserIp: ip,
                            Module: entry.Module,
                            Severity: entry.Severity.ToString(),
                            Value: entry.Snapshot,
                            Reason: ex.GetType().Name + ": " + ex.Message), ct);
                    }
                }
            }
            catch (Exception sinkEx)
            {
                _logger.LogError(sinkEx, "FcmsAuditInterceptor: fallback sink also failed.");
            }
        }
        finally
        {
            state.Writing = false;
            _state.Value = null;
        }

        return result;
    }


    private static List<PendingEntry> Capture(DbContext? ctx)
    {
        if (ctx is null) return [];

        var list = new List<PendingEntry>();

        foreach (var entry in ctx.ChangeTracker.Entries<BaseEfEntity>())
        {
            var type = entry.Entity.GetType();

            // IAppendOnlyEntity types (FcmsLog, FcmsLogArchive) are always skipped.
            if (typeof(IAppendOnlyEntity).IsAssignableFrom(type)) continue;
            if (type.GetCustomAttributes(typeof(FcmsAuditIgnoreEntityAttribute), inherit: true).Length > 0) continue;

            var (verb, severity) = DeriveAction(entry);
            if (verb is null) continue;

            var prefix = GetPrefix(type);

            string? snapshot = null;
            try { snapshot = JsonSerializer.Serialize(entry.Entity, type, JsonOpts); }
            catch { /* best-effort */ }

            list.Add(new PendingEntry(
                Action: $"{prefix}.{verb}",
                EntityType: type.Name,
                EntityId: entry.Entity.Id.ToString(),
                Snapshot: snapshot,
                Module: GetModuleId(entry.Entity, type),
                Severity: severity));
        }

        return list;
    }

    internal static (string? verb, FcmsLogSeverity severity) DeriveAction(
        Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<BaseEfEntity> entry)
    {
        return entry.State switch
        {
            EntityState.Added => ("Created", FcmsLogSeverity.Info),
            EntityState.Modified when entry.Entity.Status == EntityStatus.Deleted
                                 => ("Deleted", FcmsLogSeverity.Info),
            EntityState.Modified => ("Updated", FcmsLogSeverity.Info),
            EntityState.Deleted => ("HardDeleted", FcmsLogSeverity.Warning),
            _ => (null, FcmsLogSeverity.Info),
        };
    }

    internal static string GetPrefix(Type type)
    {
        var attr = type.GetCustomAttributes(typeof(FcmsAuditEntityAttribute), inherit: true)
                       .OfType<FcmsAuditEntityAttribute>()
                       .FirstOrDefault();
        if (attr is not null) return attr.ActionPrefix;

        var name = type.Name;
        return name.StartsWith("Fcms", StringComparison.Ordinal) ? name[4..] : name;
    }

    /// <summary>
    /// Resolve the module ID for an audit row so admins can filter
    /// <c>fcms_logs</c> by module.
    /// <list type="number">
    ///   <item>If the entity itself carries a <c>ModuleId</c> string property
    ///         (FcmsMenuItem, FcmsPermission, FcmsModuleRecord, FcmsSettings)
    ///         use that value verbatim — that's the most precise signal.</item>
    ///   <item>Else derive from the CLR namespace root: anything not under
    ///         <c>FlexCms.Framework.*</c>, <c>FlexCms.Core.*</c>, or
    ///         <c>FlexCms.Host.*</c> is treated as a module entity and tagged
    ///         with the assembly's simple name (e.g. <c>FlexCms.Sample.Hello</c>).</item>
    ///   <item>Framework / host entities fall back to <c>"core"</c>.</item>
    /// </list>
    /// </summary>
    internal static string GetModuleId(object entity, Type type)
    {
        var moduleIdProp = type.GetProperty("ModuleId", typeof(string));
        if (moduleIdProp is not null && moduleIdProp.GetValue(entity) is string mid && !string.IsNullOrWhiteSpace(mid))
            return mid;

        var ns = type.Namespace ?? "";
        if (ns.StartsWith("FlexCms.Framework", StringComparison.Ordinal) ||
            ns.StartsWith("FlexCms.Core", StringComparison.Ordinal) ||
            ns.StartsWith("FlexCms.Host", StringComparison.Ordinal))
            return "core";

        return type.Assembly.GetName().Name ?? "core";
    }

    private sealed record PendingEntry(
        string Action,
        string EntityType,
        string EntityId,
        string? Snapshot,
        string Module,
        FcmsLogSeverity Severity);

    private sealed class CallState(List<PendingEntry> pending)
    {
        public List<PendingEntry> Pending { get; } = pending;
        public bool Writing { get; set; }
    }

    private sealed class AuditConfig
    {
        public bool Enabled { get; set; } = true;
    }
}
