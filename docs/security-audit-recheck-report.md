# FlexCms v1 Security Audit Recheck Report

**Project:** `D:\flex_cms_v1` / `/mnt/d/flex_cms_v1`  
**Checked at:** 2026-06-18 19:10:23 +06  
**Branch/status:** `new-version...origin/new-version`, working tree clean during check  
**Scope:** Claude agent security-fix implementation recheck against previous `security-audit-fix-plan.md`.

---

## 1. Executive Summary

Claude agent previous security audit plan অনুযায়ী অনেকগুলো critical/high issue fix করেছে। Auth, 2FA, API token authorization, audit log, module upload validation, admin granular permissions—সব area-তেই meaningful improvement হয়েছে।

Build এবং test দুটোই pass করেছে:

- `dotnet build .\FlexCms.slnx --no-restore` → **PASS**
- `dotnet test .\FlexCms.slnx --no-build` → **PASS**
- Unit tests: **661/661 passed**
- Integration tests: **296/296 passed**
- Total tests: **957 passed**

তবে **runtime module DLL trust model এখনো fully production-safe না**। Upload hygiene এবং tamper detection improve হয়েছে, কিন্তু DLL hash/tamper check currently module load হওয়ার পরে হচ্ছে। তাই malicious/tampered DLL already process-এ load হয়ে যেতে পারে। এই অংশটি এখনো highest-priority remaining security work।

---

## 2. Verification Commands Run

```powershell
Set-Location 'D:\flex_cms_v1'
dotnet build .\FlexCms.slnx --no-restore
dotnet test .\FlexCms.slnx --no-build
```

### Build Result

```text
Build succeeded.
0 Error(s)
24 Warning(s)
```

Warnings mostly CA2000 disposable warnings in test code; security fixes compile successfully.

### Test Result

```text
Passed! - Unit:        661 passed, 0 failed
Passed! - Integration: 296 passed, 0 failed
Total:                957 passed, 0 failed
```

---

## 3. Auth / 2FA Recheck

### Status: ✅ Mostly Passed

Verified files:

- `src/FlexCms.Host/Controllers/AuthController.cs`
- `src/FlexCms.Framework/Extensions/FcmsServiceExtensions.cs`

### Confirmed Fixes

#### 3.1 Legacy OTP passwordless sign-in removed

`AuthController.cs` no longer exposes the old `/VerifyOtp` flow. Code comment confirms legacy endpoint was removed because it allowed sign-in via `UserId + OTP` without prior protected password/session challenge.

Current comment:

```csharp
// Legacy /VerifyOtp endpoint removed — it signed in via UserId + OTP with
// no prior password validation, no protected pending-challenge cookie,
// and no fcms.session_id claim...
```

#### 3.2 Pending 2FA state protected

`AuthController.cs` now uses `IDataProtector`:

```csharp
_pending2FaProtector = dataProtection.CreateProtector("FlexCms.Auth.PendingTwoFactor.v1");
```

Pending 2FA cookie now stores protected JSON:

```csharp
var protectedValue = _pending2FaProtector.Protect(json);
```

Cookie flags:

- `HttpOnly = true`
- `SameSite = Strict`
- `Expires = 10 minutes`
- `Path = /auth/two-factor`

Server-side expiry check also exists:

```csharp
if (DateTimeOffset.UtcNow - state.IssuedAtUtc > TimeSpan.FromMinutes(10))
    return null;
```

#### 3.3 2FA verify/resend protected by antiforgery

Confirmed:

```csharp
[HttpPost("auth/two-factor")]
[ValidateAntiForgeryToken]
```

```csharp
[HttpPost("auth/two-factor/resend")]
[ValidateAntiForgeryToken]
```

#### 3.4 Rate limiting added

`FcmsServiceExtensions.cs` now applies IP-partitioned rate limits for:

- `/auth/login`
- `/auth/two-factor`
- `/auth/two-factor/resend`
- `/auth/forgot-password`
- `/auth/reset-password`
- registration/comment/subscribe/webhook paths

Important examples:

- Login: 10 attempts/min/IP
- OTP verify: 5 attempts/min/IP
- OTP resend: 3 attempts/5 min/IP

### Remaining Auth Notes

No major auth blocker found in this recheck. Optional future improvement: rate-limit keys could include username/userId after password step to reduce distributed attack surface, but current IP partitioning is already a strong improvement.

---

## 4. API Token Auth/Authz Recheck

### Status: ✅ Passed

Verified files:

