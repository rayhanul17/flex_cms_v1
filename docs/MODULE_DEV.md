# FlexCMS Module Developer Guide

Build a module once, drop it into any FlexCMS host, restart — that's the contract.
This guide walks you through the mandatory contract, recommended structure, and
every helper / hook you can lean on.

> **Handing module work to an AI agent?** Start it with
> [`AGENT_MODULE_GUIDE.md`](AGENT_MODULE_GUIDE.md) — a focused, action-oriented
> distillation of the bits an AI needs to ship a working module from scratch.

---

## 1. What is a module?

A **module** is a single .NET DLL that:

1. Implements `IFcmsModule` (one type per assembly).
2. Ships a `module.json` manifest embedded as a resource.
3. Lives in a folder under the host's `modules/` directory.

When the host starts it scans `modules/*/`, loads each DLL, runs migrations,
seeds permissions + menu items, and adds the assembly as an MVC
ApplicationPart — so your controllers and Razor views become routable
without any host-side wiring.

---

## 2. Mandatory checklist

A module **must** provide:

| # | Item | Where |
|---|------|-------|
| 1 | `module.json` (embedded) | `<EmbeddedResource Include="module.json" />` in `.csproj` |
| 2 | One class implementing `IFcmsModule` (or `BaseModule`) | anywhere in the assembly |
| 3 | A unique `ModuleId` — convention: `Vendor.Domain` (`FlexCms.Investment`, `Acme.Crm`) | manifest + class property |
| 4 | A `TablePrefix` — short snake-case, used for every table the module owns | manifest + class property |
| 5 | A SemVer `Version` string | manifest + class property |

Everything else (entities, controllers, permissions, menu items) is optional —
override the relevant `BaseModule` hook when you need it.

---

## 3. Install / upload

Two supported flows:

### A. Upload via admin UI (production / customer site)

1. Build & publish the module to a folder:
   ```bash
   dotnet publish -c Release -o ./publish
   ```
2. ZIP **the contents** of `./publish` (DLL + `module.json` + dependencies).
   The archive root must contain `module.json` (or contain a single folder that
   does — both are accepted).
3. Sign in as SuperAdmin → **Modules** → **Upload Module** → pick the `.zip`.
   Tick *Overwrite* if you're replacing an earlier version.
4. Click **Restart now** in the banner. The module is loaded on the next boot.

### B. Drop folder during development

```
modules/
└── FlexCms.Module.Investment/
    ├── FlexCms.Module.Investment.dll
    ├── module.json
    └── ... (referenced dependencies)
```

Restart `dotnet watch run` and the module appears under **Admin → Modules**.

> Don't put your `bin/Debug/` output directly under `modules/` — copy the
> publish output, or use the `+ Create New Module` scaffold which sets the
> right layout for you.

---

## 4. Scaffold a new module

The fastest path:

1. Sign in as SuperAdmin → **Modules** → **+ Create New Module** (Development
   environment only).
2. Fill in:
   - **ModuleId**: dotted identifier (e.g. `FlexCms.Module.Investment`)
   - **TablePrefix**: snake-case prefix (e.g. `invest`)
3. The scaffold writes a runnable project to **solution-root `modules/<ModuleId>/`**
   (sibling of `src/`, `samples/`, `tests/` — NOT inside `src/FlexCms.Host/modules/`,
   that runtime folder is for ZIP uploads only). Both `modules/` folders are
   **gitignored in the parent repo** because each module is its own git
   repository — you'll `cd modules/<ModuleId> && git init` (or `git remote add`)
   to track its source separately. Contents:
   - `<ShortName>Module.cs` — entry point
   - `Data/<ShortName>DbContext.cs` + sample entity
   - `Data/<ShortName>DbContextDesignFactory.cs` — for `dotnet ef`
   - `Services/<ShortName>Service.cs` — wraps `EfRepository<T>(NewDb())`
   - `Controllers/Admin<ShortName>Controller.cs` — full CRUD
   - `Controllers/Public<ShortName>Controller.cs` — JSON endpoint stub
   - `Views/Admin<ShortName>/{Index,Edit}.cshtml` + `Views/_ViewImports.cshtml`
   - `module.json` (embedded), permissions + menu items + `DropTablesAsync()` wired

