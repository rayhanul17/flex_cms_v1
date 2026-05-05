# FlexCMS — Phases 1–6 Test Cases

> Comprehensive QA checklist for verifying Phases 1–6 before merging
> `phase-6-veryfy` branch and starting Phase 7.
>
> **Legend:**
> - ✅ — already covered by automated test (test class noted)
> - 🧪 — needs manual UI / browser verification
> - 🐳 — needs Docker (Testcontainers MySQL / MongoDB)
>
> **Tracking:**
> - Automated runs: `dotnet test tests/FlexCms.Tests.Unit` + `dotnet test tests/FlexCms.Tests.Integration --filter "FullyQualifiedName!~EfPhase1Tests&FullyQualifiedName!~MongoPhase1Tests"`
> - Docker runs: `dotnet test` (full suite)
> - Current count: **180 unit + 157 integration (in-memory) + 4 Mongo + N MySQL**

---

## Phase 1 — Project Scaffold + DB Layer

### Repository / Entity persistence
- [ ] ✅ EF: `EfRepository.AddAsync` → row persisted in DB (`EfPhase1Tests.EfRepository_Insert_RowExistsInDb`) 🐳
- [ ] ✅ EF: UnitOfWork rollback on exception → no rows inserted (`EfPhase1Tests.EfUnitOfWork_RollbackOnException_BothEntitiesAbsent`) 🐳
- [ ] ✅ Mongo: `MongoRepository.AddAsync` → document persisted with GUID subtype 4 (`MongoPhase1Tests.MongoRepository_Insert_DocumentExistsWithGuidSubtype4`) 🐳
- [ ] ✅ Mongo: DateTime stored as Unix milliseconds Int64 (NOT BSON Date) (`MongoRepository_DateTime_StoredAsUnixMilliseconds`) 🐳
- [ ] ✅ Mongo: DateTime UTC roundtrip preserves Kind + ticks (`MongoRepository_DateTime_StoredAsUtcReadBackAsUtc`) 🐳
- [ ] ✅ Mongo: Soft-delete excludes from `GetAllAsync`, raw doc has `status: 404` (`MongoRepository_SoftDelete_NotReturnedByGetAll`) 🐳

### EntityStatus / soft-delete
- [ ] ✅ Default value of new entity is `EntityStatus.Active` (BaseEfEntity / BaseMongoEntity)
- [ ] ✅ EF query filter `Status != Deleted` auto-applied on all DbSet queries (`AuditTrailTests`)
- [ ] ✅ `IgnoreQueryFilters()` reveals soft-deleted rows (used in TrashController, tests)
- [ ] ✅ `SoftDeleteRangeAsync` sets Status=Deleted on all (`AuditTrailTests`)
- [ ] ✅ `DeleteRangeAsync` hard-deletes (used by FcmsLogService archive)

### Audit fields auto-injection
- [ ] ✅ `CreatedAt` / `UpdatedAt` set automatically on insert/update (`AuditTrailTests`)
- [ ] ✅ `CreatedBy` set from `IHttpContextAccessor.User` (NameIdentifier claim)
- [ ] ✅ `UpdatedBy` overwritten on every update (`AuditTrailTests`)
- [ ] ✅ Background services (no HttpContext) → `CreatedBy = null` (system operation)

### Setup wizard helper
- [ ] ✅ `SetupHelper.Write` then `Read` roundtrip preserves config (`SetupHelperTests.SetupHelper_WriteRead_RoundtripWithEncryptedPassword`)
- [ ] ✅ DB password is encrypted in setup.json (starts with "CfDJ8") (`SetupHelperTests`)
- [ ] ✅ `DecryptPassword` returns original plaintext (`SetupHelperTests`)
- [ ] ✅ `IsSetupComplete` static returns true after write with flag set (`IsSetupComplete_static_returns_true_after_Write_with_complete_flag`)
- [ ] ✅ Returns false when file missing or flag false

### Manual setup-wizard UI
- [ ] 🧪 Fresh app, no `setup.json` → visit any URL → redirect to `/setup`
- [ ] 🧪 Step 1 (DB): [Test Connection] success → Next enabled; wrong creds → error message
- [ ] 🧪 Setup complete → `App_Data/setup.json` written → `/admin` accessible after restart

