# Post-Phase Hardening — Manual Test Cases

> **Scope**: features added AFTER the formal Phase 1–17 work, in response
> to the deep code audit. These items aren't tied to a single plan phase
> — they touch multiple. Cross-references back to the originating phase
> are noted per-section.
>
> **Automated coverage**: 41 new automated tests across these features
> (24 unit + 17 integration). Project total at this point: **457 unit +
> 267 EF integration tests**, all green.

## 1. Critical perf fixes (Phase 6 + Phase 9)

| # | Action | Expected |
|---|--------|----------|
| 1.1 | Upload a 5 GB file via `/admin/media/upload` (use `dd if=/dev/zero of=big.bin bs=1M count=5120`). | 400 with "File exceeds maximum upload size of {N} MB" message; **server stays up** (no OOM). Previously: heap exhaustion + crash. |
| 1.2 | Lower `SiteSettings.MaxUploadSizeMb` to 5 → upload 6 MB → expect rejection. | Setting honored. |
| 1.3 | `MaxUploadSizeMb=0` (misconfigured) → upload 200 MB → accepted (falls back to `MediaService.AbsoluteMaxBytes` = 256 MB ceiling). | Defensive default. |
| 1.4 | Open `/admin/users` with 200+ seeded users → check page render time + Slow-Query log. | Single round-trip for users + single join for roles. Previously N+1. |
| 1.5 | Run audit log archive with 50,000+ old logs → memory stays bounded. | 1000-row batches with detach-after-each. Previously OOM. |

## 2. Preview tokens (Phase 5 / Phase 16 editorial workflow)

| # | Action | Expected |
|---|--------|----------|
| 2.1 | Edit a draft Page → click "Get share link" → controller posts to `/admin/authoring/preview-token/issue` → admin gets a URL like `https://yoursite.com/about?preview={43-char-token}`. | Token is base64-url-safe, ~43 chars. |
| 2.2 | Open the shared URL in an incognito window (no admin login) → page renders even though `IsPublished=false`. | Preview gate honors token. |
| 2.3 | Same incognito session → response headers contain `X-Robots-Tag: noindex, nofollow`. | Crawler protection. |
| 2.4 | Anonymous viewing a draft post via `?preview=...` → no view-count increment on the post. | `IncrementViewCountAsync` skipped during preview. |
| 2.5 | Change one character in the token → 404. | Constant-time comparison rejects. |
| 2.6 | Wait 7 days (or set `PreviewTokenExpiresAt` in past) → token returns 404. | Default 7-day lifetime. |
| 2.7 | Issue a new token for the same page → old token stops working. | Single-token-per-entity policy. |
| 2.8 | Click "Revoke preview link" in admin → next request with token = 404. | Revoke clears the field. |

## 3. Draft auto-save (Phase 5)

| # | Action | Expected |
|---|--------|----------|
| 3.1 | Open `/admin/posts/{id}/edit` → start typing → wait 30s → check Network tab → POST to `/admin/authoring/autosave` fires. | Editor JS heartbeat. |
| 3.2 | Close the browser tab without explicit save → reopen the same edit page → page shows "Restore unsaved draft from {timestamp}?" prompt. | Snapshot newer than entity's `UpdatedAt`. |
| 3.3 | Click "Restore" → fields populate with snapshot values. | `GET /admin/authoring/autosave/peek` returns latest snapshot. |
| 3.4 | Click explicit Save → snapshot discarded. | `POST /admin/authoring/autosave/discard` after successful save. |
| 3.5 | Two users edit the SAME post in different tabs → each gets their OWN snapshot (per-user isolation). | Unique index `(EntityType, EntityId, UserId)`. |

## 4. 2FA via email/SMS OTP (Phase 13 — replaces deferred TOTP)

