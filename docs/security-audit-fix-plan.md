# FlexCms v1 Security Audit & Fix Plan

> **For Claude Agent:** Implement this plan task-by-task. Keep changes small, run tests/build after each major area, and do not refactor unrelated code. Treat this document as the source of truth for the security hardening work.

**Project:** `D:\flex_cms_v1` / WSL path `/mnt/d/flex_cms_v1`  
**Audit focus:** Runtime module DLL upload/load, multi-DB support, authentication, authorization, API tokens, 2FA, audit logging, module static files, and admin-controller security.  
**Tech stack:** ASP.NET Core MVC, Identity, EF Core, MySQL/MSSQL/PostgreSQL providers, runtime module loading via DLLs.

---

## Executive Summary

FlexCms has a good foundation, but because it supports **runtime DLL module upload and loading**, the security boundary is much more sensitive than a normal CMS. A module is effectively trusted code running inside the host process.

High-priority issues to fix first:

1. Remove or secure legacy OTP login endpoint.
2. Fix API token authentication scheme so Bearer tokens work intentionally and predictably.
3. Enforce API token scopes so tokens cannot inherit full user permissions accidentally.
4. Protect 2FA pending cookie with `IDataProtector`; do not use raw Base64 as trust data.
5. Add production-safe module package integrity checks: hash/signature/approval gate.
6. Add missing rate limits for 2FA endpoints.
7. Tighten granular permissions on sensitive admin views.
8. Strengthen audit-log governance and tamper resistance.

---

## Important Existing Files

### Auth / AuthZ

- `src/FlexCms.Host/Controllers/AuthController.cs`
- `src/FlexCms.Framework/Auth/FcmsAuthorizeAttribute.cs`
- `src/FlexCms.Framework/Auth/FcmsPermissions.cs`
- `src/FlexCms.Framework/Services/PermissionService.cs`
- `src/FlexCms.Framework/Services/PermissionExpression.cs`
- `src/FlexCms.Framework/Sessions/FcmsSessionValidationMiddleware.cs`
- `src/FlexCms.Framework/Api/FcmsApiTokenAuthenticationHandler.cs`
- `src/FlexCms.Framework/Api/ApiTokenService.cs`
- `src/FlexCms.Framework/Extensions/FcmsServiceExtensions.cs`

### Audit

- `src/FlexCms.Framework/Cms/FcmsAuditInterceptor.cs`
- `src/FlexCms.Framework/Cms/FcmsLogService.cs`
- `src/FlexCms.Framework/Cms/FcmsLogJsonResolver.cs`
- `src/FlexCms.Host/Controllers/Admin/AuditLogController.cs`
- `src/FlexCms.Host/Controllers/Admin/SettingsController.cs`

### Modules

- `src/FlexCms.Framework/Modules/ModuleLoader.cs`
- `src/FlexCms.Framework/Modules/ModuleManager.cs`
- `src/FlexCms.Framework/Modules/ModuleActivationService.cs`
- `src/FlexCms.Framework/Modules/ModuleManifest.cs`
- `src/FlexCms.Framework/Modules/ModulePermissionSeeder.cs`
- `src/FlexCms.Framework/Modules/Updates/ModuleUpdateService.cs`
- `src/FlexCms.Framework/Extensions/FcmsModuleStaticFilesExtensions.cs`
- `src/FlexCms.Host/Controllers/Admin/ModulesController.cs`

### Admin controllers to review

- `src/FlexCms.Host/Controllers/Admin/BaseAdminController.cs`
- `src/FlexCms.Host/Controllers/Admin/UserController.cs`
- `src/FlexCms.Host/Controllers/Admin/RoleController.cs`
- `src/FlexCms.Host/Controllers/Admin/PermissionController.cs`
- `src/FlexCms.Host/Controllers/Admin/ModulesController.cs`
- `src/FlexCms.Host/Controllers/Admin/SettingsController.cs`
- `src/FlexCms.Host/Controllers/Admin/AuditLogController.cs`

---

# Phase 1 — Critical Authentication Fixes

## Task 1.1: Remove or secure legacy OTP login endpoint

