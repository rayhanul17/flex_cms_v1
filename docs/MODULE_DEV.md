# Module Development Guide

> Full module spec — see [`plan.md`](plan.md) **Issue 30, 37b, 110** + **PART 0.7** (Ecommerce primitives).

---

## 🚀 Quick Start

### Scaffold a new module

```bash
# Option A — CLI template (install once from repo root):
dotnet new install ./templates/flexcms-module
dotnet new flexcms-module -n MyCompany.Blog --TablePrefix blog -o modules/MyCompany.Blog

# Option B — Admin UI (Development env only):
# http://localhost:5000/admin/modules → [+ Create New Module] → fill form → submit
# Folder is generated at modules/{ModuleId}/ automatically.
```

### Internal module (you have repo source access)

```bash
# Add to solution:
dotnet sln add modules/MyCompany.Blog/MyCompany.Blog.csproj

# Reference Framework:
dotnet add modules/MyCompany.Blog/MyCompany.Blog.csproj reference src/FlexCms.Framework

# Run with dotnet watch — auto-reload on code changes:
cd src/FlexCms.Host
dotnet watch run
```

### External module (third-party developer)

```bash
# Reference framework via NuGet (no source needed):
dotnet add package FlexCms.Framework

# Build + ship as ZIP:
dotnet publish -c Release -o publish/
cd publish && zip -r ../MyCompany.Blog.zip . && cd ..
# Distribute MyCompany.Blog.zip → admin uploads via /admin/modules
```

---

## 📁 Module Structure

```
MyCompany.Blog/
├── MyCompany.Blog.csproj
├── BlogModule.cs                # IFcmsModule implementation
├── module.json                   # Manifest (embedded resource)
├── Permissions/
│   └── BlogPermissions.cs        # Permission constants
├── Models/
│   ├── Entities/                 # IBaseEntity entities
│   └── Dtos/                     # Form/API DTOs
├── Services/                     # Business logic ([FcmsScoped])
├── Controllers/Admin/            # [FcmsAuthorize] admin controllers
├── Views/Admin/                  # .cshtml views (runtime-compiled)
├── Migrations/                   # EF Core migrations
├── wwwroot/
│   ├── css/blog.css
│   └── js/blog.js
└── Resources/
    ├── Strings.en.resx
    └── Strings.bn.resx
```

---

## 📋 module.json

```json
{
  "ModuleId": "MyCompany.Blog",
  "ModuleName": "Blog",
  "Version": "1.0.0",
  "Author": "Your Name",
  "Description": "Blog posts + categories",
  "MinFrameworkVersion": "1.0.0",
  "TablePrefix": "blog",
  "DependsOn": [],
  "RequestedPermissions": ["filesystem.write:uploads/blog", "email.send"],
  "DockerSupport": {
    "BakeIn": true,
    "MinHostImageVersion": "1.0.0"
  },
  "ProvidesApis": [
    { "Interface": "MyCompany.Blog.PublicApi.IBlogPublicApi", "Version": "1.0.0" }
  ]
}
```

---

## 🧬 Minimum Module Code

```csharp
public class BlogModule : BaseModule
{
    public override string ModuleId => "MyCompany.Blog";
    public override string ModuleName => "Blog";
    public override string Version => "1.0.0";

    public override void RegisterServices(IServiceCollection services) {
        services.AddScoped<PostService>();
        // ... your services ...
    }

    public override List<FcmsPermissionDef> GetPermissions() => new() {
        new(BlogPermissions.PostCreate, "Create Post", group: "Blog"),
        new(BlogPermissions.PostEdit,   "Edit Post",   group: "Blog"),
    };
}

public static class BlogPermissions {
    public const string PostCreate = "blog.post.create";
    public const string PostEdit   = "blog.post.edit";
}
```

---

## 🔑 Module Rules (CRITICAL)

### ✅ Allowed:
- Inject `IRepository<T>` (provider-agnostic)
- Inject Framework services (`IFcmsEmailService`, `IFcmsHookManager`, `IFcmsContextService`)
- Inject Core services (`PermissionService`, `SettingsService`, `MediaService`)
- Use `BaseAdminController` for admin pages
- Define entities, services, controllers, views, migrations
- Subscribe to hooks via `IFcmsHookManager.Register`
- Expose your own `IFcmsModuleApi` interface for cross-module use (Issue 110)

### ❌ Forbidden:
- Direct service injection from another custom module
- DLL/project reference to another custom module
- Calling `services.AddSingleton<IFcmsAiProvider>(...)` (override Framework defaults — only Framework/Core do this)
- File system writes outside declared `RequestedPermissions`
- Modifying Framework or Core source

