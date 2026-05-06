using System.Security.Claims;
using FlexCms.Framework.Auth;
using FlexCms.Framework.Auth.History;
using FlexCms.Framework.Messaging;
using FlexCms.Framework.Sessions;
using FlexCms.Host.Models.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace FlexCms.Host.Controllers;

public class AuthController : Controller
{
    private readonly UserManager<FcmsUser> _userManager;
    private readonly SignInManager<FcmsUser> _signInManager;
    private readonly RoleManager<FcmsRole> _roleManager;
    private readonly IFcmsBackgroundQueue _queue;
    private readonly ILoginHistoryService _history;
    private readonly ISessionService _sessions;

    public AuthController(
        UserManager<FcmsUser> userManager,
        SignInManager<FcmsUser> signInManager,
        RoleManager<FcmsRole> roleManager,
        IFcmsBackgroundQueue queue,
        ILoginHistoryService history,
        ISessionService sessions)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _roleManager = roleManager;
        _queue = queue;
        _history = history;
        _sessions = sessions;
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
    {
        if (!ModelState.IsValid) return View(model);

        var result = await _signInManager.PasswordSignInAsync(
            model.UserName, model.Password, model.RememberMe, lockoutOnFailure: true);

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
        var ua = Request.Headers.UserAgent.ToString();

        if (result.Succeeded)
        {
            var user = await _userManager.FindByNameAsync(model.UserName);
            await _history.RecordAsync(model.UserName, user?.Id, LoginOutcome.Success, ip, ua);

            if (user is not null)
            {
                // Issue a session id and re-sign-in with it as a claim so the
                // session-validation middleware can revoke this cookie later.
                var sessionId = Guid.NewGuid().ToString("N");
                var deviceLabel = ua.Length > 60 ? ua[..60] : ua;
                await _sessions.RecordLoginAsync(user.Id, sessionId, ip, ua, deviceLabel);

                await _signInManager.SignOutAsync();   // clear the cookie just issued without the claim
                await _signInManager.SignInWithClaimsAsync(user, model.RememberMe,
                    [new Claim(FcmsSessionValidationMiddleware.SessionIdClaim, sessionId)]);
            }

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);

            var redirectUrl = user is not null
                ? await ResolveLoginRedirectAsync(user)
                : "/";

            return LocalRedirect(redirectUrl);
        }

        if (result.IsLockedOut)
        {
            await _history.RecordAsync(model.UserName, null, LoginOutcome.LockedOut, ip, ua);
            return View("Lockout");
        }
        if (result.IsNotAllowed)
        {
            await _history.RecordAsync(model.UserName, null, LoginOutcome.NotAllowed, ip, ua, failReason: "Account not allowed (e.g. email unconfirmed).");
        }
        else
        {
            await _history.RecordAsync(model.UserName, null, LoginOutcome.InvalidCredentials, ip, ua);
        }

        ModelState.AddModelError(string.Empty, "Invalid login attempt.");
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        // Revoke the session row so the cookie can't be replayed even if it
        // somehow leaks before the SignOut response reaches the browser.
        var sessionId = User.FindFirstValue(FcmsSessionValidationMiddleware.SessionIdClaim);
        if (!string.IsNullOrEmpty(sessionId))
        {
            await _sessions.RevokeAsync(sessionId, revokedByUserId: null, reason: "logout");
        }

