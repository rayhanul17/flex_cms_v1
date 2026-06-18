# FlexCms v1 Security Audit Recheck 2 Report

**Project:** `D:\flex_cms_v1` / `/mnt/d/flex_cms_v1`  
**Checked at:** 2026-06-18 20:38:55 +06  
**Branch/status during check:** `new-version...origin/new-version`, working tree clean  
**Purpose:** Verify latest Claude/security fixes after previous `security-audit-recheck-report.md`, especially module pre-load trust verification and remaining admin permission gaps.

---

## 1. Executive Summary

Latest security changes significantly improved the remaining issues from the previous recheck.

Confirmed latest relevant commit:

```text
590206d security(modules): P0 §8.1 - pre-load integrity gate + P1 admin perm gaps
```

High-level result:

- Build: **PASS**
- Tests: **PASS**
- Admin granular permission scan: **clean**
- Module DLL hash/tamper check: moved to **pre-load phase** before `Assembly.LoadFrom`

The main previous blocker—module DLL hash check happening after load—has been mostly addressed with a pre-load gate.

Remaining hardening items still exist for stricter production security:

1. Trust-on-first-use fallback still allows unknown modules when no approved hash/trust store is available.
2. Non-module DLLs can still reach `ModuleLoader.LoadFromPath(...)`; safer behavior is to skip DLLs with no embedded `module.json` before calling loader.
3. Full package signing / trusted publisher / approval workflow / recent-2FA step-up are still future hardening items.

---

## 2. Commands Run

### Git/status

```bash
git status --short --branch
git log --oneline -8
git diff --name-status origin/new-version...HEAD
```

Result:

```text
## new-version...origin/new-version

Recent commits:
590206d security(modules): P0 §8.1 - pre-load integrity gate + P1 admin perm gaps
6b63189 security(deferred): phase 4.2 + 4.3 + 5.3 - manifest match, DLL hash, audit chain
ceb9bfb security(audit+db): phase 5+6 - audit toggle hardening, fallback sink, DB provider gate
8a67c6a security(modules): phase 4 - upload hardening + prod scan guard + static lockdown
5969d3f security(perms): phase 3 - granular admin view perms + module ops permission split
979640e security(auth): phase 2 - smart auth scheme + API token scope enforcement
30ada80 security(auth): phase 1 - legacy OTP removed, 2FA cookie protected, 2FA rate-limited
0c7109d docs(modules): ship FlexCms.dev.slnx.example as tracked baseline
```

No changed files vs origin were shown during this check.

### Build

```powershell
Set-Location 'D:\flex_cms_v1'
dotnet build .\FlexCms.slnx --no-restore
```

Result:

```text
Build succeeded.
0 Error(s)
24 Warning(s)
```

Warnings are CA2000 disposable warnings in tests, same category as before.

### Tests

```powershell
Set-Location 'D:\flex_cms_v1'
dotnet test .\FlexCms.slnx --no-build
```

Result:

```text
Passed! - Unit:        668 passed, 0 failed
Passed! - Integration: 296 passed, 0 failed
Total:                964 passed, 0 failed
```

---

## 3. Module Pre-load Trust Verification

### Status: ✅ Improved / Mostly Fixed

Verified files:

- `src/FlexCms.Framework/Modules/ModuleManager.cs`
- `src/FlexCms.Framework/Modules/IModuleTrustStore.cs`
- `src/FlexCms.Framework/Modules/AdoModuleTrustStore.cs`
- `src/FlexCms.Framework/Extensions/FcmsServiceExtensions.cs`
- `tests/FlexCms.Tests.Unit/Phase4/ModuleManagerTests.cs`
- `tests/FlexCms.Tests.Unit/Phase4/ModuleTrustStoreTests.cs`

### 3.1 Trust store added

New `IModuleTrustStore` abstraction added:

```csharp
public interface IModuleTrustStore
{
    string? GetApprovedHash(string moduleId);
    bool IsAvailable { get; }
}
```

Purpose:

- Pre-DI lookup of approved module DLL hashes.
- Used by `ModuleManager.ScanAndLoad(...)` before module load.
- Avoids needing EF/repository while service registration is still being built.

### 3.2 ADO.NET trust store added

`AdoModuleTrustStore` reads approved module hashes from:

```text
fcms_module_records.PackageHashSha256
```

Provider-specific SQL exists for:

- PostgreSQL
- MySQL
- MSSQL

Important behavior:

- If DB/provider/connection/schema is unavailable, it returns `NullModuleTrustStore`.
- This enables trust-on-first-use on fresh install/dev scenarios.

### 3.3 Trust store built before module scanning/loading

`FcmsServiceExtensions.BuildModuleRegistry(...)` now builds trust store before `ModuleManager` scans modules:

```csharp
var trust = AdoModuleTrustStore.Build(provider, connectionString);
var manager = new ModuleManager(loader, managerLog, trust);
```

