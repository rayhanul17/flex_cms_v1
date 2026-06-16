# FlexCMS — Manual Pre-Ship Test Guide

> **Purpose:** before shipping FlexCMS to a real customer, walk this list
> top-to-bottom on a fresh deployment. Expected pass = green box ticked
> off. Anything that doesn't match the "expected" column is a bug to file
> before the framework is ready for the e-commerce module.
>
> **Time budget:** ~3 hours for the full sweep on one provider; ~5 hours
> if you do both EF (MySQL) and Mongo. Do both — silent provider drift
> is the most common production surprise.
>
> **Granular per-phase manual checks** live in `phase-*-test-cases.md`.
> This doc is the **pre-ship sanity sweep** — broader, less detailed,
> hits every user-visible surface once.

---

## How to use this doc

1. Pick a provider (MySQL or MongoDB). Do the full sweep on one, then
   redo on the other.
2. Start from a clean state — drop the DB, delete `App_Data/setup.json`,
   remove `App_Data/uploads/` and `App_Data/keys/`. The setup wizard
   should appear on first request.
3. Each section starts with the URL or action, has a checklist, and ends
   with "common bugs to look for" so you know what counts as a real
   failure vs. a cosmetic glitch.
4. After each section, copy the result line into a tracking sheet:
   `[YYYY-MM-DD] [Provider] [Section] [PASS / FAIL: <one-line note>]`.
5. When all sections pass on both providers and both `dotnet test`
   suites are green → ship.

---

## Section 0 — Pre-test prep

Before running through the journey:

- [ ] `dotnet test tests/FlexCms.Tests.Unit/FlexCms.Tests.Unit.csproj`
      passes (currently 521 tests).
- [ ] `dotnet test tests/FlexCms.Tests.Integration/FlexCms.Tests.Integration.csproj`
      passes (currently 306 tests, requires Docker for some).
- [ ] `docker compose up -d` — MySQL, Postgres, MongoDB, Mailhog all
      `Up` in `docker ps`.
- [ ] `tail -f src/FlexCms.Host/App_Data/logs/flexcms-<today>.log` in a
      side terminal — you'll watch it for `[BOOT]` and `[SEED]` markers
      throughout.

---

## Section 1 — Setup Wizard (fresh install)

**URL:** `http://localhost:5000` → auto-redirects to `/Setup`

**Steps + expected result:**

- [ ] Visit any URL with no `setup.json` → redirected to `/Setup`
- [ ] **Step 1 — Database**
  - Pick MySQL (or Mongo for the second pass)
  - Click "Test Connection" with wrong password → red error message,
    Next button stays disabled
  - Fix password, Test Connection → green OK
  - Click Next
- [ ] **Step 2 — Site Info**
  - Enter Site Name, Tagline, Base URL
  - Pick a Time Zone (default Bangladesh Standard Time is fine)
  - Pick Default Language (English or Bengali)
  - Click Next
- [ ] **Step 3 — Admin Account**
  - Enter Full Name, Email, Password (must satisfy: 8+ chars, upper,
    lower, digit, special)
  - Submit weak password → field-level error appears
  - Submit valid password
- [ ] **Step 4 — Done**
  - In Development mode you'll see manual-restart instructions (Stop +
    Run again). In Production a "Restart & go to Admin" button appears
    that auto-restarts.
  - After restart, app starts in production mode; `setup.json` exists
    in `App_Data/`.

**Common bugs to look for:**
- "Test Connection" hangs forever → Mongo conn string missing
  `directConnection=true` from PC (see DEVELOPER_GUIDE §13).
- Setup wizard loops back to Step 1 after restart → `setup.json` wasn't
  written (check `App_Data/` is writable by the dotnet process).
- Production mode never starts after wizard → tail the log; if you see
  `[BOOT] app.Run()` but no port binds, an `IHostedService` is hanging
  (see DEVELOPER_GUIDE §13 "App starts but never responds" table).