### 📐 Cross-module communication:
**Only via hooks** OR module API registry. NO direct dependencies.

```csharp
// Publish:
await _hookManager.ExecuteAsync(FcmsHooks.PostPublished, post);

// Consume (decoupled):
_hookManager.Register(FcmsHooks.PostPublished, async (payload, ct) => {
    var post = (FcmsPost)payload;
    await _newsletterService.SendAsync(post, ct);
});

// Cross-module API call (decoupled, version-aware):
var blogApi = _moduleApiRegistry.Get<IBlogPublicApi>();   // null if Blog inactive
if (blogApi != null) {
    var posts = await blogApi.GetRecentAsync(5, ct);
}
```

---

## 🗄 Module Migrations

### EF Core (MySQL/Postgres/MSSQL):

```bash
cd modules/MyCompany.Blog
dotnet ef migrations add InitialBlogSchema -c BlogMigrationDbContext -o Migrations
```

Bundle migrations in your DLL — Framework auto-applies on activation.

### MongoDB:

Implement `IFcmsMongoIndexBuilder.BuildAsync()` — Framework calls during activation:

```csharp
public class BlogMongoIndexBuilder : IFcmsMongoIndexBuilder {
    public async Task BuildAsync(IMongoDatabase db, CancellationToken ct) {
        var posts = db.GetCollection<FcmsPost>("fcms_posts");
        await posts.Indexes.CreateOneAsync(new CreateIndexModel<FcmsPost>(
            Builders<FcmsPost>.IndexKeys.Ascending(p => p.Slug),
            new CreateIndexOptions { Unique = true }), cancellationToken: ct);
    }
}
```

---

## 🌐 i18n

Bundle `Resources/Strings.en.resx` + `Resources/Strings.bn.resx` in your module. Framework auto-loads.

```razor
@inject IFcmsTranslator T
<h1>@T.Get("BlogPosts")</h1>
```

Lookup chain: Module resx → Core resx fallback → key itself (never blank).

---

## 📦 Packaging for Distribution

```bash
# Build:
cd modules/MyCompany.Blog
dotnet publish -c Release -o publish/

# ZIP structure must contain:
# bin/      — DLL + transitive NuGet deps
# Views/    — .cshtml files
# wwwroot/  — static assets
# module.json — manifest
cd publish && zip -r ../MyCompany.Blog.zip . && cd ..
```

Drop ZIP into `/modules/` Docker volume OR upload via Admin UI.

---

## 🐳 Docker — Two Strategies

### Strategy A: Bake into image (production, immutable)

```dockerfile
# Dockerfile.with-modules
FROM ghcr.io/rayhanul17/flexcms:latest
COPY modules/MyCompany.Blog /app/modules/MyCompany.Blog
```

### Strategy B: Volume mount (staging/dev, hot-drop)

```bash
docker cp MyCompany.Blog.zip flexcms_flexcms_1:/app/modules/
docker exec flexcms_flexcms_1 unzip /app/modules/MyCompany.Blog.zip -d /app/modules/MyCompany.Blog/
# Then admin activates → 5-15s container restart → live
```

---

## ✅ Pre-publish Checklist

- [ ] `module.json` complete (ModuleId, Version, MinFrameworkVersion)
- [ ] `RequestedPermissions` declared honestly
- [ ] All async methods accept `CancellationToken`
- [ ] All entities follow `IBaseEntity` (Guid Id, audit fields)
- [ ] DTOs (NOT entities) used for form/API binding
- [ ] HTML content sanitized via `HtmlSanitizer.Sanitize()` (in `FlexCms.Framework.Cms`) before save
- [ ] `SeedDataAsync` is **idempotent** (uses `UpsertAsync`, not `InsertAsync`)
- [ ] i18n strings in resx files, no hardcoded English/Bangla
- [ ] Migrations tested on a fresh DB
- [ ] No reference to other custom modules' DLLs
- [ ] Cross-module needs go through `IFcmsHookManager` OR module API registry
- [ ] Tested on both EF (MySQL) AND MongoDB providers (if module is provider-agnostic)

---

## Attribute Reference

Quick reference for the attributes module authors are expected to use.
All defined in `FlexCms.Framework`.

### `[FcmsAuthorize(permission?)]`

Source: `Auth/FcmsAuthorizeAttribute.cs` — applied on a controller or
action. When omitted, just requires authentication. Permission
expressions support `&` (AND) and `|` (OR), e.g.
`[FcmsAuthorize("posts.create&posts.publish")]`. SuperAdmin bypasses
every check.