This is the correct lifecycle direction because module discovery happens before DI container is fully built.

### 3.4 Pre-load integrity gate added before `Assembly.LoadFrom`

`ModuleManager.ScanAndLoad(...)` now checks candidate DLLs before calling:

```csharp
_loader.LoadFromPath(dll, moduleFolder, disabled)
```

Relevant flow:

```csharp
if (!PreLoadIntegrityCheck(dll, out var declaredId, out var precomputedHash))
{
    _logger.LogError(
        "Module DLL at '{Path}' failed pre-load integrity check — refusing to load.", dll);
    continue;
}

var module = _loader.LoadFromPath(dll, moduleFolder, disabled);
```

This addresses the previous high-risk issue where hash/tamper detection happened after the DLL was already loaded.

### 3.5 Hash mismatch blocks module load

`PreLoadIntegrityCheck(...)`:

1. Computes SHA-256 of DLL file.
2. Uses `MetadataLoadContext` to read embedded `module.json` without executing module code.
3. Extracts `ModuleId`.
4. Looks up approved hash from trust store.
5. If approved hash exists and differs, returns `false`.

Relevant logic:

```csharp
var approved = _trust.GetApprovedHash(declaredModuleId);
...
if (!string.Equals(approved, fileHash, StringComparison.OrdinalIgnoreCase))
{
    _logger.LogError(
        "PreLoadIntegrityCheck: DLL tampering detected for {Id} — approved {Approved}, current {Current}.",
        declaredModuleId, approved[..Math.Min(12, approved.Length)], fileHash[..12]);
    return false;
}
```

### 3.6 Regression tests added

`ModuleManagerTests` includes test:

```csharp
public void ScanAndLoad_refuses_module_when_trust_store_hash_mismatches()
```

Test expectation:

- fake approved hash does not match actual DLL hash
- `ScanAndLoad(...)` returns empty list
- module is not loaded

This directly covers the previous attack scenario.

---

## 4. Remaining Module Trust Caveats

### 4.1 Trust-on-first-use still exists

If no approved hash exists:

```csharp
if (approved is null)
{
    if (_trust.IsAvailable)
        _logger.LogInformation(
            "PreLoadIntegrityCheck: no approved hash recorded yet for {Id}; trust-on-first-use.",
            declaredModuleId);
    return true;
}
```

Impact:

- Fresh install/dev flow works.
- But strict production security may still allow unknown module code on first load.

Recommended next hardening:

```text
Production mode: unknown/unapproved module hash = block
Development mode: trust-on-first-use allowed with explicit warning
```

Suggested option:

```csharp
public bool AllowModuleTrustOnFirstUse { get; set; } = false;
```

Then only allow TOFU when explicitly enabled or in development.

### 4.2 Non-module DLLs can still reach loader

Current `PreLoadIntegrityCheck(...)` behavior:

```csharp
if (resourceName is null)
{
    // Not a module DLL at all — likely a transitive dep sitting
    // in bin/Debug/. Let it pass; the loader will skip it later.
    return true;
}
```

Risk:

- A DLL with no embedded `module.json` passes the pre-load gate.
- Then `ModuleLoader.LoadFromPath(...)` may still call `Assembly.LoadFrom(dllPath)` to determine whether it is a module.
- If malicious code exists in static/module initializer paths, this is not ideal.

Recommended next fix:

Change pre-load check to return a state instead of bool:

```csharp
enum PreLoadIntegrityResult
{
    NotModule,
    ValidModule,
    InvalidModule
}
```

Then scanning flow should be:

```csharp
var integrity = PreLoadIntegrityCheck(dll, out var declaredId, out var hash);

if (integrity == PreLoadIntegrityResult.InvalidModule)
{
    log error;
    continue;
}

if (integrity == PreLoadIntegrityResult.NotModule)
{
    continue; // do NOT call Assembly.LoadFrom
}

var module = _loader.LoadFromPath(...);
```

Acceptance criteria:

- DLL without embedded `module.json` is skipped before `Assembly.LoadFrom`.
- Actual module DLL with matching approved hash loads.
- Actual module DLL with mismatched hash is refused.
- Unit test covers all three states.

### 4.3 Full package signing still not implemented

Hash pinning helps detect tampering after first approval/upload, but it is not the same as trusted publisher verification.

Still recommended:

- signed module packages
- trusted public key/certificate configuration
- signature checked at upload and startup
- revocation/rotation plan for signing keys

### 4.4 Approval and recent 2FA step-up still recommended

For production plugin security, dangerous operations should require stronger confirmation:

- module upload
- overwrite
- uninstall
- restart
- activation/deactivation if it causes code/service/route registration changes

Recommended:

- SuperAdmin required
- recent 2FA step-up required
- audit log records user/IP/session/action