**Severity:** High  
**Objective:** Prevent login via `userId + OTP` without a prior password-authenticated pending challenge.

### Problem

`AuthController` has a legacy OTP flow:

- `GET VerifyOtp()`
- `POST VerifyOtp(VerifyOtpViewModel model)`

The POST action finds a user by `model.UserId`, verifies a token, and then signs in:

```csharp
await _signInManager.SignInAsync(user, isPersistent: false);
```

This is dangerous because:

- It does not require the password step.
- It does not require a protected pending challenge.
- It signs in without the `fcms.session_id` claim.
- Session revocation middleware cannot enforce that login session.

### Files

- Modify: `src/FlexCms.Host/Controllers/AuthController.cs`
- Search references: `VerifyOtpViewModel`, `/auth/verify-otp`, `VerifyOtp`
- Tests: add/update auth controller tests if an auth test project exists.

### Implementation

Preferred fix: **delete or disable the legacy `VerifyOtp` endpoint** if it is unused.

Remove or return `NotFound()` from:

```csharp
[HttpGet]
public IActionResult VerifyOtp() => View();

[HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> VerifyOtp(VerifyOtpViewModel model)
```

If the endpoint must remain for compatibility:

1. Require a protected pending challenge generated only after password validation.
2. Use the same session-record logic used by normal login:

```csharp
var sessionId = Guid.NewGuid().ToString("N");
var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
var ua = Request.Headers.UserAgent.ToString();
var deviceLabel = ua.Length > 60 ? ua[..60] : ua;

await _sessions.RecordLoginAsync(user.Id, sessionId, ip, ua, deviceLabel);
await _signInManager.SignInWithClaimsAsync(user, isPersistent: false,
    [new Claim(FcmsSessionValidationMiddleware.SessionIdClaim, sessionId)]);
```

3. Add audit logs for success/failure.
4. Add rate limiting for this path.

### Verification

Run:

```bash
dotnet test FlexCms.slnx
```

Manual checks:

- `/VerifyOtp` should no longer provide a passwordless login route.
- Normal `/auth/two-factor` still works.
- Normal login still creates a `fcms.session_id` claim.

---

## Task 1.2: Protect 2FA pending cookie with IDataProtector

**Severity:** High/Medium  
**Objective:** Stop trusting raw Base64 cookie payload for pending 2FA state.

### Problem

Current code in `AuthController.cs`:

```csharp
var payload = $"{userId:N}|{(rememberMe ? "1" : "0")}|{Uri.EscapeDataString(returnUrl ?? "")}";
var bytes = System.Text.Encoding.UTF8.GetBytes(payload);
Response.Cookies.Append(PendingTwoFactorCookie, Convert.ToBase64String(bytes), ...);
```

Then it decodes without cryptographic validation.

This lets a client tamper with:

- `userId`
- `rememberMe`
- `returnUrl`

OTP is still required, so it is not a direct bypass, but it enables abuse such as arbitrary-user resend attempts.

### Files

- Modify: `src/FlexCms.Host/Controllers/AuthController.cs`
- Possibly add: model/record for protected pending 2FA state.

### Implementation

Inject `IDataProtectionProvider` and create a protector:

```csharp
using Microsoft.AspNetCore.DataProtection;
```

Add field:

```csharp
private readonly IDataProtector _pending2FaProtector;
```

Constructor:

```csharp
public AuthController(..., IDataProtectionProvider dataProtection)
{
    ...
    _pending2FaProtector = dataProtection.CreateProtector("FlexCms.Auth.PendingTwoFactor.v1");
}
```

Replace `StashPendingTwoFactor` with protected JSON:

```csharp
private void StashPendingTwoFactor(Guid userId, bool rememberMe, string? returnUrl)
{
    var state = new PendingTwoFactorState
    {
        UserId = userId,
        RememberMe = rememberMe,
        ReturnUrl = returnUrl ?? "",
        IssuedAtUtc = DateTimeOffset.UtcNow
    };

    var json = System.Text.Json.JsonSerializer.Serialize(state);
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
```

Replace `ReadPendingTwoFactor`:

