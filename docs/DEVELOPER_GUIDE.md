# FlexCms — Complete Developer Guide

This guide takes you from **zero to production**. Follow each step in order. Examples included.

---

## 📋 Table of Contents

1. [Prerequisites](#1-prerequisites)
2. [First Time Setup](#2-first-time-setup)
3. [Daily Development Workflow](#3-daily-development-workflow)
4. [Running Tests](#4-running-tests)
5. [What Is Built — Phases 1 to 5 Status](#5-what-is-built--phases-1-to-5-status)
6. [Creating a New Module](#6-creating-a-new-module)
7. [Building and Packaging](#7-building-and-packaging)
8. [Local Testing with Docker](#8-local-testing-with-docker)
9. [Deploying to Production](#9-deploying-to-production)
10. [Updating an Existing Production Server](#10-updating-an-existing-production-server)
11. [Module Deployment to Production](#11-module-deployment-to-production)
12. [Troubleshooting](#12-troubleshooting)

---

## 1. Prerequisites

### Install on your development machine

| Tool | Why | Where to get |
|---|---|---|
| **.NET 10 SDK** | Build the app | https://dotnet.microsoft.com/download |
| **Git** | Version control | https://git-scm.com |
| **Docker Desktop** | Local DB + containers | https://www.docker.com/products/docker-desktop |
| **Visual Studio 2022** OR **JetBrains Rider** OR **VS Code** | Code editor | Pick one |
| **GitHub CLI (`gh`)** | Easier PR creation | https://cli.github.com |

### Check everything is installed:

```bash
dotnet --version       # Should print 10.0.x
git --version          # Should print git version 2.x.x
docker --version       # Should print Docker version 24.x.x
gh --version           # Should print gh version 2.x.x
```

If any of these fail — install that tool first.

---

## 2. First Time Setup

### Step 1: Clone the repo

```bash
# Clone to D:\flex_cms_v1 (Windows) or ~/flex_cms_v1 (Mac/Linux)
cd D:\
git clone https://github.com/rayhanul17/flex_cms_v1.git
cd flex_cms_v1
```

### Step 2: Set your git identity (one time only)

```bash
git config user.name "Your Name"
git config user.email "your-email@example.com"
```

### Step 3: Verify you're on the `main` branch

```bash
git branch          # Shows: * main
git status          # Shows: nothing to commit, working tree clean
```

### Step 4: Copy environment template

```bash
# Windows PowerShell:
copy .env.example .env

# Linux/Mac:
cp .env.example .env

# Open .env and fill in real values (DB password, etc.)
notepad .env        # Windows
nano .env           # Linux/Mac
```

### Step 5: Start local databases via Docker

```bash
docker compose -f docker/docker-compose.dev.yml up -d
```

This starts:
- **MySQL** on `localhost:3306`
- **PostgreSQL** on `localhost:5432`
- **MongoDB** on `localhost:27017` (replica set mode)
- **Mailhog** SMTP test server on `localhost:1025` (UI at `http://localhost:8025`)

Verify they're running:

```bash
docker ps
# You should see 4 containers running
```

### Step 6: Build the solution

```bash
dotnet restore FlexCms.slnx
dotnet build FlexCms.slnx
```

If build fails — check the error. Most common: missing NuGet package, fix by running `dotnet restore` again.

### Step 7: Run the app for the first time

```bash
cd src/FlexCms.Host
dotnet watch run
```

Open browser: `http://localhost:5000`

You'll see the **Setup Wizard** (first run only):
1. **Database** — pick MySQL, enter `localhost:3306` + credentials → Test Connection
2. **Site Info** — name, tagline, base URL
3. **Admin Account** — your email + strong password
4. **Done** — wait for restart, then login at `/auth/login`

**Done!** You now have FlexCms running locally.

### Default Seeded Accounts

On first startup the system automatically creates these accounts:

| Role | Email | Password | Access |
|---|---|---|---|
| **SuperAdmin** | Set during Setup Wizard | Your chosen password | Full admin access at `/admin` |
| **Visitor** | `visitor@flexcms.local` | `Visitor@123` | Can submit blog comments |

> **Note:** The Visitor account is for testing the public comment workflow. Delete or change its password before going to production, or leave it — it only has `comments.submit` permission.

---

## 3. Daily Development Workflow

This is the workflow you'll use **every day**.

### The Golden Rule

> Never commit directly to `main`. Always create a feature branch.

### Step 1: Pull latest changes from main

```bash
git checkout main
git pull origin main
```

### Step 2: Create a feature branch

Branch name format: `<type>/<short-description>`

| Type prefix | When to use | Example |
|---|---|---|
| `feature/` | New feature | `feature/blog-comments` |
| `fix/` | Bug fix | `fix/login-redirect-loop` |
| `chore/` | Maintenance, deps, docs | `chore/update-readme` |
| `hotfix/` | Urgent production fix | `hotfix/payment-webhook` |
| `refactor/` | Code restructure | `refactor/extract-cart-service` |

```bash
git checkout -b feature/blog-comments
```

### Step 3: Do your work — write code, test locally

Keep the dev server running in another terminal:

```bash
cd src/FlexCms.Host
dotnet watch run    # Auto-reloads on file save
```

### Step 4: Format code before committing

CI runs `dotnet format --verify-no-changes` and **fails the build on any formatting violation** (extra alignment whitespace, indentation drift, missing newlines, etc.). Run the auto-fix locally before you commit so you don't get bounced by CI:

```bash
dotnet format FlexCms.slnxx
```

This rewrites files in place to match the project's formatting rules. If the command modifies anything, stage those changes too.

To check without modifying files (same command CI runs):

```bash
dotnet format FlexCms.slnxx --verify-no-changes --severity warn
```

Empty output = clean. Non-zero exit = something to fix.

> Common cause of CI format failures: aligning `=` signs across multiple lines for visual readability. The formatter collapses them to single spaces. Either accept that, or run `dotnet format` after writing the code.

### Step 5: Commit your changes

We use **Conventional Commits**:

```
<type>(<scope>): <short summary>

[optional longer description]
```

Examples:

```bash
git add .
git commit -m "feat(blog): add comment moderation queue"
git commit -m "fix(auth): redirect to admin after Google OAuth login"
git commit -m "chore(deps): update EF Core to 10.0.1"
git commit -m "docs(readme): clarify module install steps"
```

### Step 6: Push your branch

```bash
git push -u origin feature/blog-comments
```

The `-u` flag links your local branch to the remote one (only needed first time).

### Step 7: Create a Pull Request (PR)

```bash
gh pr create --base main --title "feat(blog): comment moderation queue" --body "Adds moderation UI for blog comments."
```

OR open the GitHub page that's printed in the terminal output and click "Create Pull Request".

### Step 8: Wait for CI to pass

GitHub Actions runs:
- Build (`dotnet build`)
- Tests (`dotnet test`)
- Format check (`dotnet format --verify-no-changes`)

If any fails — fix locally, commit, push. CI re-runs automatically.

### Step 9: Merge to main

Once CI is green, click **"Merge"** on GitHub. This triggers auto-deploy to production (via the Docker workflow).

### Step 10: Clean up

```bash
git checkout main
git pull origin main
git branch -d feature/blog-comments    # delete local branch
```

The remote branch is deleted automatically by GitHub when you click "Delete branch" in the PR.

---

## 4. Running Tests

The project has two test projects. Understanding when each needs Docker saves a lot of confusion.

---

### Test Projects at a Glance

| Project | Location | What it tests | Needs Docker? |
|---|---|---|---|
| `FlexCms.Tests.Unit` | `tests/FlexCms.Tests.Unit/` | Pure logic — no DB, no HTTP | ❌ No |
| `FlexCms.Tests.Integration` | `tests/FlexCms.Tests.Integration/` | Some tests use EF InMemory (no Docker); some use real MySQL + MongoDB | ⚠️ Only some |

---

### Which integration tests need Docker?

| Phase folder | DB used | Needs Docker? |
|---|---|---|
| `Phase1/` (`Phase1VerificationTests.cs`) | Real MySQL + real MongoDB via Testcontainers | ✅ Yes — Docker must be running |
| `Phase3/` (`PermissionServiceTests.cs`) | EF InMemory (in-process, no container) | ❌ No |

Testcontainers automatically starts and stops Docker containers for you. You will see log lines like:

```
[testcontainers.org] Delete Docker container 1f07e5c085d1
```

This is **normal and expected** — Testcontainers spins up a fresh MySQL/MongoDB container per test class, then deletes it when done. It is not an error.

---

### Commands

#### Run all unit tests (no Docker needed)

```bash
dotnet test tests/FlexCms.Tests.Unit/FlexCms.Tests.Unit.csproj
```

#### Run only Phase 3 unit tests

```bash
dotnet test tests/FlexCms.Tests.Unit/FlexCms.Tests.Unit.csproj --filter "Phase3"
```

#### Run all integration tests (Docker must be running for Phase 1)

```bash
dotnet test tests/FlexCms.Tests.Integration/FlexCms.Tests.Integration.csproj
```

#### Run only Phase 3 integration tests (no Docker needed)

```bash
dotnet test tests/FlexCms.Tests.Integration/FlexCms.Tests.Integration.csproj --filter "Phase3"
```

#### Run everything — all projects at once

```bash
dotnet test FlexCms.slnxx
```

This runs all tests. Phase 1 integration tests will start Docker containers automatically.

#### Run by phase (any phase number)

```bash
dotnet test FlexCms.slnxx --filter "Phase1"
dotnet test FlexCms.slnxx --filter "Phase3"
dotnet test FlexCms.slnxx --filter "Phase4"
```

#### Verbose output

```bash
dotnet test FlexCms.slnxx --logger "console;verbosity=normal"
```

#### Run a single specific test by name

```bash
dotnet test tests/FlexCms.Tests.Unit/FlexCms.Tests.Unit.csproj --filter "DisplayName~HasPermission"
```

---

### Setting up Docker for integration tests (Phase 1)

Testcontainers pulls MySQL and MongoDB images from Docker Hub on first run. This takes a few minutes once, then the images are cached locally.

**Prerequisites:**
- Docker Desktop installed and running
- You are logged in to Docker (`docker login`)

**Verify Docker is ready:**

```bash
docker ps
# Should print a list (even if empty). If it errors — Docker is not running.
```

**First run** (images download automatically — takes ~2-5 minutes):

```bash
dotnet test tests/FlexCms.Tests.Integration/FlexCms.Tests.Integration.csproj --filter "Phase1"
```

**Subsequent runs** are fast (~30 seconds) because images are cached.

---

### Why Phase 3 tests do not need Docker

Phase 3 tests (`PermissionServiceTests.cs`) use the **EF Core InMemory provider** — a fake in-memory database that lives entirely inside the test process. No containers, no ports, no network. This is intentional:

- Permission logic does not depend on any DB-specific SQL features
- Tests run fast (~500 ms for all 14)
- Any developer can run them with zero setup

Phase 1 tests verify that the EF + MongoDB repositories work correctly against real databases, which is why they need real containers.

---

### Quick reference

| Goal | Command |
|---|---|
| Run all unit tests | `dotnet test tests/FlexCms.Tests.Unit/FlexCms.Tests.Unit.csproj` |
| Run all integration tests | `dotnet test tests/FlexCms.Tests.Integration/FlexCms.Tests.Integration.csproj` |
| Run Phase 3 only (no Docker) | `dotnet test tests/FlexCms.Tests.Integration/FlexCms.Tests.Integration.csproj --filter "Phase3"` |
| Run Phase 1 only (needs Docker) | `dotnet test tests/FlexCms.Tests.Integration/FlexCms.Tests.Integration.csproj --filter "Phase1"` |
| Run everything | `dotnet test FlexCms.slnx` |
| Run by test name pattern | `dotnet test FlexCms.slnx --filter "DisplayName~<keyword>"` |

---

## 5. What Is Built — Phases 1 to 5 Status

This section tells you exactly what is done, what is not done yet, and what patterns to follow when working in the existing code.

---

### Phase 1 — Project Scaffold + DB Layer ✅ Done

Everything in the DB layer is implemented and tested.

**What exists:**
- `IBaseEntity`, `BaseEfEntity`, `BaseMongoEntity` — all entity base classes. Every entity automatically gets: `Id` (Guid), `CreatedAt`, `UpdatedAt`, `CreatedBy` (Guid?), `UpdatedBy` (Guid?), `IsDeleted`, `DeletedAt`
- `IRepository<T>`, `EfRepository<T>`, `MongoRepository<T>` — generic CRUD
- `IFcmsUnitOfWork`, `EfUnitOfWork`, `MongoUnitOfWork` — transaction + save coordination
- `FcmsDbContext` — EF Core context with Identity tables + soft-delete filters
- `MongoDbSerializerSetup` — GUID as binary UUID (Standard/subtype 4), DateTime as Unix milliseconds
- `SetupHelper`, `SetupConfig` — reads/writes `App_Data/setup.json`
- `FcmsServiceExtensions.AddFlexCms()` — single call registers everything
- Phase 1 integration tests pass (uses TestContainers — Docker required)

**Audit fields — auto-injected, no code needed:**

`CreatedAt`, `UpdatedAt`, `CreatedBy`, `UpdatedBy` are set automatically on every save:

| Field | Added | Modified | Background service |
|---|---|---|---|
| `CreatedAt` | `FcmsTime.Now` | — | `FcmsTime.Now` |
| `UpdatedAt` | `FcmsTime.Now` | `FcmsTime.Now` | `FcmsTime.Now` |
| `CreatedBy` | current user ID (`??=` — keeps pre-set value) | — | `null` |
| `UpdatedBy` | current user ID | current user ID | `null` |

- HTTP request → user ID from `ClaimTypes.NameIdentifier`
- Background service (no `HttpContext`) → `null` (means "system operation")
- To create a record as a specific user from code: set `entity.CreatedBy = someId` before save — the `??=` rule keeps it

**EF Migrations — how to run them for this project:**

The `FcmsDbContext` lives in `FlexCms.Framework`. Run migrations from there:

```bash
# Add a new migration (run from repo root)
dotnet ef migrations add <MigrationName> \
    --project src/FlexCms.Framework \
    --startup-project src/FlexCms.Host

# Apply migrations to DB (dev)
dotnet ef database update \
    --project src/FlexCms.Framework \
    --startup-project src/FlexCms.Host

# Generate SQL script for review before applying to prod
dotnet ef migrations script \
    --project src/FlexCms.Framework \
    --startup-project src/FlexCms.Host \
    --idempotent \
    --output migrations.sql
```

You need the EF CLI tool installed:

```bash
dotnet tool install --global dotnet-ef
```

**App_Data folder** — created automatically on first run, never commit to git:

```
App_Data/
├── setup.json      # DB config + site settings from Setup Wizard
├── keys/           # DataProtection keyring (encrypted at rest)
└── logs/           # Serilog rolling daily logs (kept 30 days)
```

---

### Phase 2 — Auth + Security Core ✅ Done

Everything is implemented. No test suite written yet for Phase 2 specifically (manual testing covers it via the Setup Wizard flow).

**What exists:**
- `FcmsUser`, `FcmsRole` — Identity entities (Guid primary key)
- `EfUserStore`, `EfRoleStore` — EF Identity stores
- `MongoUserStore`, `MongoRoleStore` — MongoDB Identity stores (full feature parity including 2FA token stores)
- Cookie auth — 8-hour sliding window, HttpOnly, Secure, SameSite=Strict
- `FcmsPasswordValidator` — reads password policy from `SiteSettings` at runtime
- `FcmsExceptionMiddleware` — catches all unhandled exceptions, logs via Serilog, shows friendly error page
- `SecurityHeadersMiddleware` — CSP, X-Frame-Options, X-Content-Type-Options
- `ForcePasswordChangeMiddleware` — redirects to `/auth/change-password` if flag set on user
- `IpFilterMiddleware` — admin whitelist + global blacklist with wildcard support
- Rate limiting — IP-partitioned: `login` policy (10/min/IP), `otp` policy (5/min/IP)
- `AuthController` — Login, Logout, ForgotPassword, ResetPassword, VerifyOtp, ChangePassword
- `FcmsValidator` — BD mobile regex, email regex, normalization to `+8801XXXXXXXXX`
- `FcmsPasswordValidator`, `FcmsValidator` — in `FlexCms.Framework/Validators/`

**Important: rate limit policies are named.** Use these exact names when adding `[EnableRateLimiting]` to controllers:

```csharp
[EnableRateLimiting("login")]   // 10 requests/min/IP
[EnableRateLimiting("otp")]     // 5 requests/min/IP
```

---

### Phase 3 — User / Role / Permission ✅ Done

Fully implemented and tested (25 unit tests + 14 integration tests, all passing).

**What exists:**
- `FcmsPermission`, `FcmsRolePermission` entities (in `FlexCms.Framework/Auth/`)
- `IPermissionService`, `PermissionService` — 15-min cache per role, DB fallback
- `PermissionExpression.Evaluate()` — parses `"a&b"` (AND), `"a|b"` (OR), single key
- `FcmsAuthorizeAttribute` + `FcmsAuthorizeFilter` — async authorization filter
- `FcmsAuthorizeTagHelper` — `fcms-authorize="key"` hides HTML elements
- `IFcmsContextService`, `FcmsContextService` — current user info + browser/OS via UAParser
- `BaseAdminController` — all admin controllers extend this
- `UserController`, `RoleController`, `PermissionController` under `/admin`
- Admin views for User and Role management

**How to protect a controller or action:**

```csharp
[FcmsAuthorize]                          // login required only
[FcmsAuthorize("posts.create")]          // login + has permission
[FcmsAuthorize("posts.create&posts.edit")] // login + has BOTH
[FcmsAuthorize("posts.create|posts.edit")] // login + has either one
```

**How to hide a button or element in a view:**

```html
<button fcms-authorize="posts.delete">Delete</button>
<!-- hidden if user lacks "posts.delete" permission -->

<a fcms-authorize="users.create|users.edit" asp-action="Manage">Manage Users</a>
```

**How to get the current user in a controller:**

```csharp
// In any controller that extends BaseAdminController:
var userId   = FcmsContext.UserId;       // Guid?
var username = FcmsContext.Username;     // string
var isSuper  = FcmsContext.IsSuperAdmin; // bool
var ip       = FcmsContext.IpAddress;    // string
```

**How to seed permissions for a new feature:**

Call `SeedPermissionsAsync` from `SeedService` or your module's `SeedDataAsync`. Pass a list of `FcmsPermission` objects — the service skips keys that already exist (idempotent):

```csharp
await _permissionService.SeedPermissionsAsync(new[]
{
    new FcmsPermission { Key = "posts.create", Group = "Posts", DisplayName = "Create Post" },
    new FcmsPermission { Key = "posts.edit",   Group = "Posts", DisplayName = "Edit Post"   },
    new FcmsPermission { Key = "posts.delete", Group = "Posts", DisplayName = "Delete Post" },
});
```

**BaseAdminController helpers — what's available:**

```csharp
// Cache
GetCache<T>("key")
SetCache("key", value, TimeSpan.FromMinutes(5))
RemoveCache("key")

// Session (JSON-serialized)
GetSession<T>("key")
SetSession("key", value)
RemoveSession("key")

// Toast messages (survive redirect via TempData)
ShowSuccess("Saved successfully.");
ShowError("Something went wrong.");
ShowWarning("Check your input.");
ShowInfo("FYI: this will take a moment.");

// AJAX JSON responses
return FcmsOk("User activated.", data: new { id = user.Id });
return FcmsFail("User not found.", errors: new[] { "ID invalid" });
```

**Login URL after setup:** `http://localhost:5000/auth/login` — use the admin email and password you set in the Setup Wizard.

**Admin view location convention** — admin controllers live under `Controllers/Admin/` (e.g. `UserController`, `RoleController`), and their views live under `Views/Admin/{Controller}/{Action}.cshtml`. Razor's default search only checks `/Views/{Controller}/`, so `Program.cs` adds `/Views/Admin/{Controller}/{Action}.cshtml` to `ViewLocationFormats`. When adding a new admin controller, just put its views in the matching `Views/Admin/{Controller}/` folder — no extra config needed.

**Cookie auth scheme** — registered under `IdentityConstants.ApplicationScheme` (`"Identity.Application"`), not the default `"Cookies"`. This is required because we use `AddIdentityCore()` (lighter than `AddIdentity()`), which does not auto-register Identity's cookie schemes. `SignInManager.PasswordSignInAsync` targets that exact scheme name.

---

### Phase 3.5 — Role Redirect + CMS Settings ✅ Done

**What exists:**

#### Role-based login redirect
Every `FcmsRole` now has two new fields:

| Field | Type | Purpose |
|---|---|---|
| `LoginRedirectUrl` | `string` | Where users with this role go after login. Empty = "/" |
| `Priority` | `int` | Tie-breaker when user has multiple roles. Higher wins. |

**Rules:**
- **SuperAdmin** always goes to `/admin` — hard-coded, ignores `LoginRedirectUrl`
- **`returnUrl` query param** takes priority over role redirect (e.g. `/auth/login?returnUrl=/admin/posts`)
- **Multiple roles** → highest `Priority` wins. If tied — first match from `UserManager.GetRolesAsync` wins
- **No roles / empty URL** → falls back to `"/"`

**Admin UI:** `/admin/roles` — new Priority + Login Redirect columns. Edit button per role. Create form now includes both fields.

#### CMS Settings page (`/admin/settings`)
A central settings page where per-installation configuration lives. Currently contains:

| Setting | Key | Default |
|---|---|---|
| Audit logging on/off | `audit:enabled` | `true` |

More settings will be added here over time (email SMTP, maintenance mode, etc.).

**Audit log toggle** was moved from the Audit Log page to Settings. The Audit Log page (`/admin/audit-log`) now has a "Settings" button that links there.

**Permissions used:**

| Permission | What it guards |
|---|---|
| `settings.view` | Can open `/admin/settings` |
| `settings.manage` | Can toggle audit logging (and future settings changes) |
| `roles.edit` | Can edit role name / redirect URL / priority |

#### Integration tests
7 new tests in `Phase6/LoginRedirectTests.cs` — no Docker needed (EF InMemory):
- `SuperAdmin_redirects_to_admin_regardless_of_redirect_url`
- `Single_role_with_redirect_url_uses_that_url`
- `Single_role_with_empty_redirect_url_falls_back_to_slash`
- `Multiple_roles_highest_priority_wins`
- `Multiple_roles_same_priority_first_non_empty_url_used`
- `No_roles_falls_back_to_slash`
- `SuperAdmin_wins_even_with_lower_priority_than_other_role`

---

### Phase 4 — Module System ✅ Done

**What exists:**
- **Naming convention** — `FcmsHelper.GetTableName<T>(modulePrefix)` produces prefix + snake_case + plural. `[FcmsTable("custom_name")]` overrides.
- **Module abstractions** — `IFcmsModule`, `BaseModule`, `ModuleManifest` (deserialized `module.json`).
- **Lifecycle hooks** — `CreateMigrationContext()` → EF `MigrateAsync()` at startup; `SeedDataAsync()` once per install; `OnUpgradeAsync(fromVersion)` on version change; `DropTablesAsync()` on uninstall with "Drop Tables".
- **Discovery pipeline** — `ModuleLoader` + `ModuleManager` (topological sort by `DependsOn`, cycle detection) + `ModuleRegistry` singleton.
- **Persistence** — `FcmsModuleRecord` (`fcms_module_records`): tracks `SeedCompleted`, `Version`, `Status`, `ActivatedAt`.
- **DI auto-scan** — `[FcmsScoped]`, `[FcmsSingleton]`, `[FcmsHostedService]` attributes; `AttributeScanner` runs per module assembly.
- **Wiring** — `AddFlexCms()` calls `RegisterServices()`, `AttributeScanner`, and `AddApplicationPart()` per active module.
- **`IFcmsModelBuilder`** — modules implement this to inject their entities into the shared `FcmsDbContext`. Register as `services.AddSingleton<IFcmsModelBuilder, MyModelBuilder>()`.
- **`ModuleActivationService`** (IHostedService) — runs at every startup per active module: wwwroot sync → `MigrateAsync()` → `SeedDataAsync()` (if not done) → `OnUpgradeAsync()` (if version changed).
- **Admin UI** — `/admin/modules`: list with activate / deactivate / uninstall (type name + optional Drop Tables checkbox). Restart button. Dev-only `[+ Create New Module]` scaffold button.
- **wwwroot sync** — `{moduleFolder}/wwwroot/` → `{webRoot}/modules/{moduleId}/` on activate; deleted on deactivate/uninstall.
- **`dotnet new flexcms-module`** — template at `templates/flexcms-module/`. Install with `dotnet new install ./templates/flexcms-module`. Creates module class, DbContext, `module.json`, `.csproj`.
- **Admin scaffold UI** — Development env only: `/admin/modules/scaffold` generates a new module folder from the template.

**Practical usage:** drop a built module folder under `modules/` → restart → Admin → Modules → Activate. The framework runs migrations, seeds data, syncs static assets, and makes the module's routes live — all automatically.

---

### Phase 5 — CMS: Pages + Posts + Frontend ✅ Done

**What exists:**

#### Entities (`FlexCms.Framework/Cms/`)
- `FcmsPage` — hierarchical pages (`ParentId` self-ref), `Slug`, `Content`, `IsPublished`, `PublishedAt`, `AccessControl`, `PasswordHash`
- `FcmsCategory` — hierarchical categories (`ParentId` self-ref)
- `FcmsPost` — blog posts with `CategoryId` FK, `ViewCount`, `FeaturedImageUrl`
- `FcmsTag` + `FcmsPostTag` — many-to-many tags (junction table `fcms_post_tags`)
- `FcmsRedirect` — URL redirects with `FromPath`, `ToPath`, `StatusCode`, `HitCount`, `IsActive`
- All CMS entities have `DeletedAt` (set on soft delete; used by trash cleanup)

#### Services
| Service | Interface | Key methods |
|---|---|---|
| `PageService` | `IPageService` | CRUD + `GetBySlugAsync`, `GetPublishedAsync`, `GetDeletedAsync`, `RestoreAsync`, `HardDeleteAsync` |
| `PostService` | `IPostService` | CRUD + tag sync + `IncrementViewCountAsync`, `GetDeletedAsync`, `RestoreAsync`, `HardDeleteAsync` |
| `CategoryService` | `ICategoryService` | CRUD + `GetBySlugAsync` |

All services sanitize `Content` via `HtmlSanitizer.Sanitize()` before save.

#### Security — HTML Sanitizer
`HtmlSanitizer` (in `FlexCms.Framework/Cms/`) strips dangerous tags (`<script>`, `<style>`, `<iframe>`, `<form>`, etc.), `on*` event attributes, and `javascript:` hrefs. Called automatically by `PageService` and `PostService` — module developers should call it too:

```csharp
using FlexCms.Framework.Cms;

var safe = HtmlSanitizer.Sanitize(userProvidedHtml);
```

#### Background Services
| Service | Interval | What it does |
|---|---|---|
| `ScheduledPublishService` | 1 minute | Publishes pages/posts where `IsPublished=false` and `PublishedAt <= now` |
| `TrashCleanupService` | 24 hours | Hard-deletes trashed items older than `TrashRetentionDays` (default 30, configurable via `FlexCmsOptions`) |

#### Page Access Control
`FcmsPage.AccessControl` is a `PageAccessControl` enum:

| Value | Behaviour |
|---|---|
| `Public` | Anyone can view |
| `AuthenticatedOnly` | Redirect to `/auth/login` if not logged in |
| `PasswordProtected` | Show password form; session key set on correct entry |

Password stored as SHA-256 hex. Set via admin page edit form.

#### Scheduling Pages/Posts
Set `IsPublished = false` + `ScheduledAt = <future datetime>` in the create/edit form. `ScheduledPublishService` auto-publishes when the time arrives. `PublishedAt` stores the actual publish time (set to `FcmsTime.Now` on immediate publish, or to the scheduled time when using the scheduler).

**Always use `FcmsTime.Now` — never `DateTime.UtcNow`** — so the site timezone is respected.

#### Admin Controllers (all under `/admin`)
| Route | Controller | Permissions |
|---|---|---|
| `/admin/pages` | `PageController` | `pages.create/edit/delete` |
| `/admin/categories` | `CategoryController` | `categories.create/edit/delete` |
| `/admin/posts` | `PostController` | `posts.create/edit/delete` |
| `/admin/trash` | `TrashController` | `pages.edit/delete`, `posts.edit/delete` |
| `/admin/redirects` | `RedirectController` | `redirects.create/edit/delete` |

#### Frontend Routes
| URL | Controller | Notes |
|---|---|---|
| `/{slug}` | `FrontendController.Page` | CMS pages; enforces AccessControl |
| `/blog` | `BlogController.Index` | Published posts list |
| `/blog/{slug}` | `BlogController.Post` | Post detail + view count increment |
| `/blog/category/{slug}` | `BlogController.Category` | Filtered by category |
| `/search?q=` | `SearchController.Index` | Title + content search across pages + posts |
| `/sitemap.xml` | `SitemapController` | XML sitemap, 1h cache |
| `/rss` | `RssController` | RSS 2.0, latest 50 posts, 1h cache |

#### RedirectMiddleware
Registered after `SecurityHeadersMiddleware`, before `IpFilterMiddleware`. Checks GET/HEAD requests against `fcms_redirects` table. On match: issues 301/302 and fire-and-forgets a `HitCount` increment (via `ExecuteUpdateAsync` in a new scope — does not block the response).

#### Trash System
- Soft delete sets `IsDeleted = true` + `DeletedAt = FcmsTime.Now`
- Admin trash UI at `/admin/trash`: restore (sets `IsPublished = false`, clears `IsDeleted`) or hard delete
- `TrashCleanupService` permanently removes items where `DeletedAt < now - RetentionDays`
- Configure retention: `FlexCmsOptions.TrashRetentionDays = 30` (default)

#### DbContext CMS Configuration
`FcmsDbContext` includes all CMS entities. Key points:
- Pages and Categories: `Restrict` cascade on self-ref FK (prevents accidental cascade delete of hierarchies)
- Posts → Category: `SetNull` on delete
- PostTags: composite PK `(PostId, TagId)`, `Cascade` on both FKs, no soft-delete
- Redirects: unique index on `FromPath`
- Global soft-delete query filter on all `BaseEfEntity` types — use `.IgnoreQueryFilters()` to query trash

---

## 5.x — Known Pending Features (Not Yet Implemented)

These features are planned but not yet built. Do not look for them in the codebase — controllers, views, and menu entries do not exist yet.

| Feature | Notes |
|---|---|
| **Module zip upload** | Admin UI to upload a `.zip` file and install a new module at runtime. Currently modules must be deployed manually (copy folder + restart). |
| **Theme zip install** | Admin UI to upload a theme `.zip`. Currently themes are deployed manually to the `themes/` folder. |
| **Comment moderation** | `/admin/comments` — approve/spam/trash. `CommentsModerate` permission is seeded but no controller or view exists. |
| **Subscribers management** | `/admin/subscribers`. Permission seeded, no controller or view. |
| **Data export / privacy requests** | `/admin/privacy/requests`. Permission seeded, no controller or view. |
| **API tokens** | `/admin/api-tokens`. Permission seeded, no controller or view. |
| **Webhooks** | `/admin/webhooks`. Permission seeded, no controller or view. |

---

## 6. Creating a New Module

Modules are how you add features without touching the CMS core.

### Step 1: Branch from main

```bash
git checkout main && git pull
git checkout -b feature/blog-module
git push -u origin feature/blog-module
```

### Step 2: Scaffold the module

**Option A — CLI template (recommended):**

```bash
# Install the template once (from repo root):
dotnet new install ./templates/flexcms-module

# Scaffold:
dotnet new flexcms-module -n FlexCms.Blog --TablePrefix blog -o modules/FlexCms.Blog
```

**Option B — Admin UI (Development env only):**

Open `http://localhost:5000/admin/modules` → click **`[+ Create New Module]`** → fill Module ID + Table Prefix → submit. The folder is created at `modules/{ModuleId}/` automatically.

**Option C — Manual:**

```bash
mkdir modules/FlexCms.Blog
cd modules/FlexCms.Blog
dotnet new classlib -n FlexCms.Blog -f net10.0
dotnet add reference ../../src/FlexCms.Framework/FlexCms.Framework.csproj
cd ../..
dotnet sln add modules/FlexCms.Blog/FlexCms.Blog.csproj
```

### Step 3: Add the required folder structure

```
modules/FlexCms.Blog/
├── FlexCms.Blog.csproj
├── BlogModule.cs              # IFcmsModule implementation
├── module.json                 # Manifest (set as embedded resource)
├── Permissions/
│   └── BlogPermissions.cs
├── Models/
│   ├── Entities/
│   └── Dtos/
├── Services/
├── Controllers/Admin/
├── Views/Admin/
├── Migrations/
├── wwwroot/
│   ├── css/
│   └── js/
└── Resources/
    ├── Strings.en.resx
    └── Strings.bn.resx
```

### Step 4: Create the minimum required files

**`module.json`** (mark as Embedded Resource in csproj):

```json
{
  "ModuleId": "FlexCms.Blog",
  "ModuleName": "Blog",
  "Version": "1.0.0",
  "Author": "Your Name",
  "Description": "Blog posts and categories",
  "MinFrameworkVersion": "1.0.0",
  "TablePrefix": "blog",
  "DependsOn": [],
  "RequestedPermissions": ["email.send"]
}
```

**`BlogModule.cs`**:

```csharp
using Microsoft.Extensions.DependencyInjection;
using FlexCms.Framework.Modules;
using Microsoft.EntityFrameworkCore;

namespace FlexCms.Blog;

public class BlogModule : BaseModule
{
    public override string ModuleId    => "FlexCms.Blog";
    public override string ModuleName  => "Blog";
    public override string Version     => "1.0.0";
    public override string TablePrefix => "blog";

    public override void RegisterServices(IServiceCollection services)
    {
        services.AddScoped<Services.PostService>();
        // Register IFcmsModelBuilder if you share entities with FcmsDbContext:
        // services.AddSingleton<IFcmsModelBuilder, BlogModelBuilder>();
    }

    public override DbContext? CreateMigrationContext(string connectionString, string provider)
    {
        var opts = new DbContextOptionsBuilder<BlogDbContext>()
            .UseMySql(connectionString, ServerVersion.AutoDetect(connectionString))
            .Options;
        return new BlogDbContext(opts);
    }

    public override async Task SeedDataAsync(IServiceProvider sp, CancellationToken ct = default)
    {
        // Insert initial data here — called once when SeedCompleted=false
        await Task.CompletedTask;
    }

    public override async Task OnUpgradeAsync(string fromVersion, IServiceProvider sp, CancellationToken ct = default)
    {
        // Apply data migrations when Version in DB differs from current Version
        await Task.CompletedTask;
    }

    public override async Task DropTablesAsync(string connectionString, string provider, CancellationToken ct = default)
    {
        // Drop all module tables — called on uninstall with "Drop Tables" option
        await Task.CompletedTask;
    }
}
```

**`Permissions/BlogPermissions.cs`**:

```csharp
namespace FlexCms.Blog.Permissions;

public static class BlogPermissions
{
    public const string PostCreate = "blog.post.create";
    public const string PostEdit   = "blog.post.edit";
    public const string PostDelete = "blog.post.delete";
}
```

### Step 5: Update the csproj to embed module.json

Edit `FlexCms.Blog.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <EmbeddedResource Include="module.json" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\FlexCms.Framework\FlexCms.Framework.csproj" />
    <ProjectReference Include="..\..\src\FlexCms.Core\FlexCms.Core.csproj" />
  </ItemGroup>
</Project>
```

### Step 6: Run the app — the module auto-loads

```bash
cd src/FlexCms.Host
dotnet watch run
```

Open `http://localhost:5000/admin/modules` — you should see "Blog" in the list. Click **Activate**.

### Step 7: Build out your module

Add entities, services, controllers, views following [`MODULE_DEV.md`](MODULE_DEV.md).

### Step 8: Commit and PR

```bash
git add .
git commit -m "feat(blog): initial Blog module scaffold"
git push origin feature/blog-module
gh pr create --base main --title "feat: Blog module v1"
```

---

## 7. Building and Packaging

When your module is ready, you need to **package it as a ZIP** so admins can upload it.

### Step 1: Publish the module (this creates a `publish/` folder with all dependencies)

```bash
cd modules/FlexCms.Blog
dotnet publish -c Release -o publish/
```

### Step 2: Verify the publish output

```bash
ls publish/
# You should see:
# FlexCms.Blog.dll
# module.json
# (any NuGet dependency DLLs)
```

### Step 3: Add Views and wwwroot folders

The module ZIP must contain:

```
FlexCms.Blog.zip
├── module.json
├── bin/           ← contents of publish/
│   ├── FlexCms.Blog.dll
│   └── (deps)
├── Views/         ← copy from your module folder
└── wwwroot/       ← copy from your module folder
```

### Step 4: Create the ZIP

**Windows PowerShell:**

```powershell
cd modules\FlexCms.Blog
Copy-Item -Recurse Views publish\Views
Copy-Item -Recurse wwwroot publish\wwwroot
Compress-Archive -Path publish\* -DestinationPath ..\..\FlexCms.Blog-1.0.0.zip -Force
```

**Linux/Mac:**

```bash
cd modules/FlexCms.Blog
cp -r Views publish/Views
cp -r wwwroot publish/wwwroot
cd publish
zip -r ../../../FlexCms.Blog-1.0.0.zip .
```

You now have `FlexCms.Blog-1.0.0.zip` in the repo root — this is what you upload via Admin UI.

### Step 5: Test the ZIP locally

1. Stop your local dev server
2. Open `http://localhost:5000/admin/modules`
3. Click **Upload Module** → select `FlexCms.Blog-1.0.0.zip`
4. Click **Activate**
5. Wait ~10 seconds for restart
6. Verify the module routes work (e.g., `/admin/blog/posts`)

---

## 8. Local Testing with Docker

Sometimes you need to test the **full Docker setup** locally before deploying.

### Step 1: Build the Docker image locally

```bash
docker build -f docker/Dockerfile -t flexcms:local .
```

This takes ~5 minutes the first time, then cached layers make rebuilds fast.

### Step 2: Run the full production stack locally

```bash
docker compose -f docker/docker-compose.prod.yml up -d
```

**Note:** You need to fill in `.env` with real values first — see Step 4 of First Time Setup.

### Step 3: Verify it's running

```bash
docker compose -f docker/docker-compose.prod.yml ps
# All containers should show "Up (healthy)"

curl http://localhost/health/ready
# Should return: {"status":"Healthy"}
```

### Step 4: Open the site

`http://localhost` — works just like production (without TLS).

### Step 5: Stop everything when done

```bash
docker compose -f docker/docker-compose.prod.yml down
```

To also delete data volumes (full reset):

```bash
docker compose -f docker/docker-compose.prod.yml down -v
```

---

## 9. Deploying to Production

This is the **first-time** production deployment. Once done, see [Section 8](#8-updating-an-existing-production-server) for updates.

### Step 1: Get a VPS

| Provider | Plan | Monthly Cost | Notes |
|---|---|---|---|
| Hetzner | CX21 | €5.83 | Best value (EU/US/Singapore) |
| DigitalOcean | $6 droplet | $6 | Easy UI, many regions |
| Linode | Nanode | $5 | Solid alternative |
| Contabo | VPS S | $4.50 | Cheapest, EU-based |

Pick one. Get **Ubuntu 22.04 LTS** image.

### Step 2: Buy a domain + Cloudflare setup (recommended)

1. Buy domain from Namecheap, GoDaddy, or any registrar (~$10/year)
2. Sign up for free Cloudflare account
3. Add your domain to Cloudflare → it gives you 2 nameservers
4. Update your domain registrar to use Cloudflare's nameservers (takes 1-24 hours to propagate)
5. In Cloudflare → DNS → Add A record:
   - Type: A
   - Name: `@` (root)
   - Value: your VPS IP
   - Proxy: ON (orange cloud) — for free DDoS protection

### Step 3: SSH into your VPS

```bash
ssh root@your-vps-ip
```

### Step 4: Initial server setup

```bash
# Create non-root user
adduser flexcms
usermod -aG sudo flexcms

# Switch to that user
su - flexcms

# Install Docker
curl -fsSL https://get.docker.com | sh
sudo usermod -aG docker flexcms
# Logout and login again so Docker group takes effect
exit
ssh flexcms@your-vps-ip

# Install firewall + fail2ban
sudo apt update
sudo apt install -y ufw fail2ban

# Configure firewall
sudo ufw allow 22/tcp
sudo ufw allow 80/tcp
sudo ufw allow 443/tcp
sudo ufw enable

# Install certbot (for HTTPS later)
sudo apt install -y certbot
```

### Step 5: Clone the repo on the VPS

```bash
cd /opt
sudo git clone https://github.com/rayhanul17/flex_cms_v1.git flexcms
sudo chown -R flexcms:flexcms /opt/flexcms
cd /opt/flexcms
```

### Step 6: Create production `.env` file

```bash
cp .env.example .env
nano .env
```

Fill in:

```bash
DOMAIN=mysite.com
SITE_NAME=My FlexCms Site
MYSQL_ROOT_PASSWORD=use-a-very-long-random-password-here-32-chars
DB_PASSWORD=another-different-long-random-password-32-chars

FLEXCMS__ConnectionString=Server=mysql;Database=flexcms;User=flexcms;Password=<DB_PASSWORD value>
FLEXCMS__SiteName=My FlexCms Site
FLEXCMS__BaseUrl=https://mysite.com
```

Save with `Ctrl+O`, `Enter`, `Ctrl+X`.

**Generate strong passwords:**

```bash
openssl rand -base64 32    # Run twice — once for each password
```

### Step 7: Get HTTPS certificate (one time)

```bash
# Stop nginx temporarily so certbot can use port 80
sudo systemctl stop nginx 2>/dev/null

# Get certificate
sudo certbot certonly --standalone \
    -d mysite.com -d www.mysite.com \
    --email admin@mysite.com \
    --agree-tos --no-eff-email

# Copy certificates to nginx folder
sudo mkdir -p /opt/flexcms/docker/nginx/certs/live/mysite.com
sudo cp /etc/letsencrypt/live/mysite.com/fullchain.pem /opt/flexcms/docker/nginx/certs/live/mysite.com/
sudo cp /etc/letsencrypt/live/mysite.com/privkey.pem /opt/flexcms/docker/nginx/certs/live/mysite.com/
sudo chown -R flexcms:flexcms /opt/flexcms/docker/nginx/certs
```

### Step 8: Start the production stack

```bash
cd /opt/flexcms
docker compose -f docker/docker-compose.prod.yml up -d
```

Wait ~30 seconds for everything to boot.

### Step 9: Check it's working

```bash
docker compose -f docker/docker-compose.prod.yml ps
# All containers should be "Up" and healthy

curl https://mysite.com/health/ready
# Should return: {"status":"Healthy"}
```

### Step 10: Run the Setup Wizard

Open `https://mysite.com` in your browser. The Setup Wizard will guide you through:

1. **Database** — should auto-detect from `.env`
2. **Site Info** — name, tagline
3. **Admin Account** — your production admin email + strong password
4. **Done** — short restart, then login at `/auth/login`

**Production is live!** 🎉

### Step 11: Set up daily backup cron

```bash
# Edit cron
crontab -e

# Add this line:
0 3 * * * /opt/flexcms/scripts/backup.sh >> /var/log/flexcms-backup.log 2>&1
```

Backups run nightly at 3 AM and upload to Backblaze B2 (configure B2 credentials in `.env`).

### Step 12: Set up TLS auto-renewal

```bash
# Test renewal
sudo certbot renew --dry-run

# Add to cron (auto-renew every Sunday)
sudo crontab -e

# Add this:
0 3 * * 0 certbot renew --quiet --post-hook "docker compose -f /opt/flexcms/docker/docker-compose.prod.yml restart nginx"
```

---

## 10. Updating an Existing Production Server

### Option A: Automatic (via GitHub Actions — recommended)

When you merge a PR to `main`, GitHub Actions:
1. Builds new Docker image
2. Pushes to GitHub Container Registry (GHCR)
3. SSHs into your VPS
4. Pulls new image and restarts

You don't need to do anything manually.

**To enable this**, set GitHub repo secrets:
- Go to GitHub repo → Settings → Secrets and variables → Actions
- Add: `SERVER_HOST` (VPS IP), `SERVER_USER` (flexcms), `SSH_KEY` (private SSH key), `DOMAIN` (mysite.com)

### Option B: Manual update

```bash
# SSH to VPS
ssh flexcms@your-vps-ip
cd /opt/flexcms

# Before updating — turn on maintenance mode (admin UI):
# https://mysite.com/admin/settings/maintenance → Enable (auto-disable in 30 min)

# Pull latest code + Docker image
git pull origin main
docker compose -f docker/docker-compose.prod.yml pull

# Restart with new image
docker compose -f docker/docker-compose.prod.yml up -d

# Wait for health check
sleep 15
curl https://mysite.com/health/ready

# If healthy — turn off maintenance mode in admin UI
```

---

## 11. Module Deployment to Production

### Step 1: Build the module ZIP locally (see Section 5)

```bash
cd modules/FlexCms.Blog
dotnet publish -c Release -o publish/
cp -r Views publish/Views
cp -r wwwroot publish/wwwroot
cd publish
zip -r ../../../FlexCms.Blog-1.0.0.zip .
```

### Step 2: Upload via Admin UI

1. Open `https://mysite.com/admin/modules`
2. Click **Upload Module** → select `FlexCms.Blog-1.0.0.zip`
3. Validation runs (file integrity, version compatibility, dependency check)
4. Module appears in list as **"Inactive"**
5. Click **Activate**
6. **Brief downtime: 5-15 seconds** (Docker auto-restarts container)
7. Module is now live — verify by visiting its routes

### Alternative: SCP + Docker exec

For automated deploys without using Admin UI:

```bash
# Copy ZIP to server
scp FlexCms.Blog-1.0.0.zip flexcms@vps:/tmp/

# SSH in
ssh flexcms@vps

# Copy into container's modules volume
docker cp /tmp/FlexCms.Blog-1.0.0.zip flexcms_flexcms_1:/app/modules/

# Extract inside container
docker exec flexcms_flexcms_1 \
    unzip -o /app/modules/FlexCms.Blog-1.0.0.zip \
    -d /app/modules/FlexCms.Blog/

# Restart container to load module
docker compose -f /opt/flexcms/docker/docker-compose.prod.yml \
    restart flexcms

# Then login to admin UI and click Activate (one-time)
```

### Step 3: Verify in production

- Visit `https://mysite.com/admin/modules` — module shows "Active"
- Visit module routes — they should respond
- Check `https://mysite.com/admin/system/dashboard` — no errors

---

## 12. Troubleshooting

### Build errors

```bash
dotnet restore
dotnet build
```

If still failing — delete `bin/` and `obj/` folders:

**Windows:**

```powershell
Get-ChildItem -Path . -Include bin,obj -Recurse | Remove-Item -Recurse -Force
```

**Linux/Mac:**

```bash
find . -type d \( -name bin -o -name obj \) -exec rm -rf {} +
```

Then `dotnet restore && dotnet build`.

### Database connection fails

1. Is the DB container running? `docker ps`
2. Is the password in `.env` correct?
3. Can you connect manually? `mysql -h localhost -u flexcms -p`

### Module fails to activate

Check logs:

```bash
# Local
cd src/FlexCms.Host
# Look at console output where dotnet watch is running

# Production
docker logs flexcms_flexcms_1 --tail 100
```

Common causes:
- Module DLL targets wrong .NET version (must be `net10.0`)
- Missing dependency DLL in ZIP (use `dotnet publish`, not `dotnet build`)
- `module.json` missing or invalid JSON

### TLS/HTTPS not working

```bash
sudo certbot certificates    # Check expiry
sudo certbot renew --dry-run # Test renewal
```

If certs expired:

```bash
sudo certbot renew
docker compose -f /opt/flexcms/docker/docker-compose.prod.yml restart nginx
```

### Out of disk space

```bash
df -h    # Check disk usage

# Clean Docker
docker system prune -af

# Clean old logs
sudo journalctl --vacuum-time=7d

# Check FlexCms data
du -sh /opt/flexcms/App_Data/
du -sh /var/lib/docker/volumes/
```

### Container won't start

```bash
docker compose -f docker/docker-compose.prod.yml logs flexcms
# Read the error message — usually missing env var or DB unreachable
```

### Need to rollback a bad release

```bash
# Find previous Docker image tag in GHCR
# (https://github.com/rayhanul17/flex_cms_v1/pkgs/container/flex_cms_v1)

# Pull specific version
docker pull ghcr.io/rayhanul17/flex_cms_v1:<previous-sha>

# Update docker-compose.prod.yml to use that tag
# Then restart
docker compose -f docker/docker-compose.prod.yml up -d
```

### Forgot admin password

You can reset via DB directly:

```bash
docker exec -it flexcms_mysql_1 mysql -u root -p flexcms

UPDATE fcms_users SET PasswordHash = NULL WHERE Email = 'admin@mysite.com';
```

Then visit `/auth/forgot-password` and use the email reset flow.

---

## 13. NuGet Package Management

This section covers two things:
- **Publishing** the FlexCms.Framework as a NuGet package (so external developers can build modules)
- **Consuming** NuGet packages inside your modules (adding third-party libraries)

---

### 11.1 Why Publish FlexCms.Framework as NuGet?

There are two kinds of module developers:

| Developer Type | Has FlexCms source? | How they reference Framework |
|---|---|---|
| **Internal** (you, your team) | ✅ Yes — full clone | Project reference (`<ProjectReference>`) |
| **External** (third parties, marketplace authors) | ❌ No — only need framework API | NuGet reference (`<PackageReference>`) |

For external developers, you need to **publish FlexCms.Framework as a NuGet package** so they can do:

```bash
dotnet add package FlexCms.Framework
```

without cloning your full repo.

---

### 11.2 Configure FlexCms.Framework for NuGet Publishing

Edit `src/FlexCms.Framework/FlexCms.Framework.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>

    <!-- ── NuGet package settings ────────────────────────────── -->
    <GeneratePackageOnBuild>true</GeneratePackageOnBuild>
    <PackageId>FlexCms.Framework</PackageId>
    <Version>1.0.0</Version>
    <Authors>Md. Rayhanul Islam Raj</Authors>
    <Company>FlexCms</Company>
    <Description>FlexCms Framework — base abstractions for building plug-and-play modules. .NET 10 monolithic CMS for Bangladesh market.</Description>
    <PackageTags>cms;flexcms;framework;modular;dotnet10</PackageTags>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <PackageProjectUrl>https://github.com/rayhanul17/flex_cms_v1</PackageProjectUrl>
    <RepositoryUrl>https://github.com/rayhanul17/flex_cms_v1</RepositoryUrl>
    <RepositoryType>git</RepositoryType>
    <PackageReadmeFile>README.md</PackageReadmeFile>
    <IncludeSymbols>true</IncludeSymbols>
    <SymbolPackageFormat>snupkg</SymbolPackageFormat>
    <PackageReleaseNotes>Initial release — Phase 1-12 framework abstractions.</PackageReleaseNotes>
  </PropertyGroup>

  <ItemGroup>
    <None Include="..\..\README.md" Pack="true" PackagePath="\" />
  </ItemGroup>

  <!-- Your existing PackageReferences for EF Core, MongoDB.Driver, etc. -->
</Project>
```

When you `dotnet build`, this auto-generates:

- `bin/Release/FlexCms.Framework.1.0.0.nupkg` (the package)
- `bin/Release/FlexCms.Framework.1.0.0.snupkg` (debug symbols)

---

### 11.3 Choose a NuGet Feed

You have three options:

| Feed | Best For | Cost | Public? |
|---|---|---|---|
| **NuGet.org** | Open-source projects, public marketplace | Free | ✅ Public to everyone |
| **GitHub Packages** | Internal team, simple setup | Free for public repos / paid for private | Configurable |
| **Private feed** (Sonatype Nexus, JFrog, etc.) | Enterprise — full control | Paid | ❌ Private |

**Recommended for FlexCms:** Start with **GitHub Packages** (free + integrated with your existing GitHub repo). Move to NuGet.org when you want public distribution.

---

### 11.4 Publish to GitHub Packages (Recommended Start)

#### Step 1: Generate a Personal Access Token (PAT)

1. Go to GitHub → Settings → Developer settings → Personal access tokens → Tokens (classic)
2. Click **Generate new token (classic)**
3. Give it a name like `flexcms-nuget-publish`
4. Select scopes:
   - ✅ `write:packages`
   - ✅ `read:packages`
   - ✅ `delete:packages` (optional, for cleanup)
5. Copy the token (you won't see it again)

#### Step 2: Add NuGet source on your dev machine

```bash
dotnet nuget add source \
    https://nuget.pkg.github.com/rayhanul17/index.json \
    --name "github-flexcms" \
    --username rayhanul17 \
    --password <YOUR_PAT_TOKEN> \
    --store-password-in-clear-text
```

(Replace `rayhanul17` with your GitHub username.)

#### Step 3: Build and publish

```bash
cd src/FlexCms.Framework

# Build the package
dotnet build -c Release

# Push to GitHub Packages
dotnet nuget push \
    bin/Release/FlexCms.Framework.1.0.0.nupkg \
    --source "github-flexcms" \
    --api-key <YOUR_PAT_TOKEN>
```

You should see: `Your package was pushed.`

Verify: visit `https://github.com/rayhanul17?tab=packages` — your package appears.

---

### 11.5 Publish to NuGet.org (Public Distribution)

#### Step 1: Create NuGet.org account

1. Go to https://www.nuget.org → Sign in (uses Microsoft account)
2. Profile → API Keys → **Create**
3. Key name: `flexcms-publish`
4. Glob pattern: `FlexCms.*` (allows publishing all FlexCms.* packages)
5. Expiration: 365 days
6. Copy the API key

#### Step 2: Push to NuGet.org

```bash
cd src/FlexCms.Framework
dotnet build -c Release

dotnet nuget push \
    bin/Release/FlexCms.Framework.1.0.0.nupkg \
    --api-key <YOUR_NUGET_API_KEY> \
    --source https://api.nuget.org/v3/index.json
```

Wait ~5 minutes — package appears at `https://www.nuget.org/packages/FlexCms.Framework`.

#### Step 3: External developers use it

```bash
dotnet add package FlexCms.Framework
```

That's it. They don't need access to your source code.

---

### 11.6 Auto-Publish via GitHub Actions

Manual publishing is tedious. Automate it via GitHub Actions.

Create `.github/workflows/nuget-publish.yml`:

```yaml
name: NuGet Publish

on:
  push:
    tags:
      - 'framework-v*.*.*'   # Triggers on tags like framework-v1.0.0

jobs:
  publish:
    runs-on: ubuntu-latest
    permissions:
      contents: read
      packages: write
    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET 10
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Extract version from tag
        id: version
        run: echo "version=${GITHUB_REF#refs/tags/framework-v}" >> $GITHUB_OUTPUT

      - name: Build & pack
        run: |
          cd src/FlexCms.Framework
          dotnet build -c Release /p:Version=${{ steps.version.outputs.version }}
          dotnet pack -c Release --no-build /p:Version=${{ steps.version.outputs.version }} -o ./nupkg

      - name: Publish to GitHub Packages
        run: |
          cd src/FlexCms.Framework
          dotnet nuget push ./nupkg/*.nupkg \
              --source "https://nuget.pkg.github.com/${{ github.repository_owner }}/index.json" \
              --api-key ${{ secrets.GITHUB_TOKEN }} \
              --skip-duplicate

      - name: Publish to NuGet.org (optional — uncomment when ready for public release)
        # run: |
        #   cd src/FlexCms.Framework
        #   dotnet nuget push ./nupkg/*.nupkg \
        #       --source https://api.nuget.org/v3/index.json \
        #       --api-key ${{ secrets.NUGET_API_KEY }} \
        #       --skip-duplicate
        run: echo "Skipping NuGet.org publish (uncomment when ready)"
```

#### Set the NuGet API key as a GitHub secret

GitHub repo → Settings → Secrets and variables → Actions → New repository secret:

- Name: `NUGET_API_KEY`
- Value: Your NuGet.org API key

#### Publish a new version

```bash
# Update version in csproj first:
# <Version>1.1.0</Version>

git commit -am "chore(framework): bump to 1.1.0"
git push

# Tag and push
git tag framework-v1.1.0
git push origin framework-v1.1.0

# GitHub Actions auto-publishes
```

---

### 11.7 Adding NuGet Packages to Your Module

Now flipping to the **module developer** side — how do you add third-party NuGet packages to your module?

#### Common packages your module might need

| Package | Why | When |
|---|---|---|
| `Markdig` (BSD) | Markdown rendering | Blog comments, docs module |
| `iTextSharp` (AGPL — careful!) | Advanced PDF | Use `PdfSharp 6.x` (MIT) instead |
| `CsvHelper` (MS-PL/Apache) | CSV import/export | Bulk product/post import |
| `Polly` (BSD) | Resilience policies | API integrations beyond standard handler |
| `Hangfire` (LGPL) | Job scheduler | ❌ Plan rejects — use IHostedService |
| `OpenIddict` (Apache) | OAuth server | If your module IS the OAuth provider |
| `BouncyCastle` (MIT) | Crypto operations | Module needing PGP / advanced crypto |
| `Quartz.NET` (Apache) | Cron-style scheduling | Beyond IHostedService capability |
| `RestSharp` (Apache) | REST client | Alternative to HttpClient |
| `Mapster` (MIT) | Object mapping | DTO ↔ Entity conversion |
| `FluentValidation` (Apache) | Validation rules | Complex form validation |
| `Bogus` (MIT) | Fake data generation | Dev seeders, testing |

#### Step 1: Add the package to your module

```bash
cd modules/FlexCms.Blog

# Public NuGet.org package
dotnet add package Markdig

# Specific version
dotnet add package Markdig --version 0.37.0

# From private feed (e.g., GitHub Packages)
dotnet add package FlexCms.Framework \
    --source "https://nuget.pkg.github.com/rayhanul17/index.json"
```

#### Step 2: Verify the package was added

Check `modules/FlexCms.Blog/FlexCms.Blog.csproj`:

```xml
<ItemGroup>
  <PackageReference Include="Markdig" Version="0.37.0" />
  <ProjectReference Include="..\..\src\FlexCms.Framework\FlexCms.Framework.csproj" />
</ItemGroup>
```

#### Step 3: Use it in your code

```csharp
using Markdig;

namespace FlexCms.Blog.Services;

public class MarkdownService
{
    public string Render(string markdown)
    {
        var pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .Build();
        return Markdown.ToHtml(markdown, pipeline);
    }
}
```

#### Step 4: Build the module — dependencies are bundled

```bash
dotnet publish -c Release -o publish/
ls publish/
# You'll see:
# FlexCms.Blog.dll
# Markdig.dll       ← bundled automatically by dotnet publish
# (other transitive dependencies)
# module.json
```

**Important:** Always use `dotnet publish` (not `dotnet build`) to package modules. `publish` includes ALL dependencies; `build` only puts your code in `bin/`.

#### Step 5: ZIP and ship as before (Section 5)

```bash
cd publish
zip -r ../../../FlexCms.Blog-1.0.0.zip .
```

The ZIP now contains your DLL **plus all NuGet dependencies** that the host process needs to load it.

---

### 11.8 Publishing a Module as NuGet (Alternative to ZIP)

For module marketplace (Phase 17) or distributed teams, you can publish a module **as a NuGet package** instead of a ZIP.

#### Configure the module's csproj

Edit `modules/FlexCms.Blog/FlexCms.Blog.csproj`:

```xml
<PropertyGroup>
  <TargetFramework>net10.0</TargetFramework>

  <!-- NuGet package settings -->
  <GeneratePackageOnBuild>true</GeneratePackageOnBuild>
  <PackageId>FlexCms.Blog</PackageId>
  <Version>1.0.0</Version>
  <Authors>Your Name</Authors>
  <Description>Blog module for FlexCms — posts, categories, comments.</Description>
  <PackageTags>flexcms;module;blog</PackageTags>
  <PackageLicenseExpression>MIT</PackageLicenseExpression>

  <!-- Include Views and wwwroot in the package -->
  <ContentTargetFolders>contentFiles\any\net10.0</ContentTargetFolders>
</PropertyGroup>

<ItemGroup>
  <Content Include="Views\**\*.cshtml" CopyToOutputDirectory="PreserveNewest" />
  <Content Include="wwwroot\**\*" CopyToOutputDirectory="PreserveNewest" />
  <EmbeddedResource Include="module.json" />
</ItemGroup>

<ItemGroup>
  <!-- Reference Framework via NuGet (NOT project ref) for distributable modules -->
  <PackageReference Include="FlexCms.Framework" Version="1.0.0" />
</ItemGroup>
```

#### Publish

```bash
cd modules/FlexCms.Blog
dotnet pack -c Release -o nupkg

# Push to GitHub Packages (or NuGet.org)
dotnet nuget push nupkg/FlexCms.Blog.1.0.0.nupkg \
    --source "github-flexcms" \
    --api-key <YOUR_PAT>
```

#### Admin installs in production

Two ways:

**Option A — Admin UI (when marketplace is built):**
- `https://mysite.com/admin/marketplace` → search "Blog" → Install

**Option B — Manual via SSH:**
```bash
ssh flexcms@vps
cd /opt/flexcms

# Pull module from NuGet to a temp folder
dotnet nuget restore --packages /tmp/flexcms-modules \
    --source "https://nuget.pkg.github.com/rayhanul17/index.json"

# Extract module files into Docker volume
docker exec flexcms_flexcms_1 mkdir -p /app/modules/FlexCms.Blog
docker cp /tmp/flexcms-modules/flexcms.blog/1.0.0/lib/net10.0/. \
    flexcms_flexcms_1:/app/modules/FlexCms.Blog/

# Restart container
docker compose -f docker/docker-compose.prod.yml restart flexcms

# Activate via admin UI
```

---

### 11.9 Versioning Strategy (Semantic Versioning)

Follow [SemVer](https://semver.org/): `MAJOR.MINOR.PATCH`

| Change type | Bump | Example |
|---|---|---|
| Bug fix, no API change | PATCH | 1.0.0 → 1.0.1 |
| New feature, backward compatible | MINOR | 1.0.1 → 1.1.0 |
| Breaking change | MAJOR | 1.1.0 → 2.0.0 |

For pre-release:

```xml
<Version>2.0.0-beta.1</Version>
<Version>2.0.0-rc.1</Version>
```

NuGet treats these as "pre-release" — clients must opt in to install them.

---

### 11.10 Using the Published Framework (External Module Author Workflow)

If someone outside your team wants to build a FlexCms module, here's their flow:

```bash
# 1. Create a brand-new project — no FlexCms source needed
mkdir mycompany-flexcms-newsletter
cd mycompany-flexcms-newsletter

dotnet new classlib -n MyCompany.FlexCms.Newsletter -f net10.0
cd MyCompany.FlexCms.Newsletter

# 2. Add Framework via NuGet
dotnet add package FlexCms.Framework --source https://nuget.pkg.github.com/rayhanul17/index.json
# OR (when published to NuGet.org):
dotnet add package FlexCms.Framework

# 3. Add other deps
dotnet add package Markdig
dotnet add package MailKit

# 4. Code the module — IFcmsModule, services, controllers, etc.
# (See MODULE_DEV.md for structure)

# 5. Build + publish
dotnet publish -c Release -o publish/
cd publish && zip -r ../MyCompany.Newsletter-1.0.0.zip . && cd ..

# 6. Distribute — send the ZIP to admins, OR publish to your own NuGet feed
```

The external developer **never clones your CMS source** — they only need the published `FlexCms.Framework` NuGet.

---

### 11.11 Quick Reference — NuGet Commands

| Task | Command |
|---|---|
| Add package to module | `dotnet add package <Name>` |
| Add specific version | `dotnet add package <Name> --version 1.2.3` |
| Add from private feed | `dotnet add package <Name> --source <URL>` |
| List installed packages | `dotnet list package` |
| Update all packages | `dotnet list package --outdated` then `dotnet add package <Name>` to bump |
| Remove package | `dotnet remove package <Name>` |
| Build NuGet package | `dotnet pack -c Release -o nupkg` |
| Push to feed | `dotnet nuget push <file>.nupkg --source <feed> --api-key <key>` |
| List configured sources | `dotnet nuget list source` |
| Add a new source | `dotnet nuget add source <URL> -n <name>` |
| Remove a source | `dotnet nuget remove source <name>` |

---

## 📚 Further Reading

- **Architecture details:** [`docs/plan.md`](plan.md) — full 14,500-line spec
- **Module dev rules:** [`docs/MODULE_DEV.md`](MODULE_DEV.md)
- **Production deploy details:** [`docs/DEPLOYMENT.md`](DEPLOYMENT.md)
- **Contributing rules:** [`CONTRIBUTING.md`](../CONTRIBUTING.md)
- **NuGet docs:** https://learn.microsoft.com/en-us/nuget/
- **SemVer specification:** https://semver.org/

---

## 📌 Framework Attribute & TagHelper Reference

For module-author-facing details (constructor signatures, examples,
when to reach for which attribute), see the **Attribute Reference**
and **Tag Helper Reference** sections at the bottom of
[MODULE_DEV.md](MODULE_DEV.md). Quick index of what's available:

**Attributes** (all defined in `FlexCms.Framework`):

- `[FcmsAuthorize(permission?)]` — controller/action permission gate
  with `&` / `|` expressions. SuperAdmin bypass.
- `[FcmsLog(action, entityType, entityIdParam?, module?)]` — automatic
  audit-log entry on successful action result.
- `[FcmsLogIgnore]` — strip a property from audit-log JSON snapshots.
  Identity sensitive fields + nav collections are stripped automatically;
  this is for module-defined fields.
- `[FcmsTable(name)]` — override the auto-generated table name on an
  entity class.
- `[FcmsScoped]` / `[FcmsSingleton]` / `[FcmsHostedService]` — mark a
  module service for auto-registration with the right lifetime.
- `[FcmsModuleApi(version)]` — mark an interface as a versioned
  cross-module API surface; consumers resolve via
  `IFcmsModuleApiRegistry.Get<T>(constraint)`.

**Tag Helpers** (auto-loaded via `_ViewImports.cshtml`):

- `<button fcms-authorize="key">` — hide when permission missing
- `<fcms-honeypot />` — bot-spam decoy field pair
- `<fcms-env-banner />` — DEV / STAGING colored banner
- `<fcms-picture src alt widths sizes>` — `<picture>` + WebP +
  responsive `srcset` + lazy `<img>` fallback
- `<fcms-data-table>` / `<fcms-row-actions>` — server-side DataTables
  with permission-filtered action buttons

**JSON serialization safety net** — `FcmsLogJsonResolver` strips:

- Identity fields: `PasswordHash`, `SecurityStamp`, `ConcurrencyStamp`,
  `NormalizedUserName`, `NormalizedEmail`, `EmailConfirmed`,
  `PhoneNumberConfirmed`, `TwoFactorEnabled`, `AccessFailedCount`,
  `LockoutEnabled`, `LockoutEnd`, `AuthenticationToken`
- Embedded Identity collections: `Tokens`, `Claims`, `Logins`
- Anonymous-type detection skips compiler-generated DTOs
- Navigation collections (any `IEnumerable<TClass>`) and nav references
  (class-typed properties except strings) are stripped automatically
- Anything `[FcmsLogIgnore]`-marked

You can pass full entities directly to `IFcmsLogService.LogAsync(...)`
without anonymous-type projection — the resolver handles the rest.

---

## ❓ Quick Reference Card

| Task | Command |
|---|---|
| Pull latest | `git checkout main && git pull` |
| New feature | `git checkout -b feature/X` |
| Commit | `git commit -m "feat(scope): message"` |
| Push | `git push -u origin <branch>` |
| Open PR | `gh pr create --base main` |
| Run dev | `cd src/FlexCms.Host && dotnet watch run` |
| Run tests | `dotnet test` |
| Format code | `dotnet format FlexCms.slnxx` (run before every commit) |
| Start local DB | `docker compose -f docker/docker-compose.dev.yml up -d` |
| Stop local DB | `docker compose -f docker/docker-compose.dev.yml down` |
| Build module ZIP | `cd modules/X && dotnet publish -c Release -o publish/` |
| Deploy to prod | Push to `main` → GitHub Actions auto-deploys |
| Update prod manually | `ssh vps && cd /opt/flexcms && git pull && docker compose pull && docker compose up -d` |
| Check prod health | `curl https://mysite.com/health/ready` |

---

**Questions?** Open an issue at https://github.com/rayhanul17/flex_cms_v1/issues