| # | Action | Expected |
|---|--------|----------|
| 4.1 | User → `/profile/security/two-factor` → choose "Email" channel → click "Enable 2FA". | 10 recovery codes shown ONCE (XXXXX-XXXXX format with no-ambiguous-chars alphabet). |
| 4.2 | Copy the codes via the "Copy to clipboard" button → store in password manager. | After page reload they're gone (TempData). |
| 4.3 | Sign out → sign in with valid password → redirected to `/auth/two-factor` showing masked email (`r******l@example.com`). | Cookie `fcms.pending2fa` set, HttpOnly, SameSite=Strict, 10-min expiry, path-scoped to `/auth/two-factor`. |
| 4.4 | Check inbox → email arrived with 6-digit code, body says "expires in 5 minutes". | OTP via `IFcmsEmailService`. |
| 4.5 | Enter the code → login completes → redirected per role (or returnUrl if set). | Session-id claim issued; login history records "2FA verified". |
| 4.6 | Wrong code → "Invalid code" → 4 more wrong attempts → "Too many wrong attempts — request a new code". | Per-OTP attempt counter caps at 5. |
| 4.7 | Wait 5+ min after code was issued → enter a fresh code → "Code expired — request a new one". | OTP lifetime constant. |
| 4.8 | Click "Resend code" → new code sent; previous one no longer accepted (hash overwritten). | `OtpChallengeService.IssueAsync` rotates. |
| 4.9 | Lost device → enter a recovery code (XXXXX-XXXXX) instead of OTP → login succeeds → that code marked used. | `VerifyRecoveryCodeAsync`. |
| 4.10 | Try the same recovery code again → fails. | Single-use enforced. |
| 4.11 | User on SMS channel → enter a phone number → enable → SMS arrives with `FlexCMS code: 123456. Expires in 5 min.`. | Routed via Phase 8 `IFcmsSmsSender`; respects `BulkSmsType` for Bengali content path. |
| 4.12 | User without email + without phone → "No deliverable channel" error blocks enrollment. | `IssueAsync` returns `Success=false`. |
| 4.13 | Two admins concurrently call `AddToRoleAsync` on the same user → second call returns `IdentityResult.Failed("ConcurrencyFailure")`. | MongoUserStore ConcurrencyStamp check (Mongo only). |
| 4.14 | Admin → Profile → 2FA → "Regenerate recovery codes" → previous batch invalidated. | Drop + recreate. |
| 4.15 | Admin → Profile → 2FA → "Disable 2FA" → next login skips the OTP step entirely. | `TwoFactorEnabled` and `TwoFactorChannel` reset. |

## 5. Bulk alt-text editor (Phase 6 / Phase 16 a11y)

| # | Action | Expected |
|---|--------|----------|
| 5.1 | Admin → Media → Bulk alt-text editor (`/admin/media/alt-text`) → tabular view of every image in the current folder. | Images-only filter (no PDFs / videos / audio). |
| 5.2 | Toggle "Only show images missing alt" → list filters to images where `AltText` is null/empty. | Query param `missingOnly=true`. |
| 5.3 | Folder dropdown → pick a sub-folder → list refreshes. | Query param `folderId={guid}`. |
| 5.4 | Edit an alt input → "Missing" badge live-updates to "OK"; bottom counter shows "1 unsaved". | Client-side dirty tracking. |
| 5.5 | Click "Save all changes" → POST sends ONLY the dirty rows; response says "Updated N item(s)". | `BulkUpdateAltTextAsync`. |
| 5.6 | Re-baseline test: after save, badges still correct; "0 unsaved"; Save button disabled. | UI stays consistent with persisted state. |
| 5.7 | Whitespace-only entry "   " → save → DB stores `null` (not "   ") so screen-readers + validators agree on "missing". | Trim-and-coalesce. |
| 5.8 | Stale form: someone deletes a media item between page load + save → dead id silently skipped, live ones still update. | Robust against concurrent edits. |
| 5.9 | Sign in as a user with `media.view` but not `media.edit` → editor loads but saves return 403. | Permission-gated POST. |

## 6. Rate-limit policies (Phase 13 hardening)

| # | Action | Expected |
|---|--------|----------|
| 6.1 | POST `/auth/register` 6 times within an hour from the same IP → 6th attempt → 429. | `register:{ip}` policy: 5/hour. |
| 6.2 | POST `/auth/forgot-password` or `/auth/reset-password` 6 times in a minute → 6th → 429. | `otp` policy: 5/min. |
| 6.3 | POST `/comments` 11 times in 5 minutes → 11th → 429. | `comment` policy: 10 per 5 min. |
| 6.4 | POST `/forms/submit` (forms-builder endpoint) — same `comment` partition → shares the 10 / 5 min budget. | Single bucket. |
| 6.5 | POST `/payment/webhook/{gateway}` 61 times in a minute → 61st → 429. | `webhook:{ip}` policy: 60/min — defense against runaway gateway retries. |
| 6.6 | POST `/subscribe` 6 times in an hour → 6th → 429. | `subscribe:{ip}` policy: 5/hour. |
| 6.7 | POST anything outside the limited paths → no 429 regardless of frequency. | `GetNoLimiter("none")` fallback. |

## 7. Hotlink protection (Phase 6)

| # | Action | Expected |
|---|--------|----------|
| 7.1 | Set `SiteSettings.PreventHotlinking=false` → `<img src>` from `evil.com` to `/uploads/hero.jpg` works. | Toggle off = pass. |
| 7.2 | Toggle ON → repeat → 403 "Hotlinking not permitted." | Same-origin check fails. |
| 7.3 | Same-origin (`<img>` on a page on YOUR domain) → 200. | Same Host as Request.Host = pass. |
| 7.4 | Direct browser-bar visit (no Referer) → 200. | Empty Referer = pass (legitimate user). |
| 7.5 | Add `partner.com` to `SiteSettings.HotlinkWhitelist` → embed from Partner.com → 200. | Whitelist match (case-insensitive). |
| 7.6 | Malformed Referer header → 403. | Fail closed. |
| 7.7 | Request to a non-`/uploads/` path with hotlink ON → no impact (pre-filter skip). | Performance: settings lookup only on `/uploads/*`. |