```csharp
private PendingTwoFactor? ReadPendingTwoFactor()
{
    if (!Request.Cookies.TryGetValue(PendingTwoFactorCookie, out var raw) || string.IsNullOrEmpty(raw))
        return null;

    try
    {
        var json = _pending2FaProtector.Unprotect(raw);
        var state = System.Text.Json.JsonSerializer.Deserialize<PendingTwoFactorState>(json);
        if (state is null) return null;

        if (DateTimeOffset.UtcNow - state.IssuedAtUtc > TimeSpan.FromMinutes(10))
            return null;

        return new PendingTwoFactor(state.UserId, state.RememberMe, state.ReturnUrl ?? "");
    }
    catch
    {
        return null;
    }
}

private sealed class PendingTwoFactorState
{
    public Guid UserId { get; set; }
    public bool RememberMe { get; set; }
    public string ReturnUrl { get; set; } = "";
    public DateTimeOffset IssuedAtUtc { get; set; }
}
```

### Verification

- Tampering the cookie should force redirect to login.
- Valid 2FA flow should still complete login.
- Expired pending cookie should redirect to login.

---

## Task 1.3: Add rate limits for 2FA endpoints

**Severity:** Medium  
**Objective:** Prevent OTP brute force and resend abuse.

### Current rate limit location

`src/FlexCms.Framework/Extensions/FcmsServiceExtensions.cs`, global limiter block around lines 331-410.

### Missing paths

Add limits for:

- `POST /auth/two-factor`
- `POST /auth/two-factor/resend`

### Implementation

In `services.AddRateLimiter(...)`, extend the OTP policy:

```csharp
if (ctx.Request.Path.StartsWithSegments("/auth/forgot-password") ||
    ctx.Request.Path.StartsWithSegments("/auth/verify-otp") ||
    ctx.Request.Path.StartsWithSegments("/auth/reset-password") ||
    ctx.Request.Path.StartsWithSegments("/auth/two-factor"))
{
    return RateLimitPartition.GetFixedWindowLimiter($"otp:{ip}", _ =>
        new FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1),
            PermitLimit = 5,
            AutoReplenishment = true
        });
}
```

If possible, use a stricter separate resend policy:

```csharp
if (ctx.Request.Method == "POST" &&
    ctx.Request.Path.StartsWithSegments("/auth/two-factor/resend"))
{
    return RateLimitPartition.GetFixedWindowLimiter($"otp-resend:{ip}", _ =>
        new FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(5),
            PermitLimit = 3,
            AutoReplenishment = true
        });
}
```

Place the more specific resend rule before the general `/auth/two-factor` rule.

### Verification

- Repeated 2FA POST attempts get HTTP 429.
- Repeated resend attempts get HTTP 429.
- Normal login and 2FA still work under the limit.

---

# Phase 2 — API Token Authentication & Authorization

## Task 2.1: Fix cookie-or-bearer authentication scheme

**Severity:** High  
**Objective:** Make Bearer token authentication work intentionally for `[Authorize]` routes.

### Problem

Current code registers a custom Bearer scheme and later sets Identity cookie as default:

```csharp
services.AddAuthentication()
    .AddScheme<FcmsApiTokenAuthenticationOptions, FcmsApiTokenAuthenticationHandler>(...);

services.AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddCookie(...);
```

This means default `[Authorize]` likely uses the cookie scheme only, not the Bearer token handler.

### File

- Modify: `src/FlexCms.Framework/Extensions/FcmsServiceExtensions.cs`

### Implementation

Replace the two separate `AddAuthentication` calls with a single policy-scheme setup.

Recommended shape:

```csharp
public const string SmartAuthScheme = "FlexCmsSmart";
```

Then:

```csharp
services.AddAuthentication(options =>
{
    options.DefaultScheme = SmartAuthScheme;
    options.DefaultAuthenticateScheme = SmartAuthScheme;
    options.DefaultChallengeScheme = SmartAuthScheme;
})
.AddPolicyScheme(SmartAuthScheme, "Cookie or API Token", options =>
{
    options.ForwardDefaultSelector = ctx =>
    {
        var auth = ctx.Request.Headers.Authorization.ToString();
        return auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? FcmsApiTokenAuthenticationHandler.SchemeName
            : IdentityConstants.ApplicationScheme;
    };
})
.AddScheme<FcmsApiTokenAuthenticationOptions, FcmsApiTokenAuthenticationHandler>(
    FcmsApiTokenAuthenticationHandler.SchemeName, _ => { })
.AddCookie(IdentityConstants.ApplicationScheme, opts =>
{
    opts.Cookie.HttpOnly = true;
    opts.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    opts.Cookie.SameSite = SameSiteMode.Strict;
    opts.SlidingExpiration = true;
    opts.ExpireTimeSpan = TimeSpan.FromHours(8);
    opts.LoginPath = "/auth/login";
    opts.LogoutPath = "/auth/logout";
    opts.AccessDeniedPath = "/Home/Error/403";
});
```

Remove the earlier standalone API token `AddAuthentication()` call.

### Verification

Add/adjust tests if available:

- Request with valid cookie authenticates.
- Request with valid `Authorization: Bearer fcms_...` authenticates.
- Request with invalid bearer token returns unauthorized.
- Browser challenge still redirects to login.

Run:

```bash
dotnet test FlexCms.slnx
```

---

## Task 2.2: Enforce API token scopes in permission checks

**Severity:** High  
**Objective:** API tokens should not inherit full user permissions unless scopes allow them.

### Problem

`FcmsApiTokenAuthenticationHandler` adds token scopes as claims:

```csharp
claims.Add(new Claim("fcms.api_scope", scope));
```

But `PermissionService.HasPermissionAsync` only evaluates role permissions and ignores `fcms.api_scope`.

### Files

- Modify: `src/FlexCms.Framework/Services/PermissionService.cs`
- Possibly add constants to API token auth classes.

### Implementation

Define claim constants:

```csharp
public const string ApiTokenIdClaim = "fcms.api_token_id";
public const string ApiScopeClaim = "fcms.api_scope";
```

In `PermissionService.HasPermissionAsync`, after `userPerms` are built:

```csharp
var roleAllowed = PermissionExpression.Evaluate(permissionExpr, userPerms);

var apiScopes = user.FindAll("fcms.api_scope")
    .Select(c => c.Value)
    .ToHashSet(StringComparer.OrdinalIgnoreCase);

if (apiScopes.Count > 0)
{
    var scopeAllowed = PermissionExpression.Evaluate(permissionExpr, apiScopes);
    return roleAllowed && scopeAllowed;
}

return roleAllowed;
```

Important: SuperAdmin bypass should not bypass token scopes. Modify this line:

```csharp
if (user.IsInRole(FcmsRoles.SuperAdmin)) return true;
```

to:

```csharp
var isApiToken = user.HasClaim(c => c.Type == "fcms.api_token_id");
if (!isApiToken && user.IsInRole(FcmsRoles.SuperAdmin)) return true;
```

Then after scopes are loaded, SuperAdmin token still needs matching scope.

### Scope convention

For now, use exact permission keys as scopes:

- `pages.create`
- `pages.edit`
- `media.upload`
- `system.manage`

Later a broader mapping can be added, but do not invent wildcard behavior unless tests cover it.

### Verification

Create tests:

- User role has `pages.edit`, token scope has `pages.edit` → allowed.
- User role has `pages.edit`, token scope lacks `pages.edit` → denied.
- SuperAdmin token lacks `system.manage` → denied.
- SuperAdmin cookie → still allowed.

---

# Phase 3 — Admin Authorization Hardening

## Task 3.1: Add missing view/manage permissions

**Severity:** Medium  
**Objective:** Sensitive admin pages should not be accessible just because a user is logged in.

### Existing issue

`BaseAdminController` has `[FcmsAuthorize]`, so all admin controllers require login. But many list/detail pages lack granular permission.

Examples:

- `UserController.Index` has no `UsersManage` / `UsersView` permission.
- `RoleController.Index` has no `RolesManage` / `RolesView` permission.
- `RoleController.Detail` has no view/manage permission.
- Content list/DataTable endpoints often lack explicit view permissions.

### Files

- Modify: `src/FlexCms.Framework/Auth/FcmsPermissions.cs`
- Modify controllers under `src/FlexCms.Host/Controllers/Admin/`
- Update seed logic if core permission seeding is explicit in `SeedService`.

