using FlexCms.Framework.Auth;
using FlexCms.Framework.Cms;
using FlexCms.Framework.Db;
using FlexCms.Framework.Services;
using Microsoft.AspNetCore.Mvc;

namespace FlexCms.Host.Controllers.Admin;

[Route("admin/audit-log")]
public class AuditLogController : BaseAdminController
{
    private readonly IOperationLogService _auditLog;
    private readonly IFcmsUnitOfWork _uow;

    public AuditLogController(IOperationLogService auditLog, IFcmsUnitOfWork uow)
    {
        _auditLog = auditLog;
        _uow = uow;
    }

    [HttpGet("")]
    [FcmsAuthorize("audit.view")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var recent = await _auditLog.GetRecentAsync(200, ct);
        var archive = await _auditLog.GetArchiveAsync(200, ct);
        ViewBag.Archive = archive;
        return View(recent);
    }

    [HttpPost("archive")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize("audit.manage")]
    public async Task<IActionResult> Archive(CancellationToken ct)
    {
        await _auditLog.ArchiveOlderThanAsync(TimeSpan.FromHours(24), ct);
        await _uow.SaveChangesAsync(ct);
        ShowSuccess("Logs older than 24 hours have been moved to archive.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("clear-archive")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize("audit.manage")]
    public async Task<IActionResult> ClearArchive(CancellationToken ct)
    {
        await _auditLog.ClearArchiveAsync(ct);
        await _uow.SaveChangesAsync(ct);
        ShowSuccess("Archive cleared.");
        return RedirectToAction(nameof(Index));
    }
}