---

## Phase 2 — Auth + Security Core

### Login + session
- [ ] ✅ Register user via `UserManager.CreateAsync` works (`Phase2 integration tests`)
- [ ] ✅ Duplicate email → Identity error returned
- [ ] ✅ Weak password → password-validator error
- [ ] ✅ `CheckPasswordAsync` returns true for correct password
- [ ] ✅ `ForcePasswordChange` flag persists on user
- [ ] ✅ Lockout enforced after threshold attempts
- [ ] ✅ Password reset token generated + valid

### Manual auth UI
- [ ] 🧪 Login with correct creds → cookie issued → `/admin` accessible
- [ ] 🧪 Login with wrong password 5× → `AccountLocked` error → 15min lockout
- [ ] 🧪 Login from locked account → lockout message with countdown
- [ ] 🧪 Rate limiter: 11th login attempt in 1min from same IP → 429 response
- [ ] 🧪 Forgot password → email link → reset → new password → login works (requires SMTP config)
- [ ] 🧪 ForcePasswordChange: user with flag → login → redirect to /auth/change-password; direct URL to /admin blocked by middleware

### Security headers + middleware
- [ ] 🧪 Response headers include `X-Frame-Options: SAMEORIGIN`, `X-Content-Type-Options: nosniff`
- [ ] 🧪 Admin IP whitelist: set "192.168.*.*" → access from 203.x.x.x → 403
- [ ] 🧪 Global IP blacklist: set "1.2.3.*" → access from 1.2.3.99 → 403 everywhere

### Validators
- [ ] 🧪 BD mobile validation: "01912345678" → accepted; "07911123456" → rejected; "+8801712345678" → normalized

---

## Phase 3 — User / Role / Permission

### Permission expression parser
- [ ] ✅ Single permission key match (`PermissionExpressionTests`)
- [ ] ✅ AND `&` requires both perms
- [ ] ✅ OR `|` requires either perm
- [ ] ✅ Edge case: empty / whitespace expression handled
- [ ] ✅ SuperAdmin uppercase role claim also passes (Mongo normalized name regression)

### `[FcmsAuthorize]` filter
- [ ] ✅ Unauthenticated user → 302 redirect to login (`FcmsAuthorizeFilterTests`)
- [ ] ✅ Authenticated SuperAdmin → bypasses all permission checks
- [ ] ✅ User with required permission → 200
- [ ] ✅ User missing permission → 403 (HTML or JSON depending on AJAX)
- [ ] ✅ AJAX request to forbidden endpoint → JSON `{IsSuccess: false}`, not HTML redirect

### Permission service
- [ ] ✅ Assign permission to role → `HasPermissionAsync` returns true (`PermissionServiceTests`)
- [ ] ✅ Revoke → returns false
- [ ] ✅ Cache: assign → immediate effect (cache cleared on assign) (`PermissionServicePhase6Tests`)
- [ ] ✅ Soft-delete `FcmsRolePermission` removes effect
- [ ] ✅ `SeedPermissionsAsync` is idempotent — second call doesn't duplicate

### `<elem fcms-authorize>` TagHelper
- [ ] ✅ Hidden if no permission, visible if has permission (rendered tests)

### Manual user/role UI
- [ ] 🧪 Create user (email) → assign Editor role → login → Editor panel visible
- [ ] 🧪 Active toggle on user list → AJAX call → status badge updates → toast appears (no page reload)
- [ ] 🧪 Delete user → confirm modal → delete → row removed → toast
- [ ] 🧪 Role detail: Permissions accordion → search "delete" → only delete perms show
- [ ] 🧪 Group "Select All" → all in group checked → save → permissions assigned
- [ ] 🧪 SuperAdmin role: all permissions bypass automatic
- [ ] 🧪 `/admin/permissions` lists all permission keys grouped by Group

---

## Phase 4 — Module System

### Module loader
- [ ] ✅ Empty `modules/` folder → app starts normally (`ModuleLoaderTests`)
- [ ] ✅ Loader finds module DLLs in subfolders (`ModuleLoaderTests`)
- [ ] ✅ `module.json` parsed correctly (manifest)