---

## 5. Admin Granular Authorization Recheck

### Status: ✅ Passed

Previous remaining gaps were:

- `NotificationController`
  - `Index`
  - `Recent`
  - `MarkRead`
  - `MarkAllRead`
- `RedirectController.Index`

These are now fixed.

### 5.1 `NotificationController` fixed

Verified:

```csharp
[HttpGet("")]
[FcmsAuthorize(FcmsPermissions.NotificationsView)]
public async Task<IActionResult> Index(...)
```

```csharp
[HttpGet("recent")]
[FcmsAuthorize(FcmsPermissions.NotificationsView)]
public async Task<IActionResult> Recent(...)
```

```csharp
[HttpPost("mark-read/{id:guid}")]
[ValidateAntiForgeryToken]
[FcmsAuthorize(FcmsPermissions.NotificationsManage)]
public async Task<IActionResult> MarkRead(...)
```

```csharp
[HttpPost("mark-all-read")]
[ValidateAntiForgeryToken]
[FcmsAuthorize(FcmsPermissions.NotificationsManage)]
public async Task<IActionResult> MarkAllRead(...)
```

### 5.2 `RedirectController.Index` fixed

Verified:

```csharp
[HttpGet("")]
[FcmsAuthorize(FcmsPermissions.RedirectsView)]
public async Task<IActionResult> Index(...)
```

### 5.3 Automated scan result

Admin controller granular permission scan output was empty, meaning no missing granular permission was detected by the script.

Scan command pattern:

```bash
python3 - <<'PY'
# scans src/FlexCms.Host/Controllers/Admin/*.cs
# reports HTTP actions without action/class-level FcmsAuthorize(...)
PY
```

Result:

```text
<empty output>
```

Interpretation:

- No unexpected missing granular admin authorization found by this scan.
- Manual review still required for business-level permission semantics, but previous obvious gaps are fixed.

---

## 6. Build/Test Verification

### 6.1 Build

Result:

```text
Build succeeded.
0 Error(s)
24 Warning(s)
```

Warnings are CA2000 disposable warnings in tests and not related to new module trust/admin authz fixes.

### 6.2 Tests

Result:

```text
Unit tests:        668 passed
Integration tests: 296 passed
Total:             964 passed
Failures:          0
Skipped:           0
```

Compared to previous recheck, unit test count increased from 661 to 668, indicating new tests were added.

New relevant tests include:

- `ModuleManagerTests.ScanAndLoad_refuses_module_when_trust_store_hash_mismatches`
- `ModuleTrustStoreTests` around null/unavailable trust store behavior

---

## 7. Final Verdict

### Overall status: ✅ Much improved and currently build/test clean

The latest changes successfully address the main previous blocker:

```text
Before: hash/tamper detection happened after module DLL load.
Now: hash/tamper check happens before ModuleLoader.LoadFromPath / Assembly.LoadFrom for actual module DLLs.
```

Admin granular permission gaps are also fixed and automated scan is clean.

### Remaining recommended next task

The next small but important production-hardening task should be:

> Change `ModuleManager.PreLoadIntegrityCheck` from bool to a tri-state result: `NotModule`, `ValidModule`, `InvalidModule`, and skip `NotModule` DLLs before calling `Assembly.LoadFrom`.

This closes the remaining nuance where non-module DLLs without embedded `module.json` can still be passed to `ModuleLoader` and potentially loaded just to discover they are not modules.

### Future hardening beyond current scope

For production-grade plugin security, still add:

1. Module package signature verification.
2. Trusted publisher/key configuration.
3. Production mode blocks unknown/unapproved module hashes.
4. Formal module approval/capability enforcement before load.
5. Recent-2FA step-up for upload/overwrite/restart/uninstall.
6. External/immutable audit chain anchoring.

---

## 8. Suggested Claude Agent Prompt for Next Fix

```text
In D:\flex_cms_v1, improve ModuleManager pre-load module discovery so non-module DLLs are skipped before Assembly.LoadFrom.

Current issue:
ModuleManager.PreLoadIntegrityCheck returns true when a DLL has no embedded module.json, then ModuleLoader.LoadFromPath may still call Assembly.LoadFrom on that non-module DLL.

Required change:
- Replace bool PreLoadIntegrityCheck with a tri-state result:
  - NotModule
  - ValidModule
  - InvalidModule
- If NotModule: continue scanning without calling ModuleLoader.LoadFromPath.
- If InvalidModule: log error and continue.
- If ValidModule: call ModuleLoader.LoadFromPath.
- Preserve trust-on-first-use behavior only for actual module DLLs with a ModuleId and no stored hash.
- Add unit tests for:
  1. non-module DLL is skipped before load,
  2. valid module with no trust store loads,
  3. valid module with mismatched approved hash is refused.
- Run dotnet build and dotnet test.
```
