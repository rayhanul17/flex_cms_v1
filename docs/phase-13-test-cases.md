# Phase 13 — Auth Hardening + Account Lifecycle: Manual Test Cases

> **Automated coverage**: 15 unit (LoginRedirectService 10 — priority chain,
> open-redirect blocking, role precedence, malformed JSON, anonymous;
> BuiltInHealthChecks 5 — queue thresholds, readiness flag, disk-space
> healthy path) + 8 integration (SessionService 5 — record / active list /
> validity / revoke-all / touch; LoginHistoryService 3 — record / recent
> ordering / failed-since-time filter). Project total: 279 unit + 224
> integration.
>
> **Scope delivered**: health checks (Issue 67), session tracking + force-
> logout API (Issue 68), login-history (Issue 69), database resilience
> (already in earlier phases via `EnableRetryOnFailure(3)` — Issue 92),
> environment banner (Issue 91), login redirect service (Issue 102).
>
> **Deferred**: 2FA TOTP (Issue 71), OAuth providers (Issue 72), full email-
> verification flow polish (Issue 70), custom 401/403/404/500 styled pages
> (Issue 103) — require external service integration or substantial UI
> design work; the framework's existing error handling middleware covers
> the unstyled defaults.

## 1. Health endpoints (`/health`, `/health/ready`, `/health/live`)

| # | Action | Expected |
|---|--------|----------|
| 1.1 | `curl http://localhost/health` → 200 with JSON `{"status":"healthy","checks":[{database, background-queue, disk}]}`. | Anonymous; no auth required. |
| 1.2 | Stop the database → `curl /health/ready` → 503 with `{"status":"unhealthy","checks":[...]}`. | The DB check sets `IncludeInReadiness=true`. |
| 1.3 | `/health/live` → always 200 (just confirms the process is alive). | Used as K8s liveness probe so transient DB failures don't trigger pod restarts. |
| 1.4 | Background queue at 85% capacity → `/health` reports `degraded` for that check (still 200 on full endpoint, queue not in readiness roll-up). | Verified by `BackgroundQueue_health_returns_degraded_above_80pct`. |
| 1.5 | Disk free <500 MB → degraded, <100 MB → unhealthy. | Threshold-based. |
| 1.6 | A check throws → caught + returned as `unhealthy` with the exception message. | `SafeAsync` wrapper in `HealthController`. |

## 2. Active sessions

| # | Action | Expected |
|---|--------|----------|
| 2.1 | Login from Chrome → `SessionService.RecordLoginAsync(userId, sessionId, ip, ua, deviceLabel)` writes a row. | Manual: trigger from `AuthController.Login` (extension to caller — service is in place). |
| 2.2 | Same user logs in from Firefox → 2nd row, both `IsRevoked=false`. | Verified by `GetActiveAsync_returns_only_non_revoked_for_user`. |
| 2.3 | Profile → "Active sessions" page shows 2 rows with IP / UA / device label. | UI deferred; service ready. |
| 2.4 | Revoke session 1 → `SessionService.RevokeAsync` marks IsRevoked + ts. Next request from that browser fails the validation middleware → forces re-login. | Middleware deferred; `IsValidAsync` covers the check. |
| 2.5 | Password change → `RevokeAllForUserAsync(userId, byUser, "password change")` flips every active row. | Verified by `RevokeAllForUserAsync_flips_only_active_rows_for_user`. |
| 2.6 | TouchAsync(sessionId) bumps LastSeenAt without other side-effects. | Verified by `TouchAsync_bumps_LastSeenAt_for_active_session`. |

## 3. Login history (admin Security Dashboard data source)

| # | Action | Expected |
|---|--------|----------|
| 3.1 | Wrong password → row inserted with `Outcome=InvalidCredentials`, `UserId=null`, `AttemptedUserName="alice"`, `IpAddress`, `UserAgent` populated. | Wired in `AuthController.Login`. |
| 3.2 | Lockout → `Outcome=LockedOut`. | Wired. |
| 3.3 | Successful login → `Outcome=Success`, `UserId` set to the resolved user. | Wired. |
| 3.4 | `LoginHistoryService.GetRecentAsync(100)` → newest first, capped at 100. | Verified by `GetRecentAsync_orders_newest_first_and_caps_at_max`. |
| 3.5 | `GetFailedCountSinceAsync(DateTime.UtcNow.AddHours(-1))` → drives the "failures in the last hour" widget on the security dashboard. Excludes Success outcomes. | Verified by `GetFailedCountSinceAsync_filters_by_time_and_outcome`. |
| 3.6 | Append-only: never updated/deleted by app code. | Verified by inspection — service has only Record + read methods. |