- `src/FlexCms.Framework/Extensions/FcmsServiceExtensions.cs`
- `src/FlexCms.Framework/Api/FcmsApiTokenAuthenticationHandler.cs`
- `src/FlexCms.Framework/Auth/FcmsClaimTypes.cs`
- `src/FlexCms.Framework/Auth/FcmsAuthorizeAttribute.cs`
- `src/FlexCms.Framework/Services/PermissionService.cs`

### Confirmed Fixes

#### 4.1 Smart auth scheme added

Default auth scheme now forwards by request type:

```csharp
services.AddAuthentication(opts =>
{
    opts.DefaultScheme = FcmsAuthSchemes.Smart;
    opts.DefaultAuthenticateScheme = FcmsAuthSchemes.Smart;
    opts.DefaultChallengeScheme = FcmsAuthSchemes.Smart;
})
.AddPolicyScheme(FcmsAuthSchemes.Smart, "Cookie or API Token", o =>
{
    o.ForwardDefaultSelector = ctx =>
    {
        var auth = ctx.Request.Headers.Authorization.ToString();
        return auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? FcmsApiTokenAuthenticationHandler.SchemeName
            : IdentityConstants.ApplicationScheme;
    };
})
```

This fixes the earlier risk where `[Authorize]` routes might only use cookie auth and ignore bearer API tokens.

#### 4.2 API token claims added

API token handler now adds:

- `fcms.api_token_id`
- `fcms.api_scope`

These are used later by permission checks.

#### 4.3 Token scope intersection enforced

`PermissionService.HasPermissionAsync(...)` now enforces:

```text
Final API token permission = user/role permission AND token scope permission
```

Important behavior:

- Browser SuperAdmin still bypasses normal permission checks.
- API token SuperAdmin does **not** bypass token scopes.
- Empty API token scope set denies access.

Relevant logic:

```csharp
var isApiToken = user.HasClaim(c => c.Type == FcmsClaimTypes.ApiTokenId);
if (!isApiToken && user.IsInRole(FcmsRoles.SuperAdmin)) return true;
...
var scopeAllowed = PermissionExpression.Evaluate(permissionExpr, apiScopes);
return roleAllowed && scopeAllowed;
```

### Remaining API Token Notes

No major blocker found. Recommended future tests:

- token with no scopes denied
- token with unrelated scope denied
- token with correct scope allowed only if user role also has permission
- SuperAdmin token still requires explicit token scope

---

## 5. Module Upload / Runtime DLL Security Recheck

### Status: ⚠️ Partially Passed — Important Remaining Risk

Verified files:

- `src/FlexCms.Host/Controllers/Admin/ModulesController.cs`
- `src/FlexCms.Framework/Modules/ModuleActivationService.cs`
- `src/FlexCms.Framework/Modules/FcmsModuleRecord.cs`
- `src/FlexCms.Framework/Modules/ModuleManifest.cs`
- `src/FlexCms.Framework/Extensions/FcmsServiceExtensions.cs`

### Confirmed Improvements

#### 5.1 Upload endpoint authorization improved

`ModulesController` has class-level module manage permission:

```csharp
[FcmsAuthorize(FcmsPermissions.ModulesManage)]
```

Upload endpoint requires dedicated permission:

```csharp
[HttpPost("upload")]
[ValidateAntiForgeryToken]
[FcmsAuthorize(FcmsPermissions.ModulesUpload)]
```

Destructive actions like restart/uninstall also check SuperAdmin in code.

#### 5.2 ZIP safety validation added

Upload now validates:

- package must be `.zip`
- max upload size: 50 MB
- max uncompressed size: 200 MB
- max ZIP entries: 5000
- path traversal blocked
- rooted paths blocked
- destination escaping blocked

Relevant constants:

```csharp
private const long MaxModulePackageBytes = 50L * 1024 * 1024;
private const long MaxModuleUncompressedBytes = 200L * 1024 * 1024;
private const int MaxModuleZipEntries = 5000;
```

#### 5.3 Sensitive/dangerous files blocked

Upload blocks obvious risky file names/extensions:

- `.env`
- `appsettings*.json`
- `.pfx`
- `.pem`
- `.key`
- `.so`
- `.exe`
- `.bat`
- `.sh`

Allowed extension whitelist exists.

#### 5.4 Loose manifest vs embedded manifest verification added

`VerifyEmbeddedManifest(...)` uses `MetadataLoadContext` to read module DLL metadata without executing module code. It verifies the embedded `module.json` ModuleId matches the loose `module.json` ModuleId.

This helps prevent package identity mismatch attacks.

#### 5.5 DLL hash recorded at upload

