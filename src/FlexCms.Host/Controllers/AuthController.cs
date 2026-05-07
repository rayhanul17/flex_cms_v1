using System.Security.Claims;
using FlexCms.Framework.Auth;
using FlexCms.Framework.Auth.History;
using FlexCms.Framework.Auth.TwoFactor;
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
    private readonly IOtpChallengeService _otp;

    public AuthController(
        UserManager<FcmsUser> userManager,
        SignInManager<FcmsUser> signInManager,
        RoleManager<FcmsRole> roleManager,
        IFcmsBackgroundQueue queue,
        ILoginHistoryService history,
        ISessionService sessions,
        IOtpChallengeService otp)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _roleManager = roleManager;
        _queue = queue;
        _history = history;
        _sessions = sessions;
        _otp = otp;
    }

    [HttpGet]
    public async Task<IActionResult> Login(string? returnUrl = null)
    {
        // Sign out any lingering auth state on GET so the antiforgery token
        // we issue for this page is bound to an anonymous identity. Without
        // this, hitting /auth/login while already signed in (stale cookie,
        // duplicate tab, or after an expired session not yet evicted) issues
        // a token claim-bound to the OLD user, then the POST runs while the
        // cookie has changed mid-flow → "antiforgery token meant for a
        // different claims-based user" 400.
        if (User?.Identity?.IsAuthenticated == true)
        {
            var sessionId = User.FindFirstValue(FcmsSessionValidationMiddleware.SessionIdClaim);
            if (!string.IsNullOrEmpty(sessionId))
            {
                await _sessions.RevokeAsync(sessionId, revokedByUserId: null, reason: "login-page-revisit");
            }
            await _signInManager.SignOutAsync();
        }
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

            // 2FA gate: if user has a channel set, password alone is NOT
            // enough — undo the cookie just issued and stash a pending-2FA
            // marker so /auth/two-factor can complete the login.
            if (user is not null && user.TwoFactorEnabled && user.TwoFactorChannel != TwoFactorChannel.Disabled)
            {
                await _signInManager.SignOutAsync();
                StashPendingTwoFactor(user.Id, model.RememberMe, returnUrl);

                var issue = await _otp.IssueAsync(user);
                if (!issue.Success)
                {
                    // Failed to send — bail back to login with the error.
                    // Bonus: don't record this as Success since the user
                    // hasn't actually completed login.
                    ModelState.AddModelError(string.Empty, issue.Error ?? "Could not deliver verification code.");
                    return View(model);
                }

                return RedirectToAction(nameof(TwoFactorVerify), new { masked = issue.MaskedDestination });
            }

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

    // ── 2FA verify (post-password challenge) ─────────────────────────────────

    public const string PendingTwoFactorCookie = "fcms.pending2fa";

    [HttpGet("auth/two-factor")]
    public IActionResult TwoFactorVerify(string? masked = null)
    {
        var pending = ReadPendingTwoFactor();
        if (pending is null) return RedirectToAction(nameof(Login));
        ViewData["Masked"] = masked ?? "";
        return View(new TwoFactorVerifyViewModel());
    }

    [HttpPost("auth/two-factor")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TwoFactorVerify(TwoFactorVerifyViewModel model)
    {
        var pending = ReadPendingTwoFactor();
        if (pending is null) return RedirectToAction(nameof(Login));
        if (!ModelState.IsValid) return View(model);

        var user = await _userManager.FindByIdAsync(pending.UserId.ToString());
        if (user is null)
        {
            ClearPendingTwoFactor();
            return RedirectToAction(nameof(Login));
        }

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
        var ua = Request.Headers.UserAgent.ToString();

        // Recovery code path: any value with a dash is treated as a recovery
        // code attempt (10-char alphanumeric formatted as XXXXX-XXXXX). Plain
        // 6-digit input goes through the OTP path.
        bool ok;
        if (model.Code?.Contains('-') == true)
        {
            ok = await _otp.VerifyRecoveryCodeAsync(user, model.Code);
            if (!ok)
            {
                ModelState.AddModelError(string.Empty, "Invalid recovery code.");
                await _history.RecordAsync(user.UserName ?? "", user.Id, LoginOutcome.InvalidCredentials, ip, ua, failReason: "2FA recovery code invalid.");
                return View(model);
            }
        }
        else
        {
            var v = await _otp.VerifyAsync(user, model.Code ?? "");
            if (v != OtpVerifyResult.Ok)
            {
                ModelState.AddModelError(string.Empty, v switch
                {
                    OtpVerifyResult.Expired => "Code expired — request a new one.",
                    OtpVerifyResult.TooManyAttempts => "Too many wrong attempts — request a new code.",
                    OtpVerifyResult.NoPending => "No active code — request a new one.",
                    _ => "Invalid code.",
                });
                await _history.RecordAsync(user.UserName ?? "", user.Id, LoginOutcome.InvalidCredentials, ip, ua, failReason: $"2FA OTP {v}.");
                return View(model);
            }
            ok = true;
        }

        if (!ok) return View(model);

        // 2FA passed — complete the login that the password-only step held.
        var sessionId = Guid.NewGuid().ToString("N");
        var deviceLabel = ua.Length > 60 ? ua[..60] : ua;
        await _sessions.RecordLoginAsync(user.Id, sessionId, ip, ua, deviceLabel);
        await _signInManager.SignInWithClaimsAsync(user, pending.RememberMe,
            [new Claim(FcmsSessionValidationMiddleware.SessionIdClaim, sessionId)]);
        await _history.RecordAsync(user.UserName ?? "", user.Id, LoginOutcome.Success, ip, ua, failReason: "2FA verified.");
        ClearPendingTwoFactor();

        if (!string.IsNullOrEmpty(pending.ReturnUrl) && Url.IsLocalUrl(pending.ReturnUrl))
            return Redirect(pending.ReturnUrl);
        return LocalRedirect(await ResolveLoginRedirectAsync(user));
    }

    [HttpPost("auth/two-factor/resend")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TwoFactorResend()
    {
        var pending = ReadPendingTwoFactor();
        if (pending is null) return RedirectToAction(nameof(Login));
        var user = await _userManager.FindByIdAsync(pending.UserId.ToString());
        if (user is null) { ClearPendingTwoFactor(); return RedirectToAction(nameof(Login)); }

        var issue = await _otp.IssueAsync(user);
        return RedirectToAction(nameof(TwoFactorVerify), new { masked = issue.MaskedDestination });
    }

    private void StashPendingTwoFactor(Guid userId, bool rememberMe, string? returnUrl)
    {
        // Short-lived (10 min) HttpOnly + SameSite=Strict cookie carrying
        // the bare facts we need to complete the login. We deliberately
        // do NOT use Session for this — Session can outlive a browser
        // restart in some configurations; this stash should not.
        var payload = $"{userId:N}|{(rememberMe ? "1" : "0")}|{Uri.EscapeDataString(returnUrl ?? "")}";
        var bytes = System.Text.Encoding.UTF8.GetBytes(payload);
        Response.Cookies.Append(PendingTwoFactorCookie, Convert.ToBase64String(bytes), new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Expires = DateTimeOffset.UtcNow.AddMinutes(10),
            Path = "/auth/two-factor",
        });
    }

    private PendingTwoFactor? ReadPendingTwoFactor()
    {
        if (!Request.Cookies.TryGetValue(PendingTwoFactorCookie, out var raw) || string.IsNullOrEmpty(raw))
            return null;
        try
        {
            var payload = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(raw));
            var parts = payload.Split('|', 3);
            if (parts.Length < 2 || !Guid.TryParse(parts[0], out var uid)) return null;
            return new PendingTwoFactor(uid, parts[1] == "1",
                parts.Length >= 3 ? Uri.UnescapeDataString(parts[2]) : "");
        }
        catch
        {
            return null;
        }
    }

    private void ClearPendingTwoFactor()
        => Response.Cookies.Delete(PendingTwoFactorCookie, new CookieOptions { Path = "/auth/two-factor" });

    private sealed record PendingTwoFactor(Guid UserId, bool RememberMe, string ReturnUrl);

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
