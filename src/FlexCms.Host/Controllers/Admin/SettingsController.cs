using FlexCms.Framework.Auth;
using FlexCms.Framework.Cms;
using FlexCms.Framework.Services;
using Microsoft.AspNetCore.Mvc;

namespace FlexCms.Host.Controllers.Admin;

[Route("admin/settings")]
public class SettingsController : BaseAdminController
{
    private readonly ISettingsService _settings;

    public SettingsController(ISettingsService settings)
    {
        _settings = settings;
    }

    [HttpGet("")]
    [FcmsAuthorize("settings.view")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var audit = await _settings.GetAsync<AuditEnabledDto>(AuditLogSettings.Key, ct: ct);
        ViewBag.AuditEnabled = audit.Enabled;
        return View();
    }

    // ── Audit log toggle ──────────────────────────────────────────────────────

    [HttpPost("audit/toggle")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize("settings.manage")]
    public async Task<IActionResult> ToggleAudit(CancellationToken ct)
    {
        var cfg = await _settings.GetAsync<AuditEnabledDto>(AuditLogSettings.Key, ct: ct);
        cfg.Enabled = !cfg.Enabled;
        await _settings.SaveAsync(AuditLogSettings.Key, cfg, ct);
        return FcmsOk(cfg.Enabled ? "Audit logging enabled." : "Audit logging disabled.", new { enabled = cfg.Enabled });
    }

    private sealed class AuditEnabledDto
    {
        public bool Enabled { get; set; } = true;
    }
}