After copy, canonical module DLL hash is computed and stored:

```csharp
PackageHashSha256 = packageHash
```

#### 5.6 DB provider support added

`ModuleManifest` now includes:

```csharp
public string[] SupportedDbProviders { get; set; } = ["mysql", "mssql", "postgresql"];
```

`ModuleActivationService` checks this before migration/seed.

### Critical Remaining Risk

#### 5.7 DLL hash/tamper verification happens too late

`ModuleActivationService` checks DLL hash during activation, but by that point module has already been loaded by module discovery/registry flow.

The code comment itself acknowledges this:

```csharp
// We can't *prevent* that — by the time we get here the DLL has already been
// loaded — but we surface it loudly so an operator can restore from backup...
```

Impact:

- A tampered DLL may already be loaded into the ASP.NET process.
- Static constructors or module registration paths may already have executed depending on load path.
- The current check is **detective**, not **preventive**.

Severity: **High** for production plugin/module system.

### Recommended Fix

Move integrity verification into pre-load module discovery:

1. Before `Assembly.LoadFrom(...)` in `ModuleLoader`, read loose `module.json` first.
2. Resolve expected module record/hash from DB or approved manifest store.
3. Compute DLL SHA-256 before load.
4. If no approved hash/signature exists, do not load in production.
5. If hash mismatch, do not call `Assembly.LoadFrom`.
6. Only after passing integrity/signature checks, load assembly and register services/MVC parts.

Ideal design:

```text
ZIP upload -> extract to quarantine -> static verification -> signature check -> approval -> move to runtime modules folder -> startup pre-load verify -> Assembly.LoadFrom
```

### Additional Module Work Still Needed

- Real signed package verification with trusted public key/certificate.
- Full module approval workflow before runtime load.
- Enforce approved capabilities before registration.
- Require SuperAdmin + recent 2FA step-up for upload/restart/uninstall.
- Consider quarantine folder separate from runtime-scanned folder.

---

## 6. Audit Log Recheck

### Status: ✅ Mostly Passed

Verified files:

- `src/FlexCms.Framework/Cms/FcmsAuditInterceptor.cs`
- `src/FlexCms.Framework/Cms/FcmsLogService.cs`
- `src/FlexCms.Framework/Cms/FcmsLogChain.cs`
- `src/FlexCms.Framework/Cms/FcmsAuditFallbackSink.cs`
- `src/FlexCms.Host/Controllers/Admin/AuditLogController.cs`
- `src/FlexCms.Framework/Db/Migration/FrameworkSchemaUpgrader.cs`

### Confirmed Fixes

#### 6.1 Tamper-evident hash chain added

`FcmsLog` and `FcmsLogArchive` now have:

- `PrevHash`
- `Hash`

`FcmsLogChain.Compute(...)` computes row hash from key audit row fields.

#### 6.2 Chain verification endpoint added

`AuditLogController` includes:

```csharp
[HttpPost("verify-chain")]
[ValidateAntiForgeryToken]
[FcmsAuthorize(FcmsPermissions.AuditManage)]
public async Task<IActionResult> VerifyChain(...)
```

This verifies up to 50,000 rows and reports first broken row.

#### 6.3 Audit fallback sink added

If DB audit write fails, `FcmsAuditInterceptor` writes fallback JSONL via `IFcmsAuditFallbackSink`:

```text
App_Data/logs/audit-fallback-yyyyMMdd.log
```

This reduces risk of silent audit loss.

#### 6.4 Audit permissions separated

`AuditLogController` now uses:

- `AuditView` for listing/datatable
- `AuditManage` for archive clear/force archive/verify chain

### Remaining Audit Notes

- Code-level fallback is good, but true append-only protection depends on deployment filesystem permissions or OS-level log shipping.
- Hash chain is tamper-evident, not tamper-proof. Attackers with DB write + app key/source knowledge can rewrite chains unless logs are exported/anchored externally.

Recommended future improvement:

- Periodically export latest audit chain head to external storage, SIEM, or object storage with immutable retention.

---

## 7. Admin Authorization Recheck

### Status: ✅ Major Sensitive Areas Passed, Minor Gaps Remain

Verified files include:

- `UserController.cs`
- `RoleController.cs`
- `PermissionController.cs`
- `SettingsController.cs`
- `AuditLogController.cs`
- `ModulesController.cs`
- `RedirectController.cs`
- automated scan over `src/FlexCms.Host/Controllers/Admin`

### Confirmed Improvements

Granular permissions added to main sensitive areas:

