# Phase 15 — SEO + Performance + Operations + Compliance: Manual Test Cases

> **Automated coverage**: 63 Phase15 unit tests across SEO rendering,
> output cache, feature flag bucketing, editor presence, SemVer +
> dependency checker, backup contracts. Project total: **392 unit + 247
> EF integration**.
>
> Phase 15 is **partial** — entities/services/middleware are wired and
> tested; admin UI tabs/views for many of these are deferred (the
> backing services are stable and reusable from module-supplied admin
> pages in the meantime).

## 1. SEO Pack (Issue 84)

| # | Action | Expected |
|---|--------|----------|
| 1.1 | Render a published Page → frontend source contains `<link rel="canonical">`, `og:type`, `og:title`, `og:description`, `og:url`, `og:site_name`, `twitter:card="summary_large_image"`, `twitter:title`. | `SeoService.RenderHeadTagsAsync` with no per-entity row falls back to entity title/description. |
| 1.2 | Save an `FcmsSeoMeta` row with `OgTitle="Custom"` for the page → reload → `og:title` shows "Custom"; entity's own `Title` still rendered as the `<title>` tag. | Override semantics. |
| 1.3 | Set `OgImageUrl="https://cdn.example.com/og.png"` → `og:image` + `twitter:image` (when twitter blank) both point there. | Twitter falls back to OG image. |
| 1.4 | Set `NoIndex=true` → response source contains `<meta name="robots" content="noindex,nofollow">`. | Verified by tests. |
| 1.5 | Set `CustomJsonLd` to a valid `FAQPage` payload → page source contains your verbatim JSON inside `<script type="application/ld+json">`; default Article generation skipped. | `RenderJsonLdAsync` honours custom payload. |
| 1.6 | No custom JSON-LD → JSON-LD auto-generated as Article with `headline` / `description` / `url` / `mainEntityOfPage` / `publisher` / `image` / `author` / `datePublished` populated from `SeoRenderContext`. | Auto-default. |
| 1.7 | Set `SchemaType="NewsArticle"` → JSON-LD `@type` reflects it. | Schema type override. |
| 1.8 | XSS-style title `<script>alert(1)</script>` → encoded as `&lt;script&gt;alert(1)&lt;/script&gt;` in head; never executes. | HTML encoding verified by `RenderHeadTags_html_encodes_user_input`. |

## 2. Robots.txt admin (Issue 85)

| # | Action | Expected |
|---|--------|----------|
| 2.1 | `GET /robots.txt` → returns `SiteSettings.RobotsTxtContent`. | `RobotsController`. |
| 2.2 | Body contains `Sitemap: {sitemap_url}` token → request rendered with `Sitemap: https://yoursite/sitemap.xml`. | Token replacement. |
| 2.3 | Set `RobotsBlockAll=true` → response always `User-agent: *\nDisallow: /\n` regardless of body. | Staging guard. |

## 3. Output cache (Issue 86)

| # | Action | Expected |
|---|--------|----------|
| 3.1 | `GetOrSetAsync("post:1", factory, 5min, tags: ["public-page"])` → factory runs once on miss; subsequent reads hit cache without invoking factory. | `Factory_runs_only_on_miss`. |
| 3.2 | Admin saves a post → controller calls `EvictByTagAsync("public-page")` → next anonymous read re-renders. | `EvictByTagAsync_drops_every_entry_carrying_that_tag`. |
| 3.3 | `EvictByTagAsync("does-not-exist")` → no-op, no exception, existing entries untouched. | Robust against unknown tags. |
| 3.4 | Multi-tag entry: `tags: ["public-page", "post:1"]` → evicting either tag drops it; the other tag's set is left consistent. | Verified by multi-entry test. |

## 4. Slow-query interceptor (Issue 87)

| # | Action | Expected |
|---|--------|----------|
| 4.1 | Trigger a 1.2-second EF query (e.g. cross-join in dev) → log appears at Warning level: `Slow query (1200 ms): SELECT ...`. | Default 1s threshold. |
| 4.2 | Query stays in the in-memory ring buffer; admin "System → Slow Queries" panel reads `interceptor.GetRecent()`. | Ring capped at 50. |
| 4.3 | Sub-threshold queries (< 1s) → never logged or buffered. | `Track` early-returns. |
| 4.4 | Long SQL (>2000 chars) → truncated to 2000 + `…` in the buffer. | Buffer-bloat guard. |

