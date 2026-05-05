using FlexCms.Framework.Auth;
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

    public AuthController(
        UserManager<FcmsUser> userManager,
        SignInManager<FcmsUser> signInManager,
        RoleManager<FcmsRole> roleManager)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _roleManager = roleManager;
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

        if (result.Succeeded)
        {
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);

            var user = await _userManager.FindByNameAsync(model.UserName);
            var redirectUrl = user is not null
                ? await ResolveLoginRedirectAsync(user)
                : "/";

            return LocalRedirect(redirectUrl);
        }

        if (result.IsLockedOut)
            return View("Lockout");

        ModelState.AddModelError(string.Empty, "Invalid login attempt.");
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
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
        TempData["ResetToken"] = token;
        TempData["UserId"] = user.Id.ToString();
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

    [HttpGet]
    [Authorize]
    public IActionResult ChangePassword() => View();

    [HttpPost]
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