## 4. Login redirect resolution (`ILoginRedirectService`)

Priority chain (first non-empty + safe wins):

1. Caller-supplied `returnUrl` if local
2. User claim `fcms.landing_page` if local
3. Per-role mapping from `SiteSettings.DefaultRoleLandingPagesJson`, with role precedence `SuperAdmin > Admin > Editor > Author > Subscriber > others`
4. `SiteSettings.FallbackLandingPage`, default `/`

| # | Scenario | Expected |
|---|----------|----------|
| 4.1 | Editor logs in (no returnUrl, no claim) → `/admin/cms/posts` (from JSON map). | Verified. |
| 4.2 | Subscriber logs in → `/profile`, NOT `/admin` (which would 403). | Verified. |
| 4.3 | User clicks protected `/admin/blog`, gets bounced to login, signs in → goes to `/admin/blog` (returnUrl wins). | Verified. |
| 4.4 | `returnUrl=https://evil.com/x` → blocked by `isLocalUrl` check → falls through to role map. | Verified by `ReturnUrl_blocked_when_external`. |
| 4.5 | User has `fcms.landing_page=/admin/blog/drafts` claim → wins over role map. | Verified. |
| 4.6 | Per-user landing page that's external → blocked. | Verified. |
| 4.7 | User has both Editor + Subscriber roles → Editor (higher precedence) wins. | Verified by `Multi_role_user_uses_highest_precedence`. |
| 4.8 | Settings JSON map malformed → falls through to fallback (no exception). | Verified. |
| 4.9 | Anonymous principal → fallback. | Verified. |

## 5. Environment banner

| # | Action | Expected |
|---|--------|----------|
| 5.1 | Run `ASPNETCORE_ENVIRONMENT=Development dotnet run` → admin pages show red banner "DEVELOPMENT ENVIRONMENT" at top. | Inserted by `<fcms-env-banner />` in `_AdminLayout`. |
| 5.2 | `Staging` env → orange banner "STAGING ENVIRONMENT". | Per-env color in tag helper. |
| 5.3 | `Production` env → no banner rendered. | `output.SuppressOutput()`. |
| 5.4 | Custom env name (e.g. "QA") → purple banner with the name uppercased. | Fallback branch. |

## 6. Database storage cross-check

- **EF**: `SELECT outcome, COUNT(*) FROM fcms_login_history GROUP BY outcome;` — outcome enum values 0–5 line up with `LoginOutcome`.
- **EF sessions**: `SELECT user_id, COUNT(*) FROM fcms_user_sessions WHERE is_revoked=false GROUP BY user_id;` — per-user active count.
- **Mongo indexes**: `ix_user_sessions_user_revoked`, `ux_user_sessions_session_id`, `ix_login_history_outcome_created`, `ix_login_history_attempted_user` all confirmed by [MongoIndexService](../src/FlexCms.Framework/Db/MongoDb/MongoIndexService.cs).

## 7. Out of scope (deferred)

- **`FcmsSessionValidationMiddleware`** — the `ISessionService.IsValidAsync` API is in place but the per-request middleware that enforces it isn't wired into the request pipeline yet. Force-logout requires both halves; ship the middleware in the next pass when the active-sessions UI is built.
- **2FA TOTP + recovery codes (Issue 71)** — Identity supports it natively; admin UI + flow design needed.
- **OAuth providers (Issue 72)** — Google/Facebook/Microsoft/GitHub need real client credentials + UI flow.
- **Email verification polish (Issue 70)** — Identity already exposes `ConfirmEmailAsync`; "resend link" button + `SiteSettings.RequireEmailVerification` enforcement need UI work.
- **Custom 401/403/404/500 styled pages (Issue 103)** — `ErrorController` + `UseStatusCodePagesWithReExecute` are wired in earlier phases; bespoke styled views with admin-overridable mapping are next.
- **Database connection resilience configuration UI** — `EnableRetryOnFailure(3)` is hardcoded in `FcmsServiceExtensions`; admin-tunable retry count + timeout is a future enhancement.