## 5. Centralized logging sinks (Issue 88)

| # | Action | Expected |
|---|--------|----------|
| 5.1 | `appsettings.json` adds `"Serilog": { "WriteTo": [{"Name":"Seq","Args":{"serverUrl":"http://localhost:5341"}}] }` + install `Serilog.Sinks.Seq` → restart → log entries appear in Seq UI. | `ReadFrom.Configuration` wired. |
| 5.2 | Same for Elasticsearch / Application Insights / Datadog with their respective sink packages. | All sinks layer ON TOP of the file sink (don't replace). |
| 5.3 | `appsettings` references a missing sink package → startup logs warning + falls back to file sink only; app boots. | Try/catch around `ReadFrom.Configuration`. |
| 5.4 | No `Serilog` section at all → only the default file sink active. | Section-existence check. |

## 6. Backup + restore (Issue 89)

| # | Action | Expected |
|---|--------|----------|
| 6.1 | Call `IFcmsBackupService.CreateBackupAsync()` → ZIP at `App_Data/backups/backup_yyyy-MM-dd_HHmmss.zip` with `_metadata.json` + `entities/{Name}.json` per DbSet + `media/...` + `config/setup.json`. | `BackupResult` has `FileName`, `EntityCount`, `SizeBytes`. |
| 6.2 | Audit-log entities (`Logs`, `LogArchives`) excluded from the dump. | Hardcoded skip — they're huge + append-only. |
| 6.3 | `ListBackupsAsync()` → newest first. | `OrderByDescending(CreationTimeUtc)`. |
| 6.4 | `DeleteBackupAsync("../../etc/passwd")` → silently rejected (path traversal). | Filename validation. |
| 6.5 | `RestoreAsync(file, RestoreOptions(restoreMedia: true, restoreConfig: false))` → DbSet rows replaced + media files extracted + `setup.json` left untouched. | Conservative defaults. |
| 6.6 | `ApplyRetentionAsync(7)` → backups older than 7 days deleted. | Returns count. |

## 7. Maintenance mode (Issue 90)

| # | Action | Expected |
|---|--------|----------|
| 7.1 | Toggle `SiteSettings.MaintenanceModeEnabled=true` → anonymous request to `/` → 503 + Maintenance.cshtml rendered with `MaintenanceMessage`. | `Retry-After: 3600` header set. |
| 7.2 | Same toggle → `/admin/...`, `/auth/...`, `/health` all pass through to normal handlers. | Hard-coded bypass paths. |
| 7.3 | Static assets (`/lib/`, `/css/`, `/js/`, `/img/`, `/favicon.ico`) all pass through so the maintenance page itself can load CSS. | Bypass list. |
| 7.4 | User in `MaintenanceAllowedRoles` ("SuperAdmin,Admin" by default) browses public pages → passes. | Role bypass. |
| 7.5 | Visit `/?bypass={token}` matching `MaintenanceBypassToken` → sets `fcms-maintenance-bypass` cookie + serves the requested page. Subsequent navigation works without re-supplying the token. | Cookie-backed bypass. |
| 7.6 | View rendering throws → falls back to inline-HTML maintenance page. | `TryRenderViewAsync` returns null on failure. |

## 8. Module SemVer + dependency check (Issues 93-94)

| # | Action | Expected |
|---|--------|----------|
| 8.1 | Module manifest `DependsOn: ["BlogModule>=1.2.0"]` → installed BlogModule 1.5.0 → `ModuleDependencyChecker.Check` returns empty list. | Constraint satisfied. |
| 8.2 | Same constraint, installed BlogModule 1.0.0 → failure: `"Module 'BlogModule' version '1.0.0' does not satisfy '>=1.2.0'."`. | Human-readable error. |
| 8.3 | `DependsOn: ["MissingMod"]` → failure: `"Required module 'MissingMod' is not installed."`. | Presence check. |
| 8.4 | `^1.2.0` → 1.5.0 ✓, 2.0.0 ✗ (major bump). `~1.2.0` → 1.2.5 ✓, 1.3.0 ✗ (minor bump). | Caret + tilde semantics. |
| 8.5 | `IModuleUpdateService.UpdateAsync(moduleId, packagePath)` → existing module folder renamed to `.{moduleId}.backup_{timestamp}/`, new payload extracted, FcmsModuleRecord version bumped. | Atomic via Directory.Move. |
| 8.6 | Update fails mid-extract → backup folder restored, original module still active, result has `RolledBack=true`. | Auto-rollback. |
| 8.7 | Both update + rollback fail → result has `RolledBack=false` + combined error message; logger.Error with backup path so operator can manually recover. | Fallback diagnostic. |

## 9. Module sandbox (Issue 95)

| # | Action | Expected |
|---|--------|----------|
| 9.1 | Module `module.json` with `"RequestedCapabilities": ["filesystem.read", "outbound.http"]` → admin install prompt shows the list. | UI reads `manifest.RequestedCapabilities`. |
| 9.2 | Admin approves → `FcmsModuleRecord.ApprovedCapabilities="filesystem.read,outbound.http"` + `ApprovedByUserId` + `ApprovedAt` set; module activates. | Audit trail. |
| 9.3 | Module declares a capability the admin hasn't approved → activation prompt blocks until approved. | (Deferred — entity + record fields are in place; UI to land in admin pass.) |

## 10. Editor conflict (Issue 96)

| # | Action | Expected |
|---|--------|----------|
| 10.1 | User A opens `/admin/pages/{id}/edit` → controller calls `EditorPresenceService.Heartbeat`. User B opens same page → controller renders banner "User A is editing (last seen 5s ago)". | `GetActive` returns A's presence. |
| 10.2 | User A doesn't beat for 45s (tab closed) → presence drops; User B's next render no longer shows the banner. | `StaleWindow` cleanup. |
| 10.3 | User A saves → EF SaveChanges throws `DbUpdateConcurrencyException` because B saved 30s earlier with the same RowVersion → controller surfaces "Another editor saved first; refresh to merge". | RowVersion concurrency token (EF translates per provider — ROWVERSION on SqlServer, TIMESTAMP on MySQL, xmin on Postgres). |
| 10.4 | `Release(entity, user)` → user removed immediately. | Tab-close hook. |

## 11. UnpublishDate (Issue 97)

| # | Action | Expected |
|---|--------|----------|
| 11.1 | Edit a published Page → set `UnpublishAt` to 2 minutes from now → save → wait → next `ScheduledPublishService` tick (every minute) flips `IsPublished=false`. | Auto-unpublish branch added. |
| 11.2 | Unpublished page no longer reachable on the public route. | Existing publish-gate logic. |
| 11.3 | Set `PublishedAt` AND `UnpublishAt` both in the past on a draft → service publishes, then unpublishes within the same tick → end state matches operator intent (unpublished). | Both queries run; unpublish wins. |

## 12. Multi-language (Issue 98)

| # | Action | Expected |
|---|--------|----------|
| 12.1 | Insert `FcmsLanguage(Code="ar", DisplayName="العربية", IsRtl=true)` via `LanguageService.UpsertAsync` → `ListActiveAsync` includes it. | Active filter respected. |
| 12.2 | Set `IsActive=false` → vanishes from `ListActiveAsync` but still in `ListAllAsync` (history kept). | Soft-disable. |
| 12.3 | Per-language `IsRtl=true` → theme can render `<html dir="rtl" lang="ar">`. | Flag exposed via `GetByCodeAsync`. |
| 12.4 | Duplicate `Code` → `UpsertAsync` updates the existing row instead of creating a duplicate. | Upsert by Code. |
| 12.5 | Unique index on Code → DB-level dedup. | Verified at schema level. |

## 13. Admin widgets (Issue 99)

| # | Action | Expected |
|---|--------|----------|
| 13.1 | E-commerce module ships `services.AddScoped<IFcmsAdminWidget, TodayOrdersWidget>()` → admin dashboard view-component scans + renders in `SortOrder`. | Open registration. |
| 13.2 | Widget with `RequiredPermission="orders.view"` → user without that permission → widget not rendered. | Permission filter. |
| 13.3 | `RequiredPermission` null → visible to any admin who can see the dashboard. | Default-allow. |

## 14. GDPR (Issue 100)

| # | Action | Expected |
|---|--------|----------|
| 14.1 | User → Profile → "Download My Data" → `IFcmsGdprService.ExportUserDataAsync` returns JSON containing profile + pages + posts + comments + sessions + login history; controller wraps in `File()`. | Single dump. |
| 14.2 | "Delete My Account" → `DeleteAccountAsync(userId, deleteOwnedContent: false)` → user soft-deleted, `Email`/`UserName` anonymized to `deleted-{guid}@example.invalid` (RFC-2606 reserved TLD), all sessions revoked, `PasswordHash` cleared, `LockoutEnd=MaxValue`. Authored content stays. | FK integrity. |
| 14.3 | Same with `deleteOwnedContent: true` → pages / posts / comments authored by that user soft-deleted too. | Cascade option. |
| 14.4 | Cookie consent banner first-visit → click Accept → cookie stored, banner gone. | (Deferred — needs banner partial.) |
| 14.5 | `SiteSettings.CurrentTermsVersion` bumped → user with older accepted version → forced re-acceptance form on next login. | (Deferred — needs middleware + form.) |

## 15. Feature flags (Issue 101)

| # | Action | Expected |
|---|--------|----------|
| 15.1 | Insert flag `Key="ai-suggestions"`, `IsEnabled=true`, `RolloutPercent=100` → `IsEnabledAsync("ai-suggestions", userId)` → true for any user. | Master switch. |
| 15.2 | `IsEnabled=false` → always false regardless of percent / target roles. | Master switch off. |
| 15.3 | `RolloutPercent=50` → user X's `StableBucket(X, "ai-suggestions") < 50` → on; ≥ 50 → off. Same user always lands in the same bucket (stable across requests). | SHA-256 of `{userId:N}:{key}`. |
| 15.4 | Same user, different feature → different bucket (decorrelated by key). | Salt prevents per-user correlation across all flags. |
| 15.5 | `TargetRolesCsv="BetaTesters"` → user in BetaTesters → on regardless of percent. | Cohort bypass. |
| 15.6 | Anonymous user (`userId=null`) + `RolloutPercent < 100` → off. (Anonymous A/B needs a different signal.) | Documented limitation. |
| 15.7 | Admin updates flag → 30s cache means next read within 30s still sees old value; after 30s the cache reloads. | `CacheTtl` documented. |
| 15.8 | Bucket distribution across 1000 users roughly uniform (each decile gets 25–400 hits). | `Distribution_across_users_is_roughly_uniform`. |

## 16. Database storage cross-check

- **EF**: `SELECT entity_type, entity_id FROM fcms_seo_meta;` (one row per entity max — unique index on the pair).
- **EF**: `SELECT key, is_enabled, rollout_percent FROM fcms_feature_flags;` (unique index on Key).
- **EF**: `SELECT code, display_name, is_rtl FROM fcms_languages;` (unique index on Code).
- **Backups**: `ls App_Data/backups/` — files match `backup_yyyy-MM-dd_HHmmss.zip` pattern.

## 17. Out of scope (future work / deferred to next admin pass)

- Admin UI tabs/views for SEO meta, robots.txt editor, maintenance settings, backup wizard, feature flags CRUD, languages CRUD, GDPR profile actions, slow-queries dashboard, module sandbox approval modal, editor conflict banner, module update upload form.
- Cookie consent banner partial + middleware comparing accepted vs current terms version.
- Multi-node output cache (Redis-backed `IFcmsOutputCache` impl).
- mysqldump / pg_dump-driven physical backups (optional alternative to the JSON dump).
- N+1 detection (Issue 87 secondary requirement) — separate from slow-query interceptor; needs query-tree analysis.
