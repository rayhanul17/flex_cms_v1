# Phase 9 — Admin UX + Notifications + Widgets + Audit: Manual Test Cases

> **Automated coverage**: 12 unit tests (honeypot guard rails + widget
> manager rendering / ordering / disabled / unknown / exception isolation /
> reorder / delete / get) + 9 integration tests (notification per-user /
> broadcast / recent / unread count / mark-read ownership; Mongo persist /
> filter / update). All passing. Project total: 235 unit + 199 integration.
>
> **Note**: Items already shipped in earlier phases:
> - Audit log + `FcmsLogService` → Phase 6 (`FcmsLog`, `FcmsLogArchive`,
>   `FcmsLogJsonResolver` auto-clean). No Phase 9 audit work needed.
> - Toast / confirm / dialog JS APIs → Phase 6.
> - DataTable JS helper → Phase 6.
> - Dynamic Menu System (sidebar groups, parent-children) → Phase 6.

## 1. Notification bell icon (admin top-bar)

| # | Action | Expected |
|---|--------|----------|
| 1.1 | Login → admin top bar shows bell icon next to language switcher. | `_NotificationBell` partial rendered for any admin layout. |
| 1.2 | If unread > 0 → red badge with count (caps at "99+"). | Updates on next 60s poll. |
| 1.3 | Click bell → dropdown shows last 10 notifications (newest first), each with icon, title, body snippet, time. | Title + body HTML-escaped. |
| 1.4 | Unread items show light-grey background; read items white. | Visual distinction. |
| 1.5 | Click an item → marks read → navigates to `n.url` if set. | POST `/admin/notifications/mark-read/{id}` with `X-FlexCms-Csrf` header. |
| 1.6 | Click "Mark all read" → all my unread flip → badge disappears → list refreshes. | POST `/admin/notifications/mark-all-read`. |
| 1.7 | Open admin in two tabs → `MarkRead` in tab 1 → tab 2's badge updates within 60s. | Polling covers cross-tab state. |

## 2. NotificationService API (programmatic)

| # | Action | Expected |
|---|--------|----------|
| 2.1 | Trigger `NotifyUserAsync(userId, "x")` from any service → row appears in DB; bell badge bumps for that user only. | Verified by `NotifyUserAsync_inserts_one_row_with_correct_payload`. |
| 2.2 | `NotifyAllAsync("ping")` with N users → N rows inserted. | Verified by `NotifyAllAsync_inserts_one_row_per_user`. |
| 2.3 | Mark Bob's notification as Alice → no-op (ownership check). | Verified by `MarkReadAsync_only_affects_owner`. |
| 2.4 | DB row has `level` enum + `url` + `icon` round-tripped. | Verified by `Add_persists_with_typed_fields` (Mongo). |

## 3. Widget system

Widgets are not rendered by any built-in zone yet; verify the manager API
behaves correctly. (Module authors will mount widgets into their themes.)

| # | Action | Expected |
|---|--------|----------|
| 3.1 | Module's `RegisterServices` adds `services.AddSingleton<IFcmsWidget, MyWidget>();` → it appears in `IFcmsWidgetManager.RegisteredWidgets`. | DI multi-bind. |
| 3.2 | Admin (or seed code) calls `AddAsync(widgetId, "Sidebar", sortOrder)` → row in `fcms_widget_placements`. | Verified by `RenderZoneAsync_renders_enabled_placements_in_sort_order`. |
| 3.3 | `RenderZoneAsync("Sidebar")` returns concatenated HTML in `SortOrder` ascending. | Same test. |
| 3.4 | Set `Enabled=false` on a placement → that widget skipped. | `RenderZoneAsync_skips_disabled_placements`. |
| 3.5 | Stale row pointing at unknown `WidgetId` → silently skipped (warn-logged). | `RenderZoneAsync_silently_skips_unknown_widget_ids`. |
| 3.6 | One widget throws → other widgets in the zone still render. | `RenderZoneAsync_isolates_per_widget_exceptions`. |
| 3.7 | `ReorderZoneAsync(zone, ids)` → `SortOrder` updated to match input order. | `ReorderZoneAsync_updates_SortOrder_to_match_input_order`. |

