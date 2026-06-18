using System.Security.Claims;
using System.Text.Json;
using FlexCms.Framework.Auth;
using FlexCms.Framework.Auth.History;
using FlexCms.Framework.Auth.TwoFactor;
using FlexCms.Framework.Messaging;
using FlexCms.Framework.Sessions;
using FlexCms.Host.Models.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
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
    private readonly IDataProtector _pending2FaProtector;

    public AuthController(
        UserManager<FcmsUser> userManager,
        SignInManager<FcmsUser> signInManager,
        RoleManager<FcmsRole> roleManager,
        IFcmsBackgroundQueue queue,
        ILoginHistoryService history,
        ISessionService sessions,
        IOtpChallengeService otp,
        IDataProtectionProvider dataProtection)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _roleManager = roleManager;
        _queue = queue;
        _history = history;
        _sessions = sessions;
        _otp = otp;
        _pending2FaProtector = dataProtection.CreateProtector("FlexCms.Auth.PendingTwoFactor.v1");
    }

    [HttpGet]
    public async Task<IActionResult> Login(string? returnUrl = null)
    {
        // Bypass the form for already-authenticated users — rendering it against a
        // non-anonymous identity led to antiforgery 400s when cookie state shifted
        // between GET and POST ("token meant for a different claims-based user").
        if (User?.Identity?.IsAuthenticated == true)
        {
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);
            var user = await _userManager.GetUserAsync(User);
            var landing = user is not null ? await ResolveLoginRedirectAsync(user) : "/";
            return LocalRedirect(landing);
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

            // 2FA gate — undo the cookie + stash a pending marker for /auth/two-factor.
            if (user is not null && user.TwoFactorEnabled && user.TwoFactorChannel != TwoFactorChannel.Disabled)
            {
                await _signInManager.SignOutAsync();
                StashPendingTwoFactor(user.Id, model.RememberMe, returnUrl);

                var issue = await _otp.IssueAsync(user);
                if (!issue.Success)
                {
                    ModelState.AddModelError(string.Empty, issue.Error ?? "Could not deliver verification code.");
                    return View(model);
                }

                return RedirectToAction(nameof(TwoFactorVerify), new { masked = issue.MaskedDestination });
            }

            await _history.RecordAsync(model.UserName, user?.Id, LoginOutcome.Success, ip, ua);

            if (user is not null)
            {
                // Re-sign-in with a session_id claim so SessionValidationMiddleware can revoke later.
                var sessionId = Guid.NewGuid().ToString("N");
                var deviceLabel = ua.Length > 60 ? ua[..60] : ua;
                await _sessions.RecordLoginAsync(user.Id, sessionId, ip, ua, deviceLabel);

                await _signInManager.SignOutAsync();
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
        // Revoke the session row so a leaked cookie can't be replayed.
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

        // Fire-and-forget through the in-memory queue (resolves IFcmsEmailService in its own scope).
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

    // Legacy /VerifyOtp endpoint removed — it signed in via UserId + OTP with
    // no prior password validation, no protected pending-challenge cookie,
    // and no fcms.session_id claim (so session revocation couldn't enforce
    // the resulting login). The modern 2FA flow at /auth/two-factor is the
    // only supported path.

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

        // Dashed input (XXXXX-XXXXX) routes to recovery codes; plain digits to OTP.
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
        // Short-lived HttpOnly cookie (10 min). Not Session: Session can outlive
        // a browser restart. The payload is JSON, then ASP.NET DataProtection
        // signed+encrypted so a client can't tamper with userId / rememberMe /
        // returnUrl. Previously Base64-only — see security-audit-fix-plan §1.2.
        var state = new PendingTwoFactorState
        {
            UserId = userId,
            RememberMe = rememberMe,
            ReturnUrl = returnUrl ?? "",
            IssuedAtUtc = DateTimeOffset.UtcNow,
        };
        var json = JsonSerializer.Serialize(state);
        var protectedValue = _pending2FaProtector.Protect(json);

        Response.Cookies.Append(PendingTwoFactorCookie, protectedValue, new CookieOptions
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
            var json = _pending2FaProtector.Unprotect(raw);
            var state = JsonSerializer.Deserialize<PendingTwoFactorState>(json);
            if (state is null) return null;

            // 10-minute hard ceiling matches the cookie's Expires header; we
            // also enforce it server-side because cookie expiration is a
            // client-side hint a determined attacker can simply ignore.
            if (DateTimeOffset.UtcNow - state.IssuedAtUtc > TimeSpan.FromMinutes(10))
                return null;

            return new PendingTwoFactor(state.UserId, state.RememberMe, state.ReturnUrl ?? "");
        }
        catch
        {
            // Tampered / corrupt / replayed-from-an-old-key cookie: treat as no
            // pending challenge and bounce the caller back to /auth/login.
            return null;
        }
    }

    private void ClearPendingTwoFactor()
        => Response.Cookies.Delete(PendingTwoFactorCookie, new CookieOptions { Path = "/auth/two-factor" });

    private sealed record PendingTwoFactor(Guid UserId, bool RememberMe, string ReturnUrl);

    private sealed class PendingTwoFactorState
    {
        public Guid UserId { get; set; }
        public bool RememberMe { get; set; }
        public string ReturnUrl { get; set; } = "";
        public DateTimeOffset IssuedAtUtc { get; set; }
    }

    /// <summary>SuperAdmin → /admin; else highest-Priority role's LoginRedirectUrl (fallback "/").</summary>
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
