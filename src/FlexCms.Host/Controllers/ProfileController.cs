using FlexCms.Framework.Auth;
using FlexCms.Framework.Auth.TwoFactor;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace FlexCms.Host.Controllers;

/// <summary>
/// Self-service profile actions — initially scoped to 2FA enrollment +
/// recovery-code regeneration. Public-facing route prefix
/// <c>/profile/security</c>; views live under <c>/Views/Profile/</c>.
/// </summary>
[Authorize]
[Route("profile/security")]
public class ProfileController : Controller
{
    private readonly UserManager<FcmsUser> _userManager;
    private readonly IOtpChallengeService _otp;

    public ProfileController(UserManager<FcmsUser> userManager, IOtpChallengeService otp)
    {
        _userManager = userManager;
        _otp = otp;
    }

    [HttpGet("two-factor")]
    public async Task<IActionResult> TwoFactor()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return RedirectToAction("Login", "Auth");

        ViewData["Enabled"] = user.TwoFactorEnabled;
        ViewData["Channel"] = user.TwoFactorChannel;
        ViewData["RecoveryCount"] = await _otp.CountUnusedRecoveryCodesAsync(user.Id);
        ViewData["HasEmail"] = !string.IsNullOrEmpty(user.Email);
        ViewData["HasPhone"] = !string.IsNullOrEmpty(user.PhoneNumber);
        return View();
    }

    [HttpPost("two-factor/enable")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Enable([FromForm] TwoFactorChannel channel)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return RedirectToAction("Login", "Auth");
        if (channel == TwoFactorChannel.Disabled)
        {
            TempData["Err"] = "Pick Email or SMS.";
            return RedirectToAction(nameof(TwoFactor));
        }
        if (channel == TwoFactorChannel.Email && string.IsNullOrEmpty(user.Email))
        {
            TempData["Err"] = "Set an email address before enabling email 2FA.";
            return RedirectToAction(nameof(TwoFactor));
        }
        if (channel == TwoFactorChannel.Sms && string.IsNullOrEmpty(user.PhoneNumber))
        {
            TempData["Err"] = "Set a phone number before enabling SMS 2FA.";
            return RedirectToAction(nameof(TwoFactor));
        }

        user.TwoFactorChannel = channel;
        user.TwoFactorEnabled = true;
        await _userManager.UpdateAsync(user);

        // Issue the recovery codes immediately + show ONCE — caller must
        // print/save them; we won't be able to re-display later.
        var codes = await _otp.RegenerateRecoveryCodesAsync(user);
        TempData["RecoveryCodes"] = string.Join("\n", codes);
        TempData["Ok"] = $"2FA enabled. Verification codes will be sent via {channel}.";
        return RedirectToAction(nameof(TwoFactor));
    }

    [HttpPost("two-factor/disable")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Disable()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return RedirectToAction("Login", "Auth");
        user.TwoFactorEnabled = false;
        user.TwoFactorChannel = TwoFactorChannel.Disabled;
        user.PendingOtpHash = null;
        user.PendingOtpExpiresAt = null;
        await _userManager.UpdateAsync(user);
        TempData["Ok"] = "2FA disabled.";
        return RedirectToAction(nameof(TwoFactor));
    }

    [HttpPost("two-factor/recovery-codes")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RegenerateRecoveryCodes()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return RedirectToAction("Login", "Auth");
        var codes = await _otp.RegenerateRecoveryCodesAsync(user);
        TempData["RecoveryCodes"] = string.Join("\n", codes);
        TempData["Ok"] = "New recovery codes generated. Save them now — the previous batch is invalid.";
        return RedirectToAction(nameof(TwoFactor));
    }
}
