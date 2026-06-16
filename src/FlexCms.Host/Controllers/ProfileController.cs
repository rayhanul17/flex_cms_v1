using FlexCms.Framework.Auth;
using FlexCms.Framework.Auth.TwoFactor;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace FlexCms.Host.Controllers;

/// <summary>
/// Self-service profile management. Lives at <c>/profile</c> (Index = profile
/// fields, Password = change password) plus <c>/profile/security/two-factor</c>
/// for 2FA enrollment + recovery codes.
/// </summary>
[Authorize]
[Route("profile")]
public class ProfileController : Controller
{
    private readonly UserManager<FcmsUser> _userManager;
    private readonly IOtpChallengeService _otp;

    public ProfileController(UserManager<FcmsUser> userManager, IOtpChallengeService otp)
    {
        _userManager = userManager;
        _otp = otp;
    }


    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return RedirectToAction("Login", "Auth");

        ViewData["FullName"] = user.FullName;
        ViewData["DisplayName"] = user.DisplayName;
        ViewData["Email"] = user.Email;
        ViewData["PhoneNumber"] = user.PhoneNumber;
        ViewData["TwoFactorEnabled"] = user.TwoFactorEnabled;
        return View();
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(string fullName, string? displayName, string? phoneNumber)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return RedirectToAction("Login", "Auth");

        if (string.IsNullOrWhiteSpace(fullName))
        {
            TempData["Err"] = "Full name is required.";
            return RedirectToAction(nameof(Index));
        }

        user.FullName = fullName.Trim();
        user.DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
        user.PhoneNumber = string.IsNullOrWhiteSpace(phoneNumber) ? null : phoneNumber.Trim();
        var result = await _userManager.UpdateAsync(user);

        TempData[result.Succeeded ? "Ok" : "Err"] = result.Succeeded
            ? "Profile updated."
            : string.Join(", ", result.Errors.Select(e => e.Description));
        return RedirectToAction(nameof(Index));
    }


    [HttpGet("password")]
    public IActionResult Password() => View();

    [HttpPost("password")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(string currentPassword, string newPassword, string confirmPassword)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return RedirectToAction("Login", "Auth");

        if (newPassword != confirmPassword)
        {
            TempData["Err"] = "New password and confirmation do not match.";
            return RedirectToAction(nameof(Password));
        }

        var result = await _userManager.ChangePasswordAsync(user, currentPassword, newPassword);
        if (result.Succeeded)
        {
            TempData["Ok"] = "Password changed.";
            return RedirectToAction(nameof(Index));
        }

        TempData["Err"] = string.Join(", ", result.Errors.Select(e => e.Description));
        return RedirectToAction(nameof(Password));
    }


    [HttpGet("security/two-factor")]
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

    [HttpPost("security/two-factor/enable")]
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

        var codes = await _otp.RegenerateRecoveryCodesAsync(user);
        TempData["RecoveryCodes"] = string.Join("\n", codes);
        TempData["Ok"] = $"2FA enabled. Verification codes will be sent via {channel}.";
        return RedirectToAction(nameof(TwoFactor));
    }

    [HttpPost("security/two-factor/disable")]
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

    [HttpPost("security/two-factor/recovery-codes")]
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
