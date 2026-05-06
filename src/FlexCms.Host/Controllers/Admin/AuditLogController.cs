using System.Linq.Expressions;
using FlexCms.Framework.Auth;
using FlexCms.Framework.Cms;
using FlexCms.Framework.Db.Ef;
using FlexCms.Framework.Models;
using Microsoft.AspNetCore.Mvc;

namespace FlexCms.Host.Controllers.Admin;

[Route("admin/audit-log")]
public class AuditLogController : BaseAdminController
{
    private readonly IFcmsLogService _auditLog;
    private readonly FcmsDbContext _db;

    public AuditLogController(IFcmsLogService auditLog, FcmsDbContext db)
    {
        _auditLog = auditLog;
        _db = db;
    }

    [HttpGet("")]
    [FcmsAuthorize(FcmsPermissions.AuditView)]
    public IActionResult Index() => View();

    // ── Recent logs DataTable ────────────────────────────────────────────────

    [HttpPost("datatable-recent")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize(FcmsPermissions.AuditView)]
    public Task<IActionResult> DataTableRecent(DataTablesRequest req, CancellationToken ct)
    {
        var orderColumns = new Expression<Func<FcmsLog, object>>[]
        {
            l => l.CreatedAt,
            l => l.UserName,
            l => l.Action,
            l => l.EntityType,
            l => l.Module,
            l => l.Severity
        };
        return DataTableResult(
            _db.Logs,
            req,
            select: l => new
            {
                l.Id,
                CreatedAt = l.CreatedAt,
                l.UserName,
                l.UserIp,
                l.Action,
                l.EntityType,
                l.EntityId,
                l.Module,
                Severity = l.Severity.ToString(),
                l.Value
            },
            orderColumns: orderColumns,
            globalSearch: q => l => l.Action.Contains(q) || l.EntityType.Contains(q)
                                  || l.UserName.Contains(q) || l.EntityId.Contains(q),
            ct: ct);
    }

    // ── Archive DataTable ────────────────────────────────────────────────────

    [HttpPost("datatable-archive")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize(FcmsPermissions.AuditView)]
    public Task<IActionResult> DataTableArchive(DataTablesRequest req, CancellationToken ct)
    {
        var orderColumns = new Expression<Func<FcmsLogArchive, object>>[]
        {
            l => l.CreatedAt,
            l => l.UserName,
            l => l.Action,
            l => l.EntityType,
            l => l.Module,
            l => l.Severity
        };
        return DataTableResult(
            _db.LogArchives,
            req,
            select: l => new
            {
                l.Id,
                CreatedAt = l.CreatedAt,
                l.UserName,
                l.UserIp,
                l.Action,
                l.EntityType,
                l.EntityId,
                l.Module,
                Severity = l.Severity.ToString(),
                l.Value
            },
            orderColumns: orderColumns,
            globalSearch: q => l => l.Action.Contains(q) || l.EntityType.Contains(q)
                                  || l.UserName.Contains(q) || l.EntityId.Contains(q),
            ct: ct);
    }

    // ── Force-archive (manual override; LogArchiveService runs hourly anyway) ─

    [HttpPost("force-archive")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize(FcmsPermissions.AuditManage)]
    public async Task<IActionResult> ForceArchive(CancellationToken ct)
    {
        await _auditLog.ArchiveOlderThanAsync(TimeSpan.FromHours(24), ct);
        return FcmsOk("Logs older than 24 hours moved to archive.");
    }

    [HttpPost("clear-archive")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize(FcmsPermissions.AuditManage)]
    public async Task<IActionResult> ClearArchive(CancellationToken ct)
    {
        await _auditLog.ClearArchiveAsync(ct);
        return FcmsOk("Archive cleared.");
    }
}