### Opening the new project in your IDE

The scaffold does **not** auto-register the new csproj in `FlexCms.slnx` —
that file is git-tracked and modules are separate repos, so a reference to
a path that doesn't exist for other developers would break solution load.
Two options:

- **Open the module standalone.** From the module folder, run
  `dotnet build` / `dotnet test` / `code .` — VS Code's C# Dev Kit
  detects the lone csproj and gives you full IntelliSense without
  involving the parent solution.
- **Add it to your local solution manually.** Right-click the
  **Solution** node in Visual Studio (not the Host project) →
  *Add → Existing Project* → pick `modules/<ModuleId>/<ModuleId>.csproj`.
  The change stays in your local `.slnx` (or revert before committing).

> **⚠ "Add Existing Item" ≠ "Add Existing Project".** In Visual Studio,
> right-clicking the **Host project** and choosing *Add → Existing Item*
> only adds a *file* to that project (and hides `.csproj` files by
> default — so you'll see only `.gitkeep` in the dialog and think the
> folder is empty). What you actually want is the Solution-level
> *Add → Existing Project* command described above.

### After the scaffold

```bash
cd modules/FlexCms.Module.Investment
git init                              # each module is its OWN git repo
dotnet ef migrations add InitialSchema
dotnet build
```

Restart the host. The framework discovers the DLL (it scans BOTH the
solution-root `modules/` and `src/FlexCms.Host/modules/`), applies the
migration, seeds permissions, adds the menu entry, and your controller
goes live at `/admin/<TablePrefix>`.

---

## 5. Lifecycle hooks

Every hook on `IFcmsModule` has a no-op default in `BaseModule` — override only
what your module needs.

| Hook | Called | Purpose |
|---|---|---|
| `RegisterServices(services)` | At host startup, once | DI registration that attributes can't express (typed HttpClients, options binding) |
| `CreateMigrationContext(connStr, provider)` | Each restart | Return your module's EF `DbContext` so the framework runs `MigrateAsync()` |
| `GetPermissions()` | Each restart | Declare permissions; framework upserts into `fcms_permissions` (prefixed `{moduleId}.`) |
| `GetMenuItems()` | Each restart | Sidebar entries; framework upserts and soft-deletes on deactivate |
| `SeedDataAsync(sp, ct)` | Once after first activation | Initial data — guaranteed idempotent by the flag `FcmsModuleRecord.SeedCompleted` |
| `OnUpgradeAsync(fromVersion, sp, ct)` | When version in DB ≠ manifest version | Data migrations between versions |
| `DropTablesAsync(connStr, provider, ct)` | On uninstall, if "Drop tables" was ticked | Drop every table this module owns. `EnsureDeletedAsync()` is the easy correct answer for module-owned contexts |

Anything that throws is captured on `FcmsModuleRecord.ActivationError` and
surfaced as a red "Error" badge with the message in **Admin → Modules** — the
other modules still finish activating.

---

## 6. Permissions

```csharp
public override List<FcmsPermissionDef> GetPermissions() =>
[
    new(InvestPermissions.ViewKey,   "View Investments",   "Investments"),
    new(InvestPermissions.CreateKey, "Create Investments", "Investments"),
    new(InvestPermissions.EditKey,   "Edit Investments",   "Investments"),
    new(InvestPermissions.DeleteKey, "Delete Investments", "Investments"),
];

public static class InvestPermissions
{
    public const string ViewKey   = "invest.view";
    public const string CreateKey = "invest.create";
    public const string EditKey   = "invest.edit";
    public const string DeleteKey = "invest.delete";

    // Fully-qualified — what [FcmsAuthorize(...)] takes.
    // Must mirror the {ModuleId}. prefix the framework writes (lowercased).
    public const string View   = "flexcms.module.investment." + ViewKey;
    public const string Create = "flexcms.module.investment." + CreateKey;
    public const string Edit   = "flexcms.module.investment." + EditKey;
    public const string Delete = "flexcms.module.investment." + DeleteKey;
}
```