## 8. EF/Mongo backend consistency (Phase 1 + Phase 15 + Phase 16)

> Run each test on BOTH backends. Connection strings are configured in
> `setup.json`; switch by changing `Provider` and restarting.

| # | Action | Expected |
|---|--------|----------|
| 8.1 | EF: `IFcmsBackupService.CreateBackupAsync()` → ZIP under `App_Data/backups/`. Restore round-trip → DbSets restored. | EF impl. |
| 8.2 | Mongo: same call → ZIP with `_metadata.json` containing `"backend": "mongo"` + `entities/{collection}.json` files. Restore → collections dropped + reseeded. | `MongoBackupService` impl. |
| 8.3 | Both: search for "react" via `IFcmsSearchProvider.SearchAsync("react")`. EF runs LIKE-based PageSearchSource + PostSearchSource; Mongo runs `MongoSearchSource` regex. Same SearchHit shape. | Auto-registered per backend. |
| 8.4 | EF: edit a Page in two tabs → save in tab2 → `DbUpdateConcurrencyException` / FcmsConcurrencyException → tab1 sees "Another editor saved first". | RowVersion column. |
| 8.5 | Mongo: same scenario → `FcmsConcurrencyException` thrown by `MongoRepository.UpdateAsync`. | Reflection-based RowVersion check. |
| 8.6 | Both: query `IRepository<FcmsLog>.GetAllAsync()` after some entries → returns ALL logs regardless of `Status` value. | `IAppendOnlyEntity` marker bypasses soft-delete filter on both backends. |
| 8.7 | Mongo single-node deployment: any service that calls `BeginTransactionAsync` → warning logged ONCE about replica set requirement; request continues without atomicity. | Graceful degrade. |
| 8.8 | Mongo replica-set deployment: same call → real transaction starts; commit/rollback work. | Latched on after first probe. |
| 8.9 | Mongo: insert `FcmsFeatureFlag` with duplicate `Key` → write rejected at DB level. | Unique index `ux_feature_flags_key` (added in audit fix). |
| 8.10 | Mongo: same for `FcmsLanguage.Code`, `FcmsSeoMeta.(EntityType,EntityId)`, `FcmsContentDraftSnapshot.(EntityType,EntityId,UserId)`. | All 12 added indexes verified. |
| 8.11 | Mongo: query Page with `RowVersion` field → driver round-trips byte[] correctly. | `BsonRepresentation` behavior. |
| 8.12 | Mongo: two admins call `userManager.AddToRoleAsync(user, "Editor")` simultaneously → second returns `IdentityResult.Failed("ConcurrencyFailure")`. | ConcurrencyStamp filter on `MongoUserStore.UpdateAsync`. |

## 9. Decryption silent-failure logging

| # | Action | Expected |
|---|--------|----------|
| 9.1 | Configure SMTP password → wipe `App_Data/keys/` → restart. | DataProtection key-ring rotated. |
| 9.2 | Trigger an email send → application log shows Warning: "SMTP password ciphertext present but DataProtection could not decrypt it. Re-save the SMTP settings...". Email send fails gracefully (returns SendResult.Fail). | Operator catches it at boot/log review instead of "test email silently fails". |
| 9.3 | Same scenario for SMS API key (`SmsSettingsService`) and any payment-gateway secret (`PaymentSettingsService.GetBkash/Ssl/Nagad WithSecretsAsync`). | Each service logs a Warning with re-save guidance. |

## 10. Permission service hot-path (Phase 3)

| # | Action | Expected |
|---|--------|----------|
| 10.1 | User has 3 roles → first request → 1 query for role-id resolution + 1 batched query for all role permissions = 2 queries total. | Previously: 1 + 3 = 4 queries (one per role). |
| 10.2 | Same user 100 requests within 15 min → 0 DB queries on permission check (full cache hit). | TTL = 15 min. |
| 10.3 | Admin assigns a new permission to a role via UI → next request that user makes for that role → cache invalidated → fresh fetch. | `InvalidateRoleCache(roleId)` after Assign/Revoke. |

## 11. Out of scope

These manual test cases assume the underlying phase-N test cases pass.
For per-phase happy-path coverage of CRUD, scheduling, theming, etc.,
see [phase-1-6-test-cases.md](phase-1-6-test-cases.md) through
[phase-16-17-test-cases.md](phase-16-17-test-cases.md).