### Implementation

Add new permissions to `FcmsPermissions`:

```csharp
public const string PagesView = "pages.view";
public const string PostsView = "posts.view";
public const string CategoriesView = "categories.view";
public const string UsersView = "users.view";
public const string RolesView = "roles.view";
public const string MediaFoldersView = "media.folders.view";
```

Then annotate list/detail endpoints.

Examples:

```csharp
[HttpGet("")]
[FcmsAuthorize(FcmsPermissions.UsersView)]
public async Task<IActionResult> Index(CancellationToken ct)
```

```csharp
[HttpGet("")]
[FcmsAuthorize(FcmsPermissions.RolesView)]
public async Task<IActionResult> Index(CancellationToken ct)
```

```csharp
[HttpGet("{id:guid}")]
[FcmsAuthorize(FcmsPermissions.RolesView)]
public async Task<IActionResult> Detail(Guid id, CancellationToken ct)
```

For DataTable endpoints:

```csharp
[HttpPost("datatable")]
[ValidateAntiForgeryToken]
[FcmsAuthorize(FcmsPermissions.PagesView)]
public Task<IActionResult> DataTable(...)
```

### Controllers to check

- `CategoryController.cs`
- `DashboardController.cs`
- `PageController.cs`
- `PostController.cs`
- `RedirectController.cs`
- `RoleController.cs`
- `TrashController.cs`
- `UserController.cs`
- `NotificationController.cs`

### Verification

- User without `users.view` cannot access `/admin/users`.
- User with `users.view` can view list but cannot create/edit/delete unless those permissions exist.
- Menu visibility and controller access match.

---

## Task 3.2: Require stronger permission for module operations

**Severity:** High/Medium  
**Objective:** Runtime module operations should be restricted to highest-trust users.

### Current state

`ModulesController` class-level permission:

```csharp
[FcmsAuthorize(FcmsPermissions.SystemManage)]
```

### Problem

`system.manage` may be too broad for arbitrary DLL upload/restart/uninstall. Module upload is code execution.

### Implementation options

Preferred:

1. Add a dedicated permission:

```csharp
public const string ModulesManage = "modules.manage";
public const string ModulesUpload = "modules.upload";
```

2. Require `ModulesManage` for view/activate/deactivate.
3. Require `ModulesUpload` or SuperAdmin for upload.
4. For destructive operations, require SuperAdmin or recent 2FA confirmation.

Minimum safe improvement:

- Keep `SystemManage` but add code-level SuperAdmin check for:
  - upload
  - uninstall with dropTables
  - restart
  - scaffold in development

Example helper:

```csharp
private bool IsSuperAdmin() =>
    User.IsInRole(FcmsRoles.SuperAdmin) ||
    User.IsInRole(FcmsRoles.SuperAdmin.ToUpperInvariant());
```

Then:

```csharp
if (!IsSuperAdmin()) return Forbid();
```

### Verification

- Non-SuperAdmin with `system.manage` cannot upload DLL modules if SuperAdmin gate is chosen.
- SuperAdmin can still manage modules.

---

# Phase 4 — Module Upload, Integrity, and Runtime Loading

## Task 4.1: Add module package size and zip-bomb protections

**Severity:** Medium/High  
**Objective:** Prevent malicious ZIP archives from exhausting disk/memory or writing unexpected files.

### Current state

`ModulesController.Upload` validates `.zip`, unsafe paths, and `module.json`, but lacks:

- max upload size
- max uncompressed size
- max entry count
- allowed extension list
- filename sanitize

### File

- Modify: `src/FlexCms.Host/Controllers/Admin/ModulesController.cs`

### Implementation

Add constants:

```csharp
private const long MaxModulePackageBytes = 50L * 1024 * 1024;
private const long MaxModuleUncompressedBytes = 200L * 1024 * 1024;
private const int MaxModuleZipEntries = 5000;

private static readonly HashSet<string> AllowedModuleExtensions = new(StringComparer.OrdinalIgnoreCase)
{
    ".dll", ".pdb", ".json", ".deps.json", ".runtimeconfig.json",
    ".cshtml", ".css", ".js", ".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg",
    ".woff", ".woff2", ".ttf", ".map"
};
```