        await _signInManager.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }

    [HttpGet]
    public IActionResult ForgotPassword() => View();

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var user = await _userManager.FindByEmailAsync(model.Email);
        if (user is null || !await _userManager.IsEmailConfirmedAsync(user))
            return View("ForgotPasswordConfirmation");

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var resetUrl = Url.Action(nameof(ResetPassword), "Auth",
            new { token, userId = user.Id }, protocol: Request.Scheme) ?? "";

        var html = $"""
            <p>Hi {System.Net.WebUtility.HtmlEncode(user.UserName ?? "")},</p>
            <p>You (or someone using your email) requested a password reset.
            Click the link below to choose a new password — it expires shortly:</p>
            <p><a href="{resetUrl}">Reset my password</a></p>
            <p>If you didn't request this, you can safely ignore this email.</p>
            """;

        // Fire-and-forget through the in-memory queue. The processor opens its
        // own scope and resolves IFcmsEmailService — keeps this request fast.
        var to = user.Email ?? "";
        _queue.TryEnqueue(async (sp, ct) =>
        {
            var email = sp.GetRequiredService<IFcmsEmailService>();
            await email.SendAsync(new EmailMessage(to, "Reset your password", html), ct);
        });

        return View("ForgotPasswordConfirmation");
    }

    [HttpGet]
    public IActionResult ResetPassword(string? token = null, string? userId = null)
    {
        if (token is null || userId is null) return BadRequest();
        return View(new ResetPasswordViewModel { Token = token, UserId = userId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var user = await _userManager.FindByIdAsync(model.UserId);
        if (user is null) return View("ResetPasswordConfirmation");

        var result = await _userManager.ResetPasswordAsync(user, model.Token, model.NewPassword);
        if (result.Succeeded) return View("ResetPasswordConfirmation");

        foreach (var error in result.Errors)
            ModelState.AddModelError(string.Empty, error.Description);

        return View(model);
    }

    [HttpGet]
    public IActionResult VerifyOtp() => View();

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> VerifyOtp(VerifyOtpViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var user = await _userManager.FindByIdAsync(model.UserId);
        if (user is null)
        {
            ModelState.AddModelError(string.Empty, "Invalid OTP.");
            return View(model);
        }

        var valid = await _userManager.VerifyTwoFactorTokenAsync(
            user, TokenOptions.DefaultPhoneProvider, model.Otp);
        if (!valid)
        {
            ModelState.AddModelError(string.Empty, "Invalid or expired OTP.");
            return View(model);
        }

        await _signInManager.SignInAsync(user, isPersistent: false);
        return RedirectToAction("Index", "Home");
    }

    [HttpGet("auth/change-password")]
    [HttpGet("Auth/ChangePassword")]
    [Authorize]
    public IActionResult ChangePassword() => View();

    [HttpPost("auth/change-password")]
    [HttpPost("Auth/ChangePassword")]
    [ValidateAntiForgeryToken]
    [Authorize]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var user = await _userManager.GetUserAsync(User);
        if (user is null) return RedirectToAction(nameof(Login));

        var result = await _userManager.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);
        if (result.Succeeded)
        {
            user.ForcePasswordChange = false;
            await _userManager.UpdateAsync(user);
            await _signInManager.RefreshSignInAsync(user);
            return RedirectToAction("Index", "Home");
        }

        foreach (var error in result.Errors)
            ModelState.AddModelError(string.Empty, error.Description);

        return View(model);
    }

    [HttpGet]
    public IActionResult AccessDenied() => View();

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// SuperAdmin → /admin always.
    /// Other roles: pick highest-Priority role's LoginRedirectUrl (fallback "/").
    /// returnUrl takes precedence over role redirect (checked before this is called).
    /// </summary>
    private async Task<string> ResolveLoginRedirectAsync(FcmsUser user)
    {
        var roleNames = await _userManager.GetRolesAsync(user);

        if (roleNames.Contains(FcmsRoles.SuperAdmin))
            return "/admin";

        if (roleNames.Count == 0)
            return "/";

        FcmsRole? bestRole = null;
        foreach (var name in roleNames)
        {
            var role = await _roleManager.FindByNameAsync(name);
            if (role is null) continue;
            if (bestRole is null || role.Priority > bestRole.Priority)
                bestRole = role;
        }

        var url = bestRole?.LoginRedirectUrl;
        return string.IsNullOrWhiteSpace(url) ? "/" : url;
    }
}