Use on controllers / actions:

```csharp
[FcmsAuthorize(InvestPermissions.View)]
[Route("admin/invest")]
public class AdminInvestController : BaseFcmsController { ... }

[HttpPost("create")]
[FcmsAuthorize(InvestPermissions.Create)]
public async Task<IActionResult> Create(...) { ... }
```

SuperAdmin bypasses every permission check.

---

## 7. Menu items

```csharp
public override List<FcmsMenuItemDef> GetMenuItems() =>
[
    new FcmsMenuItemDef
    {
        DefaultName = "Investments",
        Icon = "bi bi-graph-up",
        Url = "/admin/invest",
        Order = 500,
        RequiredPermission = InvestPermissions.View
    }
];
```

Menu items are soft-deleted on `Deactivate` and restored on the next activate.

---

## 8. Controller base class — `BaseFcmsController`

Module controllers inherit `BaseFcmsController` (framework-level) which gives
you:

```csharp
// Toast / flash messages (TempData-backed across redirects)
ShowSuccess("Saved.");
ShowError("Could not connect.");
ShowWarning("Low balance.");
ShowInfo("Heads up.");

// Full-control overload — append to the previous toast, override duration, hide close button
ShowMessage("Step 1 done", FcmsMessageType.Info, appendMessage: true, durationSeconds: 8);

// AJAX response envelope used by fcms-actions.js
return FcmsOk("Created.", new { id = item.Id });
return FcmsFail("Validation failed.");

// Logger scoped to this controller (Serilog category)
Logger.LogInformation("Created investment {Id}", item.Id);

// Cache + session shorthand
SetCache("foo", value, TimeSpan.FromMinutes(10));
SetSession("wizardState", model);
```

Admin controllers can also inherit `BaseAdminController` (host) which adds the
global `[FcmsAuthorize]` gate plus a `DataTableResult<>` helper for server-side
DataTables.

---

## 9. Helpers you can lean on

All under `FlexCms.Framework.Helpers` — pure, static, allocation-light.

| Helper | When to use |
|---|---|
| `FcmsHelper` | Table naming, slug, snake-case, pluralize, base64-url, enum to dictionary, page-password hash |
| `FcmsStringHelper` | Truncate, StripHtml, FirstWords, Capitalize, NormalizeWhitespace, SmartUrlEncode, Mask |
| `FcmsUrlHelper` | Parse URL paths into `(area, controller, action)`, combine, isAbsolute |
| `FcmsPhoneHelper` | Country-aware mobile validation (`BD` default, `IN`, `US` built-in, `Register(...)` for more) |
| `FcmsTypeConverter` | `ParseInt/Long/Decimal/Bool/Guid/DateTime/Enum` + nullable variants — null-safe, invariant culture |
| `FcmsReflectionHelper` | `GetIdValue<TId>`, `IsBaseEntity`, `CreateList`, nav-property discovery |
| `FcmsRuntimeHelper` | OS detection, framework description, assembly version |
| `FcmsEmbeddedResourceHelper` | Read embedded text / JSON resources from a module DLL |

Framework services every module can inject:

| Service | Where | What it does |
|---|---|---|
| `IFcmsExcelService` | `FlexCms.Framework.Documents` | Server-side Excel export — ClosedXML 0.105.0 (MIT) backed |
| `IFcmsPdfService` | `FlexCms.Framework.Documents` | Programmatic PDF — QuestPDF 2026.2.4 (MIT Community, free) |
| `IMediaService` | `FlexCms.Framework.Cms` | Validated file upload (magic-byte MIME check, thumbnails, DB-tracked) — returns `{Id, Url, ThumbnailUrl}` |
| `IFcmsFileStorage` | `FlexCms.Framework.Storage` | Raw file I/O abstraction (use when you don't need the media library overhead) |
| `IFcmsLogService` | `FlexCms.Framework.Cms` | Audit log (`OpLog` on `BaseAdminController`) |
| `IFcmsTranslator` | `FlexCms.Framework.I18n` | i18n lookup (uses the embedded `Resources/i18n/*.json`) |
| `ISettingsService` | `FlexCms.Framework.Services` | Per-key settings — auto-encrypts values flagged as sensitive |
| `IFcmsContextService` | `FlexCms.Framework.Services` | Current user + IP + browser parsed via UAParser |

### Quick examples

**Excel export**

```csharp
public AdminInvestController(IFcmsExcelService excel, IRepository<Investment> repo) { ... }

[HttpGet("export")]
public async Task<IActionResult> Export(CancellationToken ct)
{
    var rows = await repo.GetAllAsync(ct);
    var bytes = await excel.RenderTableAsync(
        sheetName: "Investments",
        headers: ["Investor", "Amount", "Status", "Created"],
        rows: rows.Select(r => new object?[] { r.InvestorName, r.Amount, r.Status, r.CreatedAt })
                  .ToList());
    return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "investments.xlsx");
}
```

**PDF receipt**

```csharp
public AdminInvestController(IFcmsPdfService pdf) { ... }

[HttpGet("receipt/{id:guid}")]
public async Task<IActionResult> Receipt(Guid id, CancellationToken ct)
{
    var bytes = await pdf.RenderTextAsync(
        title: "Investment Receipt",
        lines: [$"Investor: {item.InvestorName}", $"Amount: {item.Amount:C}", $"Date: {item.CreatedAt:yyyy-MM-dd}"]);
    return File(bytes, "application/pdf", $"receipt-{id}.pdf");
}
```

**File upload through the Media library**

```csharp
public AdminInvestController(IMediaService media) { ... }

[HttpPost("upload-proof")]
[ValidateAntiForgeryToken]
public async Task<IActionResult> UploadProof(IFormFile file, CancellationToken ct)
{
    if (file is null || file.Length == 0) return FcmsFail("Pick a file.");
    var result = await media.UploadAsync(file, folderId: null, ct);
    // result = { Id, Url, ThumbnailUrl, MimeType, ... } — store result.Id on your entity
    return FcmsOk("Uploaded.", new { url = result.Url, id = result.Id });
}
```

**Excel import (parse uploaded `.xlsx`)**

```csharp
public AdminInvestController(IFcmsExcelService excel) { ... }

[HttpPost("import")]
[ValidateAntiForgeryToken]
public async Task<IActionResult> Import(IFormFile file, CancellationToken ct)
{
    if (file is null || file.Length == 0) return FcmsFail("Pick an .xlsx file.");

    // Strongly-typed: ParseAsync<T> maps each row by header name (case-insensitive).
    // Use [FcmsExcelColumn("Custom Header")] on a property when the workbook
    // header doesn't match your DTO property name.
    using var stream = file.OpenReadStream();
    var rows = await excel.ParseAsync<InvestmentImportRow>(stream, ct: ct);

    foreach (var row in rows)
        await repo.AddAsync(new Investment { InvestorName = row.Name, Amount = row.Amount });
    await uow.SaveChangesAsync(ct);
    return FcmsOk($"{rows.Count} row(s) imported.");
}

public class InvestmentImportRow
{
    [FcmsExcelColumn("Investor Name")] public string Name { get; set; } = "";
    public decimal Amount { get; set; }
}
```

**Raw SQL → DTO (reports & aggregates)**

```csharp
public AdminInvestController(InvestmentDbContext db) { ... }

[HttpGet("report")]
public async Task<IActionResult> Report(CancellationToken ct)
{
    var summary = await FcmsSqlHelper.QueryAsync<InvestmentSummaryDto>(
        db,
        @"SELECT InvestorEmail, COUNT(*) AS InvestmentCount, SUM(Amount) AS TotalAmount
          FROM investments
          WHERE Status <> 404
          GROUP BY InvestorEmail
          ORDER BY TotalAmount DESC",
        ct: ct);
    return Json(summary);
}

public class InvestmentSummaryDto
{
    public string InvestorEmail { get; set; } = "";
    public int InvestmentCount { get; set; }
    public decimal TotalAmount { get; set; }
}
```

`FcmsSqlHelper` also exposes `ScalarAsync<T>()` for single-value queries and
`ExecuteAsync()` for non-query (INSERT/UPDATE/DELETE/DDL) statements. All
parameter-binding goes through `DbParameter` — never string-concatenate user
input into the SQL.

Enum descriptions:

```csharp
[Description("Pending review")]
public enum InvestmentStatus { Pending, Approved, Rejected }

// In a view / controller:
var label = FcmsHelper.GetEnumDescription(item.Status);          // "Pending review"
var dropdown = FcmsHelper.EnumToSelectList<InvestmentStatus>();  // for <select>
```

---

## 9.5 Background services + scheduling

Module-side background work uses the standard ASP.NET Core `BackgroundService`
type plus the `[FcmsHostedService]` attribute, which the framework's attribute
scanner picks up automatically. No manual registration — drop the class into
the assembly and it's wired on activation.

```csharp
using FlexCms.Framework.Hosting;
using FlexCms.Framework.Modules.Attributes;
using Microsoft.Extensions.Hosting;

[FcmsHostedService]
public sealed class InvestmentNightlyReport : BackgroundService
{
    // Cron fields: minute hour day-of-month month day-of-week
    // Cron.* exposes ready-made helpers so you don't have to remember the syntax.
    private readonly FcmsScheduledTask _schedule = new(Cron.DailyAt(hour: 2, minute: 0));

    private readonly IServiceScopeFactory _scopes;
    public InvestmentNightlyReport(IServiceScopeFactory scopes) => _scopes = scopes;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // One-minute polling — cheap and IIS / Docker friendly. The scheduled
        // task's ShouldRun() guard ensures we only fire once per matching minute.
        while (!ct.IsCancellationRequested)
        {
            if (_schedule.ShouldRun(DateTime.UtcNow))
            {
                await using var scope = _scopes.CreateAsyncScope();
                // var svc = scope.ServiceProvider.GetRequiredService<IInvestmentReportService>();
                // await svc.GenerateNightlyReportAsync(ct);
            }

            try { await Task.Delay(TimeSpan.FromMinutes(1), ct); }
            catch (TaskCanceledException) { return; }
        }
    }
}
```

Common ready-made schedules:

| Helper | Cron expression | Fires |
|---|---|---|
| `Cron.EveryMinute`        | `* * * * *`       | every minute (testing only) |
| `Cron.EveryFiveMin`       | `*/5 * * * *`     | every 5 minutes |
| `Cron.EveryFifteenMin`    | `*/15 * * * *`    | every 15 minutes |
| `Cron.Hourly`             | `0 * * * *`       | once per hour at :00 |
| `Cron.HourlyAtMinute(15)` | `15 * * * *`      | once per hour at :15 |
| `Cron.DailyAt(2, 30)`     | `30 2 * * *`      | 02:30 every day |
| `Cron.WeeklyAt(1, 9, 0)`  | `0 9 * * 1`       | Mondays at 09:00 |
| `Cron.MonthlyAt(1, 0, 0)` | `0 0 1 * *`       | first of the month at 00:00 |

> Multi-node deployments need an outer "did anyone already run this minute?"
> coordination lock — `FcmsScheduledTask` is intentionally in-process.

---

## 9.6 Indexing module tables

Indexes live in the module's own `DbContext.OnModelCreating` and ship as part
of the same EF migration as the entity:

```csharp
protected override void OnModelCreating(ModelBuilder b)
{
    base.OnModelCreating(b);

    b.Entity<Investment>().HasIndex(x => x.InvestorEmail);
    b.Entity<Investment>().HasIndex(x => new { x.Status, x.CreatedAt });
    b.Entity<Investment>().HasIndex(x => x.AccountNumber).IsUnique();
}
```

Run `dotnet ef migrations add AddInvestmentIndexes` once and commit the
generated migration files alongside the module's source. `ModuleActivationService`
applies them on the next host restart.

---

## 10. UI API — toasts, confirms, AJAX actions

The host ships a small JS library available on every page (admin and public):

```js
// Toasts
fcms.toast.success("Saved.");
fcms.toast.danger("Failed.", { duration: 8000, closeButton: false });
fcms.toast.warning("Low balance.", { appendMessage: true });

// Confirm modal — promise-based
const ok = await fcms.confirm({
  title: "Delete investor?",
  body:  "This cannot be undone.",
  okText: "Delete",
  okClass: "btn-danger"
});
if (ok) { /* delete */ }

// Async loader / overlay
fcms.loader.show("Saving…");
try { await save(); } finally { fcms.loader.hide(); }

// DataTables config wrapper — see fcms-datatable.js
fcms.datatable.init('#grid', { ajaxUrl: '/admin/invest/datatable', columns: [...] });
```

`fcms-actions.js` auto-handles `<button data-fcms-action="...">` declarative
delete / activate / deactivate patterns. The button posts to the configured
URL, shows a toast from the JSON envelope, and reloads the row.

---

## 11. Audit logging

Inject `IFcmsLogService` or use `BaseAdminController.OpLog`:

```csharp
await OpLog.LogAsync("invest.create", nameof(Investment), item.Id.ToString(),
    value: item, module: "FlexCms.Module.Investment", ct: ct);
```

Module operations (Upload / Activate / Deactivate / Uninstall) are audited
automatically by the framework — your CRUD logs are additive.

---

## 12. Drop tables (uninstall hygiene)

`EnsureDeletedAsync()` is the simplest correct implementation when your module
owns its `DbContext`:

```csharp
public override async Task DropTablesAsync(string connectionString, string provider, CancellationToken ct = default)
{
    var ctx = CreateMigrationContext(connectionString, provider);
    if (ctx is null) return;
    await using (ctx) await ctx.Database.EnsureDeletedAsync(ct);
}
```

This runs **only** when the admin ticks *Drop all database tables* on the
uninstall dialog — by default uninstall keeps the data so the customer can
re-install later.

---

## 13. Sample module

A complete, working reference lives under `samples/FlexCms.Sample.Hello/`:
manifest, entity, DbContext, admin CRUD controller, permissions, menu, drop
tables, `[FcmsScoped]` attribute-registered service. Copy its layout when in
doubt.

---

## 14. Common pitfalls

- **Forgot `<EmbeddedResource Include="module.json" />`** → the loader skips
  your DLL with a "no manifest" log entry. Always check this first.
- **`ModuleId` casing changes between manifest and class** → registry uses the
  manifest's `ModuleId`; permission seeding lowercases everything for the
  prefix. Keep them identical to avoid confusion.
- **`Permission keys` don't match `[FcmsAuthorize(...)]`** → seeding writes
  `{moduleId}.{key}` (lowercase); the attribute must use the same string.
  Encode it as a constant.
- **EF migration fails because the connection string in `setup.json` is wrong**
  → `ActivationError` will say so; fix the setting and restart.
- **Old `bin/Debug/` artefacts left in `modules/<id>/`** → the loader may pick
  the wrong DLL. Always `dotnet publish` the module folder fresh.