Check upload size:

```csharp
if (file.Length > MaxModulePackageBytes)
    return FcmsFail("Module package is too large.");
```

Use safe staging filename:

```csharp
var safeFileName = Path.GetFileName(file.FileName);
var packagePath = Path.Combine(stagingDir, safeFileName);
```

Before extraction, iterate entries:

```csharp
long totalUncompressed = 0;
int entryCount = 0;

foreach (var entry in archive.Entries)
{
    entryCount++;
    if (entryCount > MaxModuleZipEntries)
        return FcmsFail("Module package contains too many files.");

    totalUncompressed += entry.Length;
    if (totalUncompressed > MaxModuleUncompressedBytes)
        return FcmsFail("Module package uncompressed size is too large.");

    if (!string.IsNullOrEmpty(entry.Name))
    {
        var ext = Path.GetExtension(entry.Name);
        if (!AllowedModuleExtensions.Contains(ext) && !entry.Name.EndsWith(".deps.json") && !entry.Name.EndsWith(".runtimeconfig.json"))
            return FcmsFail($"Module package contains disallowed file type: {entry.FullName}");
    }
}
```

Block sensitive names:

```csharp
var lower = entry.FullName.ToLowerInvariant();
if (lower.Contains(".env") || lower.Contains("appsettings") || lower.EndsWith(".pfx") || lower.EndsWith(".pem") || lower.EndsWith(".key"))
    return FcmsFail($"Module package contains sensitive file name: {entry.FullName}");
```

### Verification

- Valid module ZIP uploads successfully.
- ZIP with `../evil.dll` still blocked.
- Huge/uncompressed ZIP is blocked.
- `.env` or `.pfx` in package is blocked.

---

## Task 4.2: Verify module manifest matches loaded module

**Severity:** High/Medium  
**Objective:** Prevent mismatch between extracted `module.json`, embedded module manifest, and `IFcmsModule.ModuleId`.

### Problem

Upload reads extracted `module.json` and stores folder by its `ModuleId`, while loader reads embedded `module.json` from assembly. These can diverge.

### Files

- Modify: `src/FlexCms.Host/Controllers/Admin/ModulesController.cs`
- Modify: `src/FlexCms.Framework/Modules/ModuleLoader.cs` if needed.

### Implementation

During upload, after extraction:

1. Find candidate DLLs in module source dir.
2. Load only enough metadata to verify embedded manifest.
3. Verify:
   - extracted `module.json.ModuleId`
   - embedded `module.json.ModuleId`
   - `IFcmsModule.ModuleId`
   all match.

If direct assembly load during upload is too risky, at least require package contains exactly one root module DLL and compare extracted manifest with embedded resource using metadata APIs.

### Verification

- Package with mismatched manifest is rejected.
- Package with missing DLL is rejected.
- Package with multiple module DLLs requires clear deterministic rule or is rejected.

---

## Task 4.3: Add module hash/integrity record

**Severity:** High  
**Objective:** Detect tampering of module binaries after upload.

### Files

- Modify: `src/FlexCms.Framework/Modules/FcmsModuleRecord.cs`
- Modify DB config/migration/schema upgrader as needed.
- Modify: `ModulesController.Upload`
- Modify: `ModuleActivationService` or `ModuleManager`

### Implementation

Add fields to module record:

```csharp
public string? PackageHashSha256 { get; set; }
public string? ApprovedHashSha256 { get; set; }
public Guid? ApprovedByUserId { get; set; }
public DateTime? ApprovedAt { get; set; }
```

On upload:

- compute package hash
- store it
- mark status `PendingApproval` unless in development

At startup/load:

- compute DLL/package hash
- compare with approved hash
- if mismatch: do not register services/routes; mark module error.

### Verification

- Tampering with DLL after approval prevents module load.
- Re-upload updates hash and requires reapproval.

---

## Task 4.4: Add production guard for solution-root module scanning

**Severity:** Medium  
**Objective:** Prevent dev source-tree modules from taking precedence in production.

### Current behavior

