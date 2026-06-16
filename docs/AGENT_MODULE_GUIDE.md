# Building a FlexCMS Module — AI Agent Quick Guide

> **Audience: an AI coding agent (Claude / GPT / etc.) handed a fresh FlexCMS
> checkout and told "build me module X."** This file is the shortest possible
> path to a working module. For deeper conceptual background read
> [`MODULE_DEV.md`](MODULE_DEV.md) **after** you have a working scaffold.

---

## 0. Ground rules (read these once)

1. **Reference implementation is `samples/FlexCms.Sample.Hello/`.** Whenever in
   doubt about a file's shape, mirror that sample. It compiles cleanly, follows
   the runtime contract, and uses the framework's `IRepository<T>` /
   `IFcmsLogService` / `FcmsAuthorize` patterns correctly.
2. **Don't invent new patterns.** The framework provides:
   - `BaseModule` lifecycle hooks (`SeedDataAsync`, `OnUpgradeAsync`,
     `DropTablesAsync`, `GetMenuItems`, `GetPermissions`)
   - `IRepository<T>` + `IFcmsUnitOfWork` for EF queries
   - `IFcmsLogService` for audit rows (pass `module: ModuleIdValue`)
   - `[FcmsAuthorize(<permissionKey>)]` for action-level gating
   - `[FcmsScoped]` / `[FcmsSingleton]` / `[FcmsHostedService]` for DI
   - `Microsoft.NET.Sdk.Razor` so Views compile into the module DLL
3. **Two on-disk paths for modules — they are NOT the same.**
   | Path | Purpose |
   |---|---|
   | `Modules/<ModuleId>/` (solution root) | **Dev source** — csproj + code (git-tracked) |
   | `src/FlexCms.Host/Modules/<ModuleId>/` | **Runtime drop-in** — DLL + module.json from the admin Upload flow (gitignored) |
   The runtime scanner reads both. New modules you build live in **solution-root `Modules/`** — sibling of `src/`, `samples/`, `tests/`.
4. **Use the scaffold, don't hand-write the skeleton.** It writes Razor SDK
   csproj + all stub files with correct token replacement and auto-registers
   the new project in `FlexCms.slnx`. Hand-rolled csprojs miss subtle pieces
   (e.g. `AddRazorSupportForMvc`) and you'll spend hours debugging "view not
   found" 404s.

---

## 1. Three-line scaffold

> Replace `FlexCms.MyMod` / `mymod` with the real module id + table prefix.
> ModuleId rule: `^[A-Za-z][A-Za-z0-9]*(\.[A-Za-z][A-Za-z0-9]*)+$` (dotted PascalCase).
> TablePrefix rule: `^[a-z][a-z0-9_]*$` (lowercase snake).

```bash
# 1. Start the host in Development mode
cd src/FlexCms.Host && dotnet run --urls http://localhost:5099

# 2. Sign in → /admin/modules/scaffold → fill ModuleId + TablePrefix → Submit
#    Or POST directly (you'll need the antiforgery token from a GET first):
#    POST /admin/modules/scaffold  ModuleId=FlexCms.MyMod&TablePrefix=mymod

# 3. The scaffold creates Modules/FlexCms.MyMod/ AND adds it to FlexCms.slnx.
```

After the scaffold you have:

```
Modules/FlexCms.MyMod/
├── FlexCms.MyMod.csproj                         # Sdk.Razor, refs Framework
├── module.json                                  # Embedded resource
├── MyModModule.cs                               # BaseModule subclass
├── Data/
│   ├── MyModDbContext.cs                        # DbSet<MyModItem>
│   └── MyModDbContextDesignFactory.cs           # For `dotnet ef`
├── Services/MyModService.cs                     # IRepository wrapper
├── Controllers/
│   ├── AdminMyModController.cs                  # Full CRUD
│   └── PublicMyModController.cs                 # JSON stub
└── Views/
    ├── _ViewImports.cshtml
    └── AdminMyMod/{Index,Edit}.cshtml
```

---

## 2. Make it run (always-do checklist)