```csharp
[FcmsAuthorize(FcmsPermissions.PostsEdit)]
public async Task<IActionResult> Edit(Guid id) { ... }
```

### `[FcmsLog(action, entityType, entityIdParam = "id", module = "core")]`

Source: `Auth/FcmsLogAttribute.cs` — automatic audit-log entry on
successful 2xx redirect or JSON action result. Reads the entity id
from the named route/query parameter (default `id`); use
`FcmsLogContext.SetEntityId(id)` inside the action when the value
isn't on the URL.

```csharp
[HttpPost("create")]
[FcmsLog("post.create", "FcmsPost")]
[FcmsAuthorize(FcmsPermissions.PostsCreate)]
public async Task<IActionResult> Create(PostVm vm) { ... }
```

### `[FcmsLogIgnore]`

Source: `Cms/FcmsLogIgnoreAttribute.cs` — applied on a property.
Causes `FcmsLogJsonResolver` to strip the property from the audit
JSON snapshot. Use for sensitive or noisy fields. The framework
already strips Identity-managed fields (`PasswordHash`,
`SecurityStamp`, `ConcurrencyStamp`, etc.) and navigation
collections automatically — `[FcmsLogIgnore]` is for module-defined
fields the framework can't infer.

```csharp
public class CustomerData : BaseEfEntity
{
    public string Name { get; set; } = "";
    [FcmsLogIgnore] public string InternalNotes { get; set; } = "";
}
```

### `[FcmsTable(name)]`

Source: `Helpers/FcmsHelper.cs` — applied on an entity class.
Overrides the auto-generated table name (`{prefix}_{snake_case_plural}`)
when you need a specific table name (legacy compat, naming conflicts).
Use sparingly — the convention is the path of least surprise.

```csharp
[FcmsTable("legacy_users")]
public class FcmsUser : IdentityUser<Guid> { ... }
```

### `[FcmsScoped(serviceType?)]` / `[FcmsSingleton(serviceType?)]` / `[FcmsHostedService]`

Source: `Modules/Attributes/FcmsLifetimeAttributes.cs` — applied on a
service class. The module loader scans the assembly for these and
registers them automatically with the right lifetime. Pass an
explicit interface to register against if your class implements
multiple interfaces.

```csharp
[FcmsScoped(typeof(IBlogService))]
public class BlogService : IBlogService, IDisposable { ... }

[FcmsHostedService]
public class BlogIndexerService : BackgroundService { ... }
```

### `[FcmsModuleApi(version, DisplayName?)]`

Source: `Modules/Api/FcmsModuleApiAttribute.cs` — applied on an
interface that other modules will resolve through `IFcmsModuleApiRegistry`.
Version follows SemVer; bump major on breaking changes. Consumers
specify a constraint when calling `Get<T>(">=1.2.0")`.

```csharp
[FcmsModuleApi("1.2.0", DisplayName = "Blog Public API")]
public interface IBlogPublicApi
{
    Task<List<Post>> GetRecentAsync(int n);
}

// Consumer side (in another module):
var blog = registry.Get<IBlogPublicApi>(">=1.0.0");
if (blog is not null)
{
    var posts = await blog.GetRecentAsync(5);
}
```

---

## Tag Helper Reference

All tag helpers ship with `FlexCms.Framework`. Add
`@addTagHelper *, FlexCms.Framework` to `_ViewImports.cshtml` (the
host already does this).

| Tag | Purpose | Example |
|---|---|---|
| `<button fcms-authorize="key">` | Hide element when user lacks permission | `<button fcms-authorize="users.delete">Delete</button>` |
| `<fcms-honeypot />` | Renders an off-screen honeypot field pair to deter bots | `<form>... <fcms-honeypot /> </form>` |
| `<fcms-env-banner />` | Colored "DEVELOPMENT" / "STAGING" banner; hidden in Production | Place once in `_AdminLayout.cshtml` |
| `<fcms-picture src alt widths sizes>` | `<picture>` with WebP + responsive srcset + lazy fallback | `<fcms-picture src="/uploads/hero.jpg" alt="Hero" widths="640,1024,1920" />` |
| `<fcms-data-table url page-length>` | Server-side jQuery DataTable with permission-filtered actions | See `MediaController.Index` |
| `<fcms-row-actions entity-id base-url>` | Permission-filtered edit / delete / restore row buttons | Used inside DataTable templates |