### Module manager
- [ ] ✅ Topological sort by `DependsOn` (`ModuleManagerTests`)
- [ ] ✅ Circular dependency → throws (`ModuleManagerTests`)
- [ ] ✅ Empty manifest → handles gracefully

### Module registry
- [ ] ✅ Find module by ID (`ModuleRegistryTests`)
- [ ] ✅ Active vs deactivated status

### Module state
- [ ] ✅ Activate marks `module.json.deactivated` = false (`ModuleStateServiceTests`)
- [ ] ✅ Deactivate marks deactivated = true
- [ ] ✅ Uninstall removes folder (next startup)

### Attribute scanner
- [ ] ✅ `[FcmsScoped]` types auto-registered (`AttributeScannerTests`)
- [ ] ✅ `[FcmsSingleton]`, `[FcmsHostedService]` likewise

### Module lifecycle (full integration)
- [ ] ✅ Real sample module DLL: discovered, manifest parsed, deactivation works (`ModuleLifecycleTests`) 🐳

### Manual module admin UI
- [ ] 🧪 Drop test module DLL → restart → Admin Modules list shows it
- [ ] 🧪 Activate module → tables created in DB (`FcmsModuleRecord.SeedCompleted=true`)
- [ ] 🧪 Re-activate same module → `MigrateAsync()` only, `SeedDataAsync()` skipped
- [ ] 🧪 Version change → `OnUpgradeAsync(fromVersion)` called
- [ ] 🧪 Deactivate → restart → module routes 404, menu items hidden
- [ ] 🧪 Re-activate → menu items restored (Status: Deleted → Active)
- [ ] 🧪 Uninstall "Keep Tables" → DLL removed, DB data intact
- [ ] 🧪 Uninstall "Drop Tables" → type module name to confirm → tables dropped
- [ ] 🧪 `dotnet new flexcms-module -n FlexCms.Blog` → correct folder structure
- [ ] 🧪 Dev-mode Admin UI scaffold → [+ Create New Module] visible only in Development env

---

## Phase 5 — CMS: Pages + Posts + Frontend

### Page service
- [ ] ✅ Create + GetById (`PageServiceTests`)
- [ ] ✅ Slug uniqueness enforced
- [ ] ✅ HTML sanitize on Content (script tags stripped) (`HtmlSanitizerTests`)
- [ ] ✅ Soft delete + GetDeleted + Restore (`PageServiceTrashTests`)
- [ ] ✅ Hard delete physical removal

### Post service
- [ ] ✅ Create + tags (auto-create new tags) (`PostServiceTests`)
- [ ] ✅ UpdateAsync replaces tags
- [ ] ✅ GetPublishedAsync excludes drafts
- [ ] ✅ IncrementViewCount works
- [ ] ✅ GetTagSlugsAsync returns post tags
- [ ] ✅ GetByCategoryAsync filters published only
- [ ] ✅ GetBySlug includes Category + Tags nav

### Category service
- [ ] ✅ CRUD operations (`CategoryServiceTests`)
- [ ] ✅ GetPostCountAsync excludes deleted posts (regression)

### Scheduled publish
- [ ] ✅ Drafts with `PublishedAt <= now` get published by background service (`ScheduledPublishAndTrashTests`)

### Trash cleanup
- [ ] ✅ Logs older than retention → hard deleted (`ScheduledPublishAndTrashTests`)
- [ ] ✅ Posts with tags → tags also deleted in cleanup

### Settings + Redirect
- [ ] ✅ SettingsService get/set roundtrip
- [ ] ✅ Redirect query filter excludes deleted (`RedirectTests`)

### Search
- [ ] ✅ Title + content LIKE search returns matching pages + posts

### HtmlSanitizer (security)
- [ ] ✅ `<script>` tags stripped (`HtmlSanitizerTests`)
- [ ] ✅ `onclick="..."` event attrs stripped
- [ ] ✅ `javascript:` URL protocol stripped
- [ ] ✅ Edge cases (nested, malformed) handled