```bash
# A. Generate the initial migration — DesignFactory is already there
cd Modules/FlexCms.MyMod
dotnet ef migrations add InitialSchema

# B. Build
dotnet build

# C. Restart the host (stop with Ctrl+C, then `dotnet run` again)
#    On first activation the framework will:
#      - Apply the migration       (mymod_items table created)
#      - Seed 4 permissions        (flexcms.mymod.mymod.{view,create,edit,delete})
#      - Add the sidebar menu node (visible only with .view permission)
#      - Run SeedDataAsync         (inserts "Welcome" sample row)
```

If the host log shows `Module FlexCms.MyMod: seed completed.` you're done.
Visit `/admin/mymod` (replace `mymod` with your TablePrefix).

### If permissions are needed for non-SuperAdmin users

The scaffold's `[FcmsAuthorize(MyModPermissions.View)]` gates every admin
action. SuperAdmin bypasses all checks. To grant access to a regular role,
go to `/admin/roles/<roleId>/edit` → tick the 4 `MyMod` permissions.

---

## 3. Where to add your real domain logic

| Need | File to edit |
|---|---|
| Add fields to the entity | `Data/MyModDbContext.cs` → `MyModItem` class, then `dotnet ef migrations add Whatever` |
| Custom DB query | `Services/MyModService.cs` → use `IRepository<T>` (see § 4 below) |
| Custom admin page | New action on `AdminMyModController.cs` + matching `Views/AdminMyMod/<Action>.cshtml` |
| Public JSON endpoint | `PublicMyModController.cs` is the starter — extend it |
| Lifecycle hook (eg. version-bump backfill) | `MyModModule.cs` → override `OnUpgradeAsync` |
| Static asset (CSS/JS for public site) | `wwwroot/` inside the module folder — activation syncs to `wwwroot/modules/<ModuleId>/` |
| Background job | `[FcmsHostedService]` on a `BackgroundService` subclass — auto-registered by AttributeScanner |

---

## 4. `IRepository<T>` — the read-side cheat sheet

```csharp
// All read methods accept three optional flags + variadic eager-loads:
//   includeDeleted (default false), includeInactive (default true),
//   includes: params Expression<Func<T,object>>[]

// Active rows only:
await _repo.GetAllAsync(ct, includeInactive: false);

// Trash view (include soft-deleted):
await _repo.FindAsync(p => p.AuthorId == userId, ct, includeDeleted: true);

// Eager-load navigation:
await _repo.GetByIdAsync(id, ct, includes: x => x.Tags, x => x.Author);

// IQueryable for joins / projections / DataTables:
var q = _repo.Query(includeDeleted: false, includeInactive: true,
                    includes: x => x.Tags);
```

Writes:

```csharp
await _repo.AddAsync(entity, ct);
await _repo.UpdateAsync(entity, ct);
await _repo.SoftDeleteAsync(entity, ct);   // sets Status=Deleted, DeletedAt
await _repo.DeleteAsync(entity, ct);       // hard delete — use for audit-log-only entities
await _uow.SaveChangesAsync(ct);           // commit
```

> **Module-owned context caveat.** Host DI registers `EfRepository<T>` against
> `FcmsDbContext`. Your module's entity lives in `<MyMod>DbContext` which
> isn't in host DI. The scaffolded service handles this by constructing
> `new EfRepository<MyModItem>(ctx)` per request from
> `ModuleActivationOptions`. Don't try to inject `IRepository<MyModItem>`
> directly — it would resolve against the wrong DbContext and throw at runtime.

---

## 5. Audit logging — tag with your module id

```csharp
// In the controller, after a mutation:
await _log.LogAsync(
    action: "mymod.create",
    entityType: nameof(MyModItem),
    entityId: saved.Id.ToString(),
    value: saved,
    module: MyModModule.ModuleIdValue,   // ← critical, or it tags as "core"
    ct: ct);
```

The `module:` parameter populates `fcms_logs.Module` so admins can filter
`/admin/audit-log` by module. Auto-emitted rows from the EF interceptor
(entity Created/Updated/Deleted) read the entity's `ModuleId` property if
present, else fall back to the CLR namespace's assembly name — so they're
tagged correctly without you doing anything.