`FcmsServiceExtensions.cs` scans solution-root `modules/` before host modules folder.

### Problem

This is useful for development but risky in production. A leftover source-tree module could override uploaded production package.

### Implementation

Only scan root modules when environment is development, or when explicit config enables it.

Because `AddFlexCms` currently receives only `FlexCmsOptions`, add option:

```csharp
public bool EnableDevelopmentModuleRootScan { get; set; }
```

Set from host:

```csharp
EnableDevelopmentModuleRootScan = builder.Environment.IsDevelopment()
```

Then in `BuildModuleRegistry` call decide roots accordingly.

### Verification

- Development still scans source modules.
- Production scans only host/runtime modules folder.

---

## Task 4.5: Treat module static files as public and block secret files

**Severity:** Medium  
**Objective:** Prevent accidental public exposure of secrets under module `wwwroot`.

### Current behavior

`UseFcmsModuleStaticFiles` serves each module `wwwroot` at:

```text
/modules/{module-id-lowercase}/...
```

### Implementation

- In upload validator, block sensitive file names under any folder, especially `wwwroot`.
- In static file options, explicitly keep unknown types disabled.

```csharp
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(moduleWwwroot),
    RequestPath = $"/modules/{slug}",
    ContentTypeProvider = provider,
    ServeUnknownFileTypes = false,
});
```

Optional: block `.map` files in production.

### Verification

- Static CSS/JS/image assets still load.
- `.env`, `.pfx`, `.pem`, `.key`, `appsettings*.json` cannot be uploaded/served.

---

# Phase 5 — Audit Logging Hardening

## Task 5.1: Move audit toggle to AuditManage and always log audit changes

**Severity:** Medium  
**Objective:** Audit logging should not be disabled by a generic settings manager without a strong audit trail.

### Current state

`SettingsController.ToggleAudit` uses:

```csharp
[FcmsAuthorize(FcmsPermissions.SettingsManage)]
```

### Fix

Change to:

```csharp
[FcmsAuthorize(FcmsPermissions.AuditManage)]
```

When toggling audit, write a mandatory security event even when disabling audit.

Option A: add a method in `IFcmsLogService` like `LogSecurityEventBypassSettingsAsync` that does not check `AuditConfig.Enabled`.

Option B: write directly to repository from controller/service for this specific event.

Event examples:

- `Audit.Enabled`
- `Audit.Disabled`

### Verification

- User with settings manage but not audit manage cannot toggle audit.
- Disabling audit creates a durable log row.
- Re-enabling audit creates a durable log row.

---

## Task 5.2: Add fallback audit sink for audit write failures

**Severity:** Medium  
**Objective:** Critical audit failures should not disappear silently.

### Current state

`FcmsAuditInterceptor` catches and logs warnings when audit write fails.

### Problem

If DB audit write fails, high-risk events may be lost.

### Implementation

Add a simple fallback append-only file writer for audit failures:

- path: `App_Data/logs/audit-fallback-yyyyMMdd.log`
- JSON lines
- include action/entity/user/ip/value if available

Use it in:

- `FcmsAuditInterceptor` catch block
- `FcmsLogService.LogAsync` catch block if added

### Verification

- Simulated audit DB failure writes fallback file.
- Normal audit logging still writes DB rows.

---

## Task 5.3: Add tamper-evident audit hash chain

**Severity:** P2 / Hardening  
**Objective:** Make audit logs tamper-evident.

### Implementation

Add columns to `FcmsLog` and `FcmsLogArchive`:

```csharp
public string? PrevHash { get; set; }
public string? Hash { get; set; }
```

When writing a log:

1. Read latest log hash.
2. Compute:

```text
Hash = SHA256(PrevHash + CreatedAt + UserId + Action + EntityType + EntityId + Value)
```

3. Store both.

Add admin verification action:

- checks chain continuity
- reports first broken row

### Verification

- Audit chain verifies clean.
- Manually modifying a row causes verification failure.

---

# Phase 6 — Multi-DB / Migration Consistency

## Task 6.1: Validate selected provider and module support

**Severity:** Medium  
**Objective:** Prevent module activation when selected DB provider is unsupported or misconfigured.

