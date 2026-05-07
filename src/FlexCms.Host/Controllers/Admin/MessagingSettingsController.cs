using FlexCms.Framework.Auth;
using FlexCms.Framework.Messaging;
using FlexCms.Framework.Messaging.Services;
using FlexCms.Host.Models.Admin;
using Microsoft.AspNetCore.Mvc;

namespace FlexCms.Host.Controllers.Admin;

/// <summary>
/// Admin UI for SMTP + SMS gateway credentials. Both secret fields (SMTP
/// password, SMS API key) round-trip empty for the "keep existing" pattern;
/// the underlying settings services re-encrypt on rotation only.
/// </summary>
[Route("admin/messaging-settings")]
public class MessagingSettingsController : BaseAdminController
{
    private readonly ISmtpSettingsService _smtp;
    private readonly ISmsSettingsService _sms;
    private readonly IFcmsEmailService _email;
    private readonly IFcmsSmsSender _smsSender;

    public MessagingSettingsController(
        ISmtpSettingsService smtp,
        ISmsSettingsService sms,
        IFcmsEmailService email,
        IFcmsSmsSender smsSender)
    {
        _smtp = smtp;
        _sms = sms;
        _email = email;
        _smsSender = smsSender;
    }

    [HttpGet("")]
    [FcmsAuthorize(FcmsPermissions.SettingsView)]
    public async Task<IActionResult> Index(CancellationToken ct)
        => View(await BuildVmAsync(ct));

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize(FcmsPermissions.SettingsManage)]
    [FcmsLog("messaging-settings.save", "MessagingSettings")]
    public async Task<IActionResult> Index(MessagingSettingsViewModel vm, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(vm);

        await _smtp.SaveAsync(new SmtpSettings
        {
            Enabled = vm.SmtpEnabled,
            Host = vm.SmtpHost?.Trim() ?? "",
            Port = vm.SmtpPort,
            UseSsl = vm.SmtpUseSsl,
            Username = vm.SmtpUsername?.Trim() ?? "",
            FromAddress = vm.SmtpFromAddress?.Trim() ?? "",
            FromName = vm.SmtpFromName?.Trim() ?? ""
        }, vm.SmtpPassword, ct);

        await _sms.SaveAsync(new SmsSettings
        {
            Enabled = vm.SmsEnabled,
            Gateway = vm.SmsGateway ?? SmsGateways.Alpha,
            SenderId = vm.SmsSenderId?.Trim() ?? "",
            Username = vm.SmsUsername?.Trim() ?? "",
            EndpointOverride = vm.SmsEndpointOverride?.Trim() ?? "",
            BulkSmsType = vm.SmsBulkType
        }, vm.SmsApiKey, ct);

        ShowSuccess("Messaging settings saved.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("test-email")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize(FcmsPermissions.SettingsManage)]
    public async Task<IActionResult> TestEmail([FromForm] string to, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(to)) return FcmsFail("Recipient required.");
        var r = await _email.SendAsync(new EmailMessage(
            to,
            "FlexCms test email",
            "<p>This is a test email from FlexCms — if you received it, SMTP is configured correctly.</p>"), ct);
        return r.Success ? FcmsOk("Test email sent.") : FcmsFail($"SMTP failed: {r.Error}");
    }

    [HttpPost("test-sms")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize(FcmsPermissions.SettingsManage)]
    public async Task<IActionResult> TestSms([FromForm] string to, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(to)) return FcmsFail("Recipient required.");
        var r = await _smsSender.SendAsync(new SmsMessage(to, "FlexCms test SMS"), ct);
        return r.Success ? FcmsOk("Test SMS sent.") : FcmsFail($"SMS failed: {r.Error}");
    }

    private async Task<MessagingSettingsViewModel> BuildVmAsync(CancellationToken ct)
    {
        var smtp = await _smtp.GetAsync(ct);
        var sms = await _sms.GetAsync(ct);
        return new MessagingSettingsViewModel
        {
            SmtpEnabled = smtp.Enabled,
            SmtpHost = smtp.Host,
            SmtpPort = smtp.Port,
            SmtpUseSsl = smtp.UseSsl,
            SmtpUsername = smtp.Username,
            SmtpFromAddress = smtp.FromAddress,
            SmtpFromName = smtp.FromName,
            SmtpHasPassword = !string.IsNullOrEmpty(smtp.PasswordEncrypted),

            SmsEnabled = sms.Enabled,
            SmsGateway = string.IsNullOrWhiteSpace(sms.Gateway) ? SmsGateways.Alpha : sms.Gateway,
            SmsSenderId = sms.SenderId,
            SmsUsername = sms.Username,
            SmsEndpointOverride = sms.EndpointOverride,
            SmsBulkType = sms.BulkSmsType,
            SmsHasApiKey = !string.IsNullOrEmpty(sms.ApiKeyEncrypted)
        };
    }
}