### Manual frontend UI
- [ ] 🧪 Create page → Publish → visit `/slug` → page renders
- [ ] 🧪 Draft page: visit `/slug` → 404 (not leaked)
- [ ] 🧪 Scheduled publish: PublishDate=now+2min, Draft → wait → auto-published (within 1min poll)
- [ ] 🧪 Page ParentId: `/about/team` → parent "about" → child "team" resolved
- [ ] 🧪 AuthenticatedOnly page: logout → visit → redirect to login
- [ ] 🧪 PasswordProtected page: visit → password form → wrong → error → correct → page renders
- [ ] 🧪 Soft delete page → gone from frontend; Admin Trash → page listed; Restore → back
- [ ] 🧪 Trash auto-cleanup: set `TrashRetentionDays=0` → cleanup runs → hard deleted
- [ ] 🧪 Redirect: create /old → /new (301) → visit /old → 301 to /new; HitCount incremented
- [ ] 🧪 Sitemap: publish page → `/sitemap.xml` → URL appears; unpublish → URL gone
- [ ] 🧪 RSS: `/rss` → valid RSS 2.0 XML → latest 20 published posts
- [ ] 🧪 Search: page with "Hello World" title → `/search?q=hello` → result appears
- [ ] 🧪 Slug uniqueness: create 2 pages with same slug → DB constraint error + user-friendly message

---

## Phase 6 — Media + File Storage

### LocalFileStorage
- [ ] ✅ SaveAsync writes file to wwwroot/uploads/ (`LocalFileStorageTests`)
- [ ] ✅ Path traversal protection — `../../etc/passwd` rejected (`LocalFileStorageTests`)
- [ ] ✅ DeleteAsync removes file
- [ ] ✅ GetPublicUrl returns correct URL

### Media service
- [ ] ✅ UploadAsync persists FcmsMedia row (`MediaServiceTests`)
- [ ] ✅ Magic bytes validation: jpg/png/gif/webp/pdf/mp4/mp3/zip allowed
- [ ] ✅ Filename sanitization removes path traversal chars
- [ ] ✅ Thumbnail generated for images (300px max, JPEG 85%)
- [ ] ✅ SoftDeleteAsync → Status=Deleted, file deleted from disk
- [ ] ✅ MoveToFolderAsync changes FolderId

### Media folder service
- [ ] ✅ Create + Rename + Delete (`MediaFolderServiceTests`)
- [ ] ✅ Delete with media inside → reparents media to grandparent folder
- [ ] ✅ Breadcrumb traversal works
- [ ] ✅ Audit logged for create/rename/delete

### Audit logging integration
- [ ] ✅ `MediaUploaded` action logged with full media entity in Value (no nav props)
- [ ] ✅ `FolderRenamed` keeps OldName/NewName projection (delta)
- [ ] ✅ `[FcmsLogIgnore]` properties stripped from JSON
- [ ] ✅ Identity sensitive fields auto-stripped

### Manual media UI
- [ ] 🧪 Upload jpg → succeeds → grid shows thumbnail
- [ ] 🧪 Upload `evil.exe` renamed to `evil.jpg` → magic-bytes rejects ("Invalid file format")
- [ ] 🧪 Upload `../../etc/passwd.jpg` → filename sanitized to safe form, file saved
- [ ] 🧪 Image upload → check `wwwroot/uploads/thumbs/` for generated thumbnail
- [ ] 🧪 Create folder → appears in sidebar tree → click → media filtered to folder
- [ ] 🧪 Rename folder via inline edit → name updates
- [ ] 🧪 Delete folder containing 3 media items → media reparented to parent folder
- [ ] 🧪 Move media via drag/drop or move action → FolderId changes
- [ ] 🧪 Soft-delete media → disappears from library → appears in `/admin/trash`
- [ ] 🧪 Restore media from trash → reappears in library
- [ ] 🧪 User without `media.upload` perm → Upload button hidden, direct API POST → 403
- [ ] 🧪 User without `media.delete` → Delete buttons hidden, direct API → 403
- [ ] 🧪 User without `media.folders` → Create Folder button hidden

---

## Cross-cutting (verify on every phase)

### Build & test health
- [ ] ✅ `dotnet build` → 0 errors, 0 warnings
- [ ] ✅ `dotnet test tests/FlexCms.Tests.Unit` → 180 passing
- [ ] ✅ `dotnet test tests/FlexCms.Tests.Integration` (in-memory) → 157 passing
- [ ] 🐳 Full suite with Docker → all passing