### Current behavior

Core provider selected in `Program.cs` and `FcmsServiceExtensions.cs`. Modules receive provider string via `ModuleActivationOptions`.

### Problem

Module can return null or fail migrations, but there is no explicit module provider support declaration.

### Implementation

Add to `ModuleManifest`:

```csharp
public string[] SupportedDbProviders { get; set; } = ["mysql", "mssql", "postgresql"];
```

In `ModuleActivationService`, before migration/seed:

```csharp
if (!module.Manifest.SupportedDbProviders.Contains(_opts.Provider, StringComparer.OrdinalIgnoreCase))
{
    errors.Add($"provider: module does not support {_opts.Provider}");
    continue;
}
```

Also validate exactly one provider selected at startup.

### Verification

- Module declaring only `postgresql` does not activate on MySQL.
- Error appears in admin module list.

---

## Task 6.2: Review EnsureCreated vs migrations strategy

**Severity:** Medium/P2  
**Objective:** Avoid schema drift across MySQL/MSSQL/PostgreSQL.

### Current state

- Setup uses `EnsureCreatedAsync`.
- Modules use EF migrations.
- Framework has schema upgrader logic.

### Recommendation

Choose one consistent strategy:

- Option A: EF migrations for core + modules.
- Option B: framework schema upgrader for core; module migrations remain explicit.

Document and test each provider.

### Verification

Run setup and smoke tests for:

- MySQL
- MSSQL
- PostgreSQL

---

# Phase 7 — Test Plan

## Required automated tests

Add or update tests for:

1. Legacy OTP endpoint disabled or protected.
2. 2FA pending cookie rejects tampered payload.
3. 2FA pending cookie expires.
4. Bearer token can authenticate via smart scheme.
5. Bearer token scope limits permission access.
6. SuperAdmin cookie bypasses permission checks.
7. SuperAdmin API token still needs matching scope.
8. Module ZIP upload rejects path traversal.
9. Module ZIP upload rejects zip bomb / too many entries.
10. Module package rejects mismatched manifest IDs.
11. Audit toggle requires `AuditManage`.
12. User/Role list pages require view permissions.

## Commands

From repo root:

```bash
dotnet restore FlexCms.slnx
dotnet build FlexCms.slnx
dotnet test FlexCms.slnx
```

If Windows path/build is preferred:

```powershell
cd D:\flex_cms_v1
dotnet restore .\FlexCms.slnx
dotnet build .\FlexCms.slnx
dotnet test .\FlexCms.slnx
```

---

# Phase 8 — Implementation Order

Implement in this order:

1. **AuthController legacy OTP removal/security**
2. **2FA pending cookie protection**
3. **2FA rate limits**
4. **Smart cookie-or-bearer auth scheme**
5. **API token scope enforcement**
6. **Admin granular view permissions**
7. **Audit toggle permission + mandatory audit event**
8. **Module upload zip hardening**
9. **Module manifest consistency check**
10. **Module hash/approval flow**
11. **Production guard for dev module scanning**
12. **Audit fallback sink / hash chain**
13. **Multi-DB provider compatibility metadata**

---

# Acceptance Criteria

The work is complete when:

- No passwordless legacy OTP login route remains.
- 2FA pending state is cryptographically protected.
- 2FA endpoints are rate-limited.
- Cookie and Bearer auth both work through an intentional policy scheme.
- API token scopes actually restrict authorization.
- SuperAdmin API token is not unlimited without scopes.
- Sensitive admin list/detail pages have explicit view/manage permissions.
- Module upload has zip-bomb, file type, sensitive-file, and manifest validation.
- Production does not scan dev source module folders unless explicitly enabled.
- Audit toggle requires `AuditManage` and logs enable/disable events.
- Tests/build pass.

---

# Notes for Claude Agent

- Do not make broad unrelated refactors.
- Prefer small commits per phase/task.
- If a task requires schema changes, update the schema upgrader/migrations consistently for MySQL, MSSQL, and PostgreSQL.
- Preserve existing UX unless a security issue requires changing it.
- For any endpoint behavior change, add/adjust tests.
- Treat runtime module loading as code execution, not as a simple file upload feature.