- Users:
  - `UsersView`
  - `UsersCreate`
  - `UsersEdit`
  - `UsersDelete`
- Roles:
  - `RolesView`
  - `RolesCreate`
  - `RolesEdit`
  - `RolesDelete`
- Permissions:
  - `RolesPermissions`
- Settings:
  - `SettingsView`
  - `SettingsManage`
  - audit toggle guarded by `AuditManage`
- Audit:
  - `AuditView`
  - `AuditManage`
- Modules:
  - `ModulesManage`
  - `ModulesUpload`
  - some destructive operations also require SuperAdmin
- Redirect CRUD:
  - create/edit/delete permissions added

### Minor Remaining Gaps

Automated scan still found missing granular permissions on:

#### 7.1 `NotificationController`

Missing granular permissions:

- `GET Index`
- `GET Recent`
- `POST MarkRead`
- `POST MarkAllRead`

These are probably lower severity because notifications are user/admin convenience actions, but they still rely only on base admin auth. Recommended:

- `NotificationsView` for read/list/recent
- `NotificationsManage` or `NotificationsAcknowledge` for mark-read actions

#### 7.2 `RedirectController.Index`

`Create/Edit/Delete` have granular permissions, but `Index` is missing explicit view permission.

Recommended:

```csharp
[FcmsAuthorize(FcmsPermissions.RedirectsView)]
public async Task<IActionResult> Index(...)
```

If `RedirectsView` does not exist yet, add it to `FcmsPermissions` and seed service.

---

## 8. Remaining Issues Prioritized for Claude Agent

### P0 / High Priority

#### 8.1 Prevent module DLL load before integrity/signature verification

Current state detects hash mismatch after load. Must move check before `Assembly.LoadFrom` / module registration.

Implementation target:

- `src/FlexCms.Framework/Modules/ModuleLoader.cs`
- `src/FlexCms.Framework/Extensions/FcmsServiceExtensions.cs`
- possibly introduce `IModuleTrustService`

Acceptance criteria:

- Tampered DLL is never loaded.
- Tampered DLL does not register services.
- Tampered DLL does not become MVC ApplicationPart.
- Error is visible in admin module UI/logs.
- Unit/integration test covers hash mismatch pre-load block.

#### 8.2 Add module signature verification

Implementation target:

- module package format
- upload controller
- module trust service
- appsettings/public key configuration

Acceptance criteria:

- Unsigned package rejected in production.
- Invalid signature rejected.
- Trusted signed package accepted.
- Development mode can optionally allow unsigned packages with explicit warning.

#### 8.3 Step-up/recent 2FA for module dangerous operations

Apply to:

- module upload
- overwrite
- uninstall
- restart
- maybe activation/deactivation if it loads/routes code

Acceptance criteria:

- SuperAdmin without recent 2FA must re-confirm.
- Recent 2FA window expires after short time.
- Audit logs include who approved and from which IP/session.

### P1 / Medium Priority

#### 8.4 Complete module approval/capability enforcement

Existing fields:

- `ApprovedCapabilities`
- `ApprovedByUserId`
- `ApprovedAt`

Need full enforcement before module activation/registration.

Acceptance criteria:

- New uploaded module status = PendingApproval.
- Pending modules not loaded in production.
- Admin approval UI shows requested permissions/capabilities.
- Only approved capabilities can be used.

#### 8.5 Fix remaining granular admin permission gaps

Files:

- `NotificationController.cs`
- `RedirectController.cs`

Acceptance criteria:

- All admin actions have explicit granular permission or documented intentional base-admin-only behavior.
- Automated controller permission scan returns no unexpected missing permissions.

### P2 / Hardening

#### 8.6 External audit chain anchoring

Acceptance criteria:

- Periodic chain head export to immutable/append-only external location.
- Admin UI can show latest anchored hash/time.

---

## 9. Final Verdict

### Overall Result: ✅ Improved and build/test clean, but module trust still incomplete

Claude agent successfully fixed many of the original high-risk auth/authz/audit issues. The project now compiles and all tests pass. The biggest remaining issue is still the runtime module DLL model:

```text
Uploaded DLL == trusted code execution inside the CMS process
```

Current code improves ZIP validation and records hash, but does not yet prevent a tampered DLL from being loaded because hash verification occurs after loading. For production-grade CMS/plugin security, module trust verification must happen before any assembly load or service/MVC registration.

Recommended next Claude task:

> Implement pre-load module trust verification: no module DLL should be loaded, instantiated, registered, or added as MVC ApplicationPart unless its hash/signature and approval status pass first.