## 4. Honeypot

| # | Action | Expected |
|---|--------|----------|
| 4.1 | Form includes `<fcms-honeypot />` → page source has hidden `fcms_hp` input wrapped in absolute-positioned off-screen div with `aria-hidden="true"` + `tabindex="-1"`. | Real users + screen readers don't see it. |
| 4.2 | Bot fills all fields including `fcms_hp=anything` → controller calls `IFcmsHoneypotService.IsLegit(Request.Form)` → returns false → return BadRequest with no body. | Silent rejection — bot operator gets no feedback to iterate against. |
| 4.3 | Real user submits with `fcms_hp` empty/missing → IsLegit=true → form processes normally. | Verified by `IsLegit_returns_true_when_*` tests. |

## 5. Dashboard (`/admin`)

| # | Action | Expected |
|---|--------|----------|
| 5.1 | Page shows 8 stat cards: Pages, Posts (Published), Users, Media, Categories, Roles, Pending messages, Failed messages. | Click each → drills into the corresponding admin index. |
| 5.2 | Recent Activity panel shows last 10 audit-log entries (when, action code, entity type, user). | "View all →" link goes to `/admin/audit-log`. |
| 5.3 | System panel shows app version + .NET runtime + OS. | Read from assembly + `RuntimeInformation`. |
| 5.4 | First load is slow on a populated DB; subsequent loads within 5 minutes are fast. | `IMemoryCache` 5-minute entry. |

## 6. View render service

| # | Action | Expected |
|---|--------|----------|
| 6.1 | Background email service calls `IFcmsViewRenderService.RenderAsync<TModel>("~/Views/Email/Welcome.cshtml", model)` → returns the rendered HTML string. | Resolves view via `ICompositeViewEngine.GetView` for paths. |
| 6.2 | View not found → throws `InvalidOperationException` with searched-locations list. | Catch in caller and surface a clear error. |
| 6.3 | Used inside a widget → tag helpers + partials work as in a normal MVC request. | Same Razor engine. |

## 7. Database storage cross-check

- **EF**: `SELECT user_id, COUNT(*) FROM fcms_notifications WHERE is_read=false GROUP BY user_id;` → matches bell badges.
- **Mongo**: `db.fcms_notifications.find({userId: BinData(...), isRead: false})` uses index `ix_notifications_user_unread_created`.
- **Widget placements EF**: `SELECT zone, sort_order, widget_id FROM fcms_widget_placements ORDER BY zone, sort_order;`.
- **Widget placements Mongo**: index `ix_widget_placements_zone_enabled_sort` confirmed by [MongoIndexService](../src/FlexCms.Framework/Db/MongoDb/MongoIndexService.cs).

## 8. Edge cases

| # | Action | Expected |
|---|--------|----------|
| 8.1 | User soft-deleted → notifications still exist; bell stops loading once user can't sign in. | Soft delete is on FcmsUser; FK is loose. |
| 8.2 | Notification `Url` is external (`https://...`) → click navigates to it. | Browser-level — controller doesn't sanitize. |
| 8.3 | `NotifyAllAsync` while 0 users exist → returns 0, no rows inserted, no error. | Verified by guard `if (ids.Count == 0) return 0`. |
| 8.4 | Disable a widget placement, re-enable → next zone render includes it again. | `Enabled` toggle is the only state. |

## 9. Out of scope (future phases)

- **Real-time notification push (SignalR)** — Phase 10 (Chat) will introduce SignalR; this phase is poll-only.
- **Notification email/SMS fallback** ("if not read in 5 min, send email") — Phase 13 / 14.
- **Per-notification "actions" buttons** (Approve / Reject inline) — module-specific extension.
- **GDPR export of notifications** — Phase 15 (Compliance).
