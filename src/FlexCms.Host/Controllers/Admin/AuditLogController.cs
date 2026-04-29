using FlexCms.Framework.Cms;
using FlexCms.Framework.Db;
using FlexCms.Framework.Services;
using Microsoft.AspNetCore.Mvc;

namespace FlexCms.Host.Controllers.Admin;

[Route("admin/audit-log")]
public class AuditLogController : BaseAdminController
{
    private readonly IOperationLogService _auditLog;
    private readonly ISettingsService _settings;
    private readonly IFcmsUnitOfWork _uow;

    public AuditLogController(
        IOperationLogService auditLog,
        ISettingsService settings,
        IFcmsUnitOfWork uow)
    {
        _auditLog = auditLog;
        _settings = settings;
        _uow = uow;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var recent = await _auditLog.GetRecentAsync(200, ct);
        var archive = await _auditLog.GetArchiveAsync(200, ct);

        var cfg = await _settings.GetAsync<AuditEnabledSettings>("audit:enabled", ct: ct);
        ViewBag.AuditEnabled = cfg.Enabled;
        ViewBag.Archive = archive;

        return View(recent);
    }

    [HttpPost("archive")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Archive(CancellationToken ct)
    {
        await _auditLog.ArchiveOlderThanAsync(TimeSpan.FromHours(24), ct);
        await _uow.SaveChangesAsync(ct);
        ShowSuccess("Logs older than 24 hours have been moved to archive.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("clear-archive")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ClearArchive(CancellationToken ct)
    {
        await _auditLog.ClearArchiveAsync(ct);
        await _uow.SaveChangesAsync(ct);
        ShowSuccess("Archive cleared.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("toggle")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(CancellationToken ct)
    {
        var cfg = await _settings.GetAsync<AuditEnabledSettings>("audit:enabled", ct: ct);
        cfg.Enabled = !cfg.Enabled;
        await _settings.SaveAsync("audit:enabled", cfg, ct);
        return FcmsOk(cfg.Enabled ? "Audit logging enabled." : "Audit logging disabled.", new { enabled = cfg.Enabled });
    }

    // ── Inner settings POCO ───────────────────────────────────────────────────

    private sealed class AuditEnabledSettings
    {
        public bool Enabled { get; set; } = true;
    }
}