---

## 6. Permissions — naming convention

`MyModPermissions` constants must match what `GetPermissions()` returns.
The seeder prefixes every key with `{ModuleId}.` (lowercased) before
writing to `fcms_permissions`. So define both forms:

```csharp
public static class MyModPermissions
{
    // Short keys — passed to GetPermissions() / FcmsPermissionDef
    public const string ViewKey   = "mymod.view";
    public const string CreateKey = "mymod.create";

    // Fully-qualified — used in [FcmsAuthorize] attributes + checks
    public const string View   = "flexcms.mymod." + ViewKey;
    public const string Create = "flexcms.mymod." + CreateKey;
}
```

---

## 7. Done-when checklist

- [ ] `dotnet build Modules/FlexCms.MyMod/FlexCms.MyMod.csproj` → 0 warnings, 0 errors
- [ ] `dotnet ef migrations add InitialSchema` ran, migration files exist under `Migrations/`
- [ ] Host restart log shows `Module FlexCms.MyMod: migrations applied.` and `seed completed.`
- [ ] `/admin/mymod` returns 200 (with `.view` permission) or 403 (without)
- [ ] `/admin/mymod/create` form submits → row visible in list → audit log shows `mymod.create` tagged `Module = FlexCms.MyMod`
- [ ] `dotnet test --nologo` from repo root still passes (661 unit + 296 integration)
- [ ] If the module ships static assets: `wwwroot/modules/FlexCms.MyMod/<file>.css` resolves on a 200 after activation

---

## 8. Don't do these

- ❌ Inject `IRepository<MyModItem>` directly — wrong DbContext. Use the scaffolded service.
- ❌ Hand-write the csproj. Use the scaffold; you'll forget `<AddRazorSupportForMvc>true</AddRazorSupportForMvc>` and views won't render.
- ❌ Skip the `module:` arg to `LogAsync`. Defaults to `"core"`, breaking audit filtering.
- ❌ Add the module project to `FlexCms.Host.csproj` as a `ProjectReference`. Modules are runtime-loaded, not compile-time linked. The scaffold adds it to `FlexCms.slnx` (solution file) instead, which is correct.
- ❌ Put files under `src/FlexCms.Host/Modules/`. That folder is for **uploaded ZIPs**; dev source belongs in solution-root `Modules/`.
- ❌ Use `FcmsOk()` / `FcmsFail()` from a non-AJAX POST handler. They return JSON; the browser will render the raw envelope on a blank page. Use `RedirectToAction` + `TempData["Success"]` for normal form submits.
- ❌ Edit Sample.Hello when building a new module. Copy it conceptually, don't fork it. The sample exists as a reference, not a starting point.

---

## 9. Reference files (read these if stuck)

| Topic | File |
|---|---|
| Canonical module shape | `samples/FlexCms.Sample.Hello/HelloModule.cs` |
| Controller + service pattern | `samples/FlexCms.Sample.Hello/Controllers/HelloAdminController.cs` + `Services/GreetingService.cs` |
| Razor SDK csproj | `samples/FlexCms.Sample.Hello/FlexCms.Sample.Hello.csproj` |
| Permission constants | `samples/FlexCms.Sample.Hello/HelloModule.cs` (bottom) |
| BaseModule API | `src/FlexCms.Framework/Modules/BaseModule.cs` + `IFcmsModule.cs` |
| Repository surface | `src/FlexCms.Framework/Db/IRepository.cs` |
| Audit logging | `src/FlexCms.Framework/Cms/IFcmsLogService.cs` |
| Deeper module concepts | `docs/MODULE_DEV.md` |

---

## 10. Reporting back

When you finish, state explicitly:

1. The `ModuleId` and `TablePrefix` you used
2. The files you added or modified, grouped (controller / view / service / entity / migration)
3. The migration name (`dotnet ef migrations add <Name>`)
4. Whether you ran the full test suite and the result
5. Anything you skipped or deferred (e.g. "no PublicController endpoints yet — TODO")
