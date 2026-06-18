using System.Linq.Expressions;
using FlexCms.Framework.Auth;
using FlexCms.Framework.Cms;
using FlexCms.Framework.Db;
using FlexCms.Framework.Db.Ef;
using FlexCms.Framework.Models;
using Microsoft.AspNetCore.Mvc;

namespace FlexCms.Host.Controllers.Admin;

[Route("admin/audit-log")]
public class AuditLogController : BaseAdminController
{
    private readonly IFcmsLogService _auditLog;
    private readonly IRepository<FcmsLog> _logs;
    private readonly IRepository<FcmsLogArchive> _archives;
    private readonly FcmsDbContext _db;

    // IRepository<> keeps the DataTable endpoints provider-agnostic; the
    // direct FcmsDbContext is only used by the chain verifier (which needs
    // a DbSet to iterate in CreatedAt order).
    public AuditLogController(
        IFcmsLogService auditLog,
        IRepository<FcmsLog> logs,
        IRepository<FcmsLogArchive> archives,
        FcmsDbContext db)
    {
        _auditLog = auditLog;
        _logs = logs;
        _archives = archives;
        _db = db;
    }

    [HttpGet("")]
    [FcmsAuthorize(FcmsPermissions.AuditView)]
    public IActionResult Index() => View();


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
            _logs.Query(),
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
            _archives.Query(),
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
        var moved = await _auditLog.ArchiveOlderThanAsync(TimeSpan.FromHours(24), ct);
        return FcmsOk(moved == 0
            ? "Nothing to archive — all current logs are newer than 24 hours."
            : $"{moved} log entr{(moved == 1 ? "y" : "ies")} older than 24 hours moved to archive.");
    }

    [HttpPost("clear-archive")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize(FcmsPermissions.AuditManage)]
    public async Task<IActionResult> ClearArchive(CancellationToken ct)
    {
        await _auditLog.ClearArchiveAsync(ct);
        return FcmsOk("Archive cleared.");
    }

    /// <summary>
    /// Walk the audit-log hash chain and report whether it's intact. Caps
    /// at 50k rows per call (paged scans are a follow-up). Returns the id
    /// of the first broken row so an admin can pivot directly to it. See
    /// security-audit-fix-plan §5.3.
    /// </summary>
    [HttpPost("verify-chain")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize(FcmsPermissions.AuditManage)]
    public async Task<IActionResult> VerifyChain(CancellationToken ct)
    {
        var (intact, firstBrokenRowId, rowsChecked) = await FcmsLogChain.VerifyAsync(_db, limit: 50_000, ct);
        if (intact)
            return FcmsOk($"Audit chain intact across {rowsChecked} row{(rowsChecked == 1 ? "" : "s")}.",
                new { rowsChecked, intact = true });
        return FcmsFail($"Audit chain broken — first inconsistent row Id = {firstBrokenRowId}. " +
                        $"Checked {rowsChecked} row{(rowsChecked == 1 ? "" : "s")}.");
    }
}