---

## Section 2 — Login & Auth

**URL:** `/auth/login`

- [ ] Visit `/admin` while logged out → redirected to `/auth/login`
- [ ] Login with wrong password → "Invalid credentials" error
- [ ] Login with correct admin credentials → land on `/admin` Dashboard
- [ ] Page title shows `Dashboard — <YourSiteName> Admin` (not
      `FlexCMS Admin` — confirms admin sidebar pulls SiteName from
      settings)
- [ ] Sidebar brand at top-left shows your configured site name (and
      logo if you've set one in settings)
- [ ] User widget at bottom-left shows your email + initials
- [ ] Click logout → form posts → land at `/auth/login`
- [ ] Login as visitor: `visitor@flexcms.local` / `Visitor@123` →
      redirects to `/` (NOT `/admin` — visitor doesn't have admin
      permission)
- [ ] Visitor visits `/admin` → 403 Access Denied page (polished,
      not raw error)

**Common bugs to look for:**
- Wrong page title → SettingsService caching stale value; restart and
  retry, file a bug if it persists.
- Visitor lands on `/admin` → role-based landing page logic broken in
  `AuthController.Login`.

---

## Section 3 — Dashboard

**URL:** `/admin`

- [ ] Loads in <1s on a fresh DB (no posts/pages yet)
- [ ] Shows: Pages count, Posts count, Users count, Media count,
      Categories count, Roles count, Pending messages, Failed messages
- [ ] "Recent activity" panel shows the most recent audit entries
      (you'll have at least your own login)
- [ ] "System" panel shows version, runtime (.NET 10), OS

**Common bugs to look for:**
- All counts show 0 even after creating content → `IRepository.CountAsync`
  not honoring the active provider.
- "Recent activity" empty even though you've done admin actions →
  audit logging broken; verify `fcms_logs` collection / table has rows.

---

## Section 4 — Pages CRUD

**URL:** `/admin/pages`

- [ ] **List page** loads. If empty, shows the "No records" message.
- [ ] Click `+ New Page` → form with Title, Slug (auto-fills from Title
      via `<fcms-slug-input>`), Content (TinyMCE), Meta Title/Desc,
      Parent (dropdown), Sort Order, Published checkbox.
- [ ] Type a Title → Slug auto-fills with the slugified version.
- [ ] Manually edit Slug → from now on, changing Title does NOT
      override the slug (sticky-manual flag).
- [ ] Fill Content with TinyMCE (rich text + headings)
- [ ] Check Published, click Create Page
- [ ] Redirects back to `/admin/pages`, success toast
- [ ] Visit `/<your-slug>` on the public site → page renders with the
      content
- [ ] Edit the page from `/admin/pages` → existing values populate the
      form, slug field is editable (sticky-manual since it has a value)
- [ ] Delete a page → confirm modal appears (custom, not browser
      `confirm()`) → click Confirm → row disappears from list,
      success toast
- [ ] Visit `/<deleted-slug>` → 404
- [ ] Visit `/admin/trash` → deleted page is listed → click Restore →
      page returns to live list, public URL works again

**Common bugs to look for:**
- Slug doesn't auto-fill → `<fcms-slug-input>` tag helper not loaded
  (check `_ViewImports.cshtml` adds the framework's tag helpers).
- TinyMCE doesn't render → `_RichTextEditor` partial missing from the
  Scripts section of the view.
- Confirm dialog is the browser's native one → `_FcmsConfirm` partial
  missing from `_AdminLayout`, or `fcms-confirm.js` not loaded.

---

## Section 5 — Posts (Blog) CRUD + Featured Image Picker

**URL:** `/admin/posts`

- [ ] First create a Category at `/admin/categories/create` (e.g. "Tech")
- [ ] Visit `/admin/posts/create`
- [ ] Same slug auto-fill behavior as Pages, with `/blog/` prefix
- [ ] **Featured Image picker:**
  - Click the "Pick" button → modal opens with grid of recent images
    (empty on fresh install)
  - Click "Upload new" inside the modal → file dialog → pick a real PNG/JPG
  - Upload completes → modal closes → URL appears in the input field →
    thumbnail preview appears below the input
  - Click the "×" Clear button → URL clears, preview disappears
  - Re-pick the just-uploaded image from the grid → preview re-appears
- [ ] Pick a Category, set Published, write Content, click Create Post
- [ ] Visit `/blog` → card appears with the featured image rendered as
      `card-img-top`
- [ ] Click the card → `/blog/<slug>` → hero image renders at top of
      detail page
- [ ] Edit the post → existing featured image preview already visible
      (loaded from the saved URL)

**Common bugs to look for:**
- "Pick" button does nothing → `_MediaPicker` partial not in `@section
  Scripts`.
- Upload returns 200 but URL doesn't fill → `data-fcms-id-mode` confusion
  (Post uses URL mode, not ID mode — the input is `string?
  FeaturedImageUrl`, not `Guid?`).
- Card or detail doesn't show image → `Blog/Index.cshtml` or
  `Blog/Post.cshtml` `<img>` block guard wrong.

---

## Section 6 — Categories + Tags + Comments

- [ ] Create a category → list updates
- [ ] Edit category name → list reflects change
- [ ] Soft-delete category → moves to Trash
- [ ] Create a post with comma-separated tags → tags stored
- [ ] As a logged-out user, visit a published blog post → comment form
      appears (or "Login to comment" prompt depending on settings)
- [ ] Login as Visitor (`visitor@flexcms.local` / `Visitor@123`) → submit
      a comment → "Awaiting moderation" message
- [ ] Verify in DB: `fcms_comments` has the row with status pending
- [ ] As admin, navigate to comments moderation (likely under
      `/admin/comments`) → approve → reload public post → comment now
      visible

---

## Section 7 — Media Library

**URL:** `/admin/media`

- [ ] List view shows all uploaded media (just the picker upload from
      Section 5 if you went in order)
- [ ] Upload a real PNG/JPG via drag-drop → appears in list with
      thumbnail
- [ ] Upload an invalid file (e.g. `.exe` renamed to `.png`) → magic-byte
      validation rejects with "File content does not match the declared
      extension" message
- [ ] Click a media item → details panel/modal with URL, alt text, file
      size, dimensions
- [ ] Bulk-edit alt text → save → verify persisted
- [ ] Soft-delete a media item → moves to Trash
- [ ] Folder operations: create folder, rename, delete (folder containing
      media re-parents the items, doesn't lose them)

**Common bugs to look for:**
- Upload fails with "Cannot access a closed Stream" → SkiaSharp stream
  bug (already fixed in `MediaService.GetImageDimensions` —
  regression check).
- Upload fails with "execution strategy" error on MySQL → controller
  wrapping in explicit transaction (already fixed in `MediaController.Upload`
  — regression check).

---

## Section 8 — Users, Roles, Permissions

**URL:** `/admin/users`

- [ ] User list loads, shows admin + visitor accounts
- [ ] Create a new user → role multi-select shows all roles → assign
      "Editor" or similar → save → list updates
- [ ] Toggle user active/inactive → status badge updates without page
      reload (AJAX)
- [ ] Edit user → change name → save → list reflects change
- [ ] Delete user → confirm modal → user removed
- [ ] **Roles** (`/admin/roles`):
  - Create role "Test Role" → list updates
  - Click role → permissions accordion view
  - Search "delete" in permissions filter → only delete-related perms show
  - Check 2-3 permissions, save → audit log shows
    `Permission.Assigned` entries (NOT just generic
    `RolePermission.Created` — confirms the audit gap fix)
  - Uncheck → audit shows `Permission.Revoked`
- [ ] **Permissions** (`/admin/permissions`):
  - Page lists all permission keys grouped by Group
  - SuperAdmin role bypass works (no permission gates apply to your
    own admin user)
- [ ] Create a user, assign only the "Editor" role, login as that user
  - Sidebar shows only the menu items their permissions allow
  - User cannot access pages they don't have permission for (e.g.
    `/admin/users` if Editor doesn't have `users.manage`)

---

## Section 9 — Site Settings (Identity, Theme, Logo, Favicon)

**URL:** `/admin/settings`

- [ ] **Site Identity card:**
  - Change Site Name → save → page title in tab updates
  - Change Tagline → reflected in `<meta name="description">` on
    public site
  - Set a Logo via the picker → save → public navbar + admin sidebar
    show the logo image
  - Set a Favicon via the picker → save → browser tab shows the
    custom favicon
  - Clear logo/favicon → revert to default Bootstrap-icon brand +
    `/favicon.ico`
- [ ] **Appearance card (theme colors):**
  - Pick "Default" preset → form fields fill with Bootstrap defaults
  - Pick "Dark Pro" → fields update
  - Pick "Modern" → fields update
  - Save → public site primary color changes (e.g. button colors)
  - Toggle dark mode (sidebar account menu → Appearance → Dark) →
    dark color overrides apply
- [ ] **Locale & Display:**
  - Change Default Language → public site falls back to that language
    when visitor cookie is missing
  - Change Date format → admin date columns + audit log timestamps
    use the new format
- [ ] **Content & Audit:**
  - Toggle "Audit logging" off via switch → write to anything →
    `fcms_logs` does NOT get a new row → toggle back on → next write
    DOES log

**Common bugs to look for:**
- Page title still says "FlexCMS" after Save → SettingsService cache
  not invalidated on save (`SaveAsync` should call `Remove`).
- Favicon doesn't update after save → media URL not resolved (check
  `IMediaService.GetByIdAsync` is being awaited in both `_Layout` and
  `_AdminLayout`).
- Theme preset button doesn't fill fields → JS preset object missing
  in `_ThemeSettingsCard`.

---

## Section 10 — Menu Builder

**URL:** `/admin/menu`

- [ ] Lists current admin sidebar menu (Dashboard, Blog group, Pages,
      etc.)
- [ ] Drag-drop reorder → persists after page refresh
- [ ] Rename "Posts" to "Articles" → admin sidebar (after refresh)
      shows "Articles"
- [ ] Add a new top-level item → appears in sidebar after refresh
- [ ] Delete custom item → gone from sidebar

---

## Section 11 — Module Upload + Activation

**URL:** `/admin/modules`

- [ ] Module list loads (empty initially, or shows Sample.Hello if
      pre-deployed)
- [ ] **Drop-in install:**
  - On the host, copy a built module folder (DLL + module.json) into
    `src/FlexCms.Host/Modules/<ModuleId>/`
  - Restart the app
  - Module appears in the list as "Active"
  - DB has a row in `fcms_module_records`
  - Module's controller routes work (e.g. for Sample.Hello, GET
    `/hello?name=World` returns "Hello, World!")
- [ ] **Deactivate:**
  - Click Deactivate → confirm → status becomes "Inactive"
  - After restart, module's routes return 404
- [ ] **Re-activate:**
  - Click Activate → status flips back → routes work again after
    restart
- [ ] **Uninstall (Keep Tables):**
  - Click Uninstall → choose "Keep Tables" → next restart, module
    folder is deleted, DB data intact (re-installing the same module
    finds existing data)
- [ ] **Uninstall (Drop Tables):**
  - Repeat with "Drop Tables" → after restart, module's tables/
    collections are dropped too

**Common bugs to look for:**
- Module doesn't appear after restart → wrong path. The host loads
  from `<ContentRoot>/../modules/<id>/`. For `dotnet run` from
  `src/FlexCms.Host/`, that resolves to `src/modules/`. For published
  binaries on a VM, it's `<publish>/../modules/`.
- Deactivate doesn't hide routes → `ModuleActivationService` doesn't
  re-evaluate on activation status change without restart. Document
  the restart requirement.

---

## Section 12 — Sitemap, RSS, Search, Robots, 404

- [ ] `/sitemap.xml` → 200, valid XML, contains all published pages
      and posts as `<url>` entries
- [ ] `/rss` → 200, valid RSS 2.0, channel `<title>` reads
      `<YourSiteName> — Blog`, items are latest published posts
- [ ] `/search?q=<word>` → renders results from both Pages and Posts
      that contain the word in title or content
- [ ] `/robots.txt` → returns 200 with the configured robots content
- [ ] Visit `/this-does-not-exist-xyz` → polished 404 page
      ("Lost in space" / glass-card design)
- [ ] Trigger a 500 (e.g. force-throw via dev exception page disabled)
      → polished error page with copy-error-id button

---

## Section 13 — Light/Dark Mode + Language Switcher

**Public site:**

- [ ] Open `/` → light mode by default
- [ ] Click the theme toggle in the navbar → dropdown with Light /
      Dark / Auto
- [ ] Pick Dark → page reloads, `<html data-bs-theme="dark">`, body
      bg goes dark
- [ ] Pick Auto → respects OS theme preference
- [ ] Pick Light → reverts
- [ ] Click language switcher → dropdown shows EN + BN
- [ ] Pick BN → navbar items render in Bengali (হোম, ব্লগ, etc.)
- [ ] Pick EN → revert

**Admin sidebar account menu:**

- [ ] Click your account widget at bottom-left → dropdown opens
      UPWARD with proper 8px gap from trigger
- [ ] Trigger button stays highlighted (active state) while menu open
- [ ] Chevron flips to point down while menu open
- [ ] Pick a theme from the Appearance section → admin theme switches
- [ ] Pick a language from the Language section → admin UI switches
- [ ] Click outside the menu → closes; trigger highlight clears,
      chevron flips back up

---

## Section 14 — Mobile Responsive

Resize browser to ~375px width (or use DevTools mobile emulator):

- [ ] Public navbar collapses to hamburger
- [ ] Click hamburger → menu drawer opens with all nav items
- [ ] Admin sidebar slides off-screen by default on mobile
- [ ] Click hamburger in admin topbar → sidebar slides in with
      backdrop overlay; tapping backdrop closes
- [ ] Account dropup still works on mobile (opens upward, doesn't get
      clipped)

---

## Section 15 — Audit Log

**URL:** `/admin/audit-log`

- [ ] Page loads with two DataTables: Recent + Archive
- [ ] Recent shows entries from your test session (logins, creates,
      updates, deletes)
- [ ] Each row shows: Time, User, Action, Entity, Module, Severity,
      Value (JSON)
- [ ] Click "Force Archive" → entries older than 24h move to Archive
      table
- [ ] Permission changes from Section 8 show as `Permission.Assigned`
      / `Permission.Revoked` (NOT just generic `RolePermission.Created`)
- [ ] Settings save shows as `settings.save` action (from
      `[FcmsLog]` attribute on controller)
- [ ] Login shows in `LoginHistoryService` (separate from main audit
      log; check via `/admin/users/<id>/login-history` if such a page
      exists, or query DB directly)

---

## Section 16 — Backup & Restore Drill

This is the critical drill — do it once before going live.

- [ ] Run the backup script (manually or via the documented cron
      one-shot): produces `mongo-<TS>.gz` + `app_data-<TS>.tgz`
- [ ] Verify file sizes are non-zero and reasonable (Mongo dump bigger
      than 1KB, app_data tarball includes `setup.json` + `keys/` +
      `uploads/`)
- [ ] **Restore drill** — do this in a throwaway VM or local Docker
      setup, NOT on prod:
  - Spin up a fresh VM following DEVELOPER_GUIDE §9 (single-VM
    direct install) up to "Step 5 systemd unit registered, NOT
    started"
  - SCP the two backup files
  - Stop the new flexcms service, restore App_Data tarball, restore
    Mongo dump
  - Edit `setup.json` MongoConnectionString to match the new VM's
    creds if different
  - Start flexcms → public site comes up identical to source
  - Login with original admin credentials → works
  - All pages, posts, users, media accessible

**This drill is not optional.** Doing it once on a non-prod VM
catches 90% of "I have backups" → "I have a working restore"
gap problems.

---

## Section 17 — Performance smoke test

Light load test from a separate machine (or `localhost` if no other):

- [ ] `wrk -t4 -c100 -d30s http://localhost:5000/blog` →
      reasonable throughput (>100 req/s on a 4GB VM), p95 latency
      <500ms, error rate 0
- [ ] Same against `/admin/audit-log/datatable-recent` (POST with
      antiforgery token) → handles concurrent reads cleanly
- [ ] During the test, watch:
  - `top` / `htop` for memory growth
  - `/admin/audit-log/datatable-recent` not flooding the audit log
    with self-references
  - DB query log (MySQL `general_log` or Mongo `--profile=2`) for
    obvious N+1 patterns

---

## Section 18 — Cross-provider parity smoke

After completing Sections 1–17 on **MySQL**, drop the database, delete
`setup.json`, and redo Sections 1–11 on **MongoDB**. Specifically
verify:

- [ ] Setup wizard completes against Mongo (`directConnection=true`
      conn string)
- [ ] All admin pages load 200 (especially `/admin/audit-log` and
      `/admin/redirects` — these used to break on Mongo before recent
      fixes)
- [ ] Pages, Posts, Categories, Comments, Media all CRUD-able
- [ ] Search works (both EF LIKE and Mongo `$regex` translate from
      `string.Contains`)
- [ ] `MongoUnitOfWork` transactions work (verified by
      `MongoTransactionTests` integration test against the dev
      docker-compose replica set; manually: trigger a multi-write
      operation that's wrapped in a transaction, kill the app
      mid-flight, verify nothing partial in DB)

---

## Final pre-ship checklist

When all 18 sections pass on both providers:

- [ ] All 521 unit tests pass
- [ ] All 306 integration tests pass (including Docker MySQL + Mongo
      replica set)
- [ ] No new exceptions in `App_Data/logs/` during the manual sweep
- [ ] Backup cron installed and `flexcms-backup.log` shows recent
      "Backup OK" line
- [ ] Restore drill completed at least once on a throwaway VM
- [ ] TLS cert is valid (`certbot certificates` shows >30 days
      remaining)
- [ ] `setup.json` is NOT in git (`git ls-files | grep setup.json`
      returns nothing)
- [ ] `.env` with prod secrets is NOT in git (same check)
- [ ] DataProtection keys (`App_Data/keys/`) are backed up — losing
      them means losing every encrypted setting + every active session

When all of these are ticked, the framework is **ready for the
e-commerce module** to be built on top of it.

---

## What this guide does NOT cover (separate concerns)

- **Load testing under realistic e-commerce traffic** — comes when
  the e-commerce module exists. Will need 100-500 concurrent shopper
  simulation with full checkout flow.
- **Payment gateway end-to-end testing** — needs sandbox credentials
  for bKash / SSLCommerz / Nagad and live network.
- **Email delivery** — needs real SMTP. Mailhog catches in dev; for
  prod do a one-time send-real-email test before going live.
- **TLS / certbot renewal under failure modes** — requires a
  real-domain VM to test fully.
- **Module marketplace / signing** — not implemented yet (noted in
  framework docs as future work).

These belong in the e-commerce module's own pre-ship test guide,
written when that module is built.