### FcmsTime usage
- [ ] ✅ No `DateTime.UtcNow` in `src/` (only `tests/` allowed)
  - Run: `grep -rn "DateTime.UtcNow" src/ --include="*.cs" | grep -v "src/FlexCms.Framework/Clock"`

### FcmsPermissions constants
- [ ] ✅ No magic-string `[FcmsAuthorize("...")]` in `src/`
  - Run: `grep -rn 'FcmsAuthorize("[a-z]' src/ --include="*.cs"`

### IsDeleted removal verification
- [ ] ✅ No `IsDeleted` references in `src/` (only docstring in EntityStatus.cs)
  - Run: `grep -rn "IsDeleted" src/ --include="*.cs" | grep -v "EntityStatus.cs"`

### FcmsLog Value column
- [ ] ✅ Edit a User → check fcms_logs table → Value column has JSON snapshot of user (no PasswordHash)
- [ ] ✅ Create a Role → check fcms_logs → Value contains role name + priority
- [ ] ✅ Delete a Page → check fcms_logs → Value contains page snapshot (no Children nav property)

### Admin sidebar (post-Phase-5 menu system)
- [ ] 🧪 Sidebar shows: Dashboard / Blog (Posts, Categories) / Pages / Media / Trash / People (Users, Roles, Permissions) / System (Modules, Menu, Redirects, Audit Log, Settings)
- [ ] 🧪 Click Blog parent → expands → click Posts → highlights, navigates
- [ ] 🧪 User without `users.manage` → Users menu item hidden
- [ ] 🧪 SuperAdmin sees all menu items
- [ ] 🧪 Admin renames "Posts" → "Articles" via /admin/menu → sidebar updates after refresh
- [ ] 🧪 Drag-drop reorder in /admin/menu → persists after refresh

### Reusable UI components
- [ ] 🧪 Delete button on User Index → confirm modal pops with correct message
- [ ] 🧪 Activate/Deactivate button → no confirm, direct AJAX, status badge updates
- [ ] 🧪 Custom action button (when added) → respects permission, shows custom confirm
- [ ] 🧪 Toast notifications appear bottom-right, auto-dismiss after 4s
- [ ] 🧪 fcms.confirm / fcms.alert / fcms.dialog JS APIs work in browser console

### DRY DataTable (Page Index proof-of-concept)
- [ ] 🧪 `/admin/pages` loads with server-side DataTables (jQuery DataTables UI)
- [ ] 🧪 Pagination works (next/prev, jump to page)
- [ ] 🧪 Search box filters server-side (AJAX request)
- [ ] 🧪 Column sort (click header) sends sort param to server
- [ ] 🧪 Action column shows Edit + Delete based on user permissions
- [ ] 🧪 Status column shows colored badge (Active=green, InActive=gray, Deleted=red)
- [ ] 🧪 Date column formatted using browser locale

### Error / fallback pages
- [ ] 🧪 Visit nonexistent URL → polished 404 page (Lost in Space, glass card)
- [ ] 🧪 Login as user without admin permission → visit `/admin` → polished 403 (AccessDenied)
- [ ] 🧪 Trigger 500 (e.g. set `app.Run` exception) → polished Error.cshtml with copy error ID
- [ ] 🧪 `/Home/Maintenance` → polished maintenance page with animated gear

---

## Suggested test order

1. **Automated smoke** (5 min): `dotnet build && dotnet test tests/FlexCms.Tests.Unit && dotnet test tests/FlexCms.Tests.Integration --filter "FullyQualifiedName!~Phase1Tests"`
2. **Phase 1 sanity** (2 min): setup wizard fresh-install flow once
3. **Phase 2 auth** (10 min): login / lockout / forgot password / IP filter
4. **Phase 3 permissions** (10 min): create users with different roles, verify access
5. **Phase 4 modules** (15 min): activate / deactivate / uninstall a sample module
6. **Phase 5 CMS** (20 min): page + post CRUD + scheduled publish + trash + frontend rendering
7. **Phase 6 media** (15 min): upload + folders + magic-bytes + permissions
8. **Cross-cutting UI** (10 min): menu + DataTables + modal + toast in browser

**Total ~90 min for full Phase 1–6 manual QA pass.**
