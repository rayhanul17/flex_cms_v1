# Phase 14 — API + Integrations + Engagement: Manual Test Cases

> **Automated coverage**: 13 unit (webhook signature symmetry, comment
> spam scorer, ApiTokenService.HashTokenString) + 25 integration
> (ApiTokenService 6 — issue/validate/revoke/expire/list; Subscriber +
> CustomField 11 — opt-in flow + meta serialize round-trip; Revisions
> + Comments 8 — version auto-increment + spam routing). Project total:
> 292 unit + 249 integration.
>
> **Scope delivered**: API tokens (Issue 73), webhooks (Issue 74),
> CORS settings on `SiteSettings` (Issue 75), captcha provider abstraction
> + Turnstile impl (Issue 76), CDN URL helper + asset versioning
> (Issues 77-78), content revisions (Issue 79), comments + spam filter
> (Issue 80), subscribers + double opt-in (Issue 82), custom fields
> (Issue 83).
>
> **Deferred**: Forms Builder drag-drop UI (Issue 81 — substantial UI
> design work), Newsletter compose UI + scheduled-send wiring (Issue 82
> partial — entity + service in place; UI + queue integration next),
> hCaptcha/reCAPTCHA concrete providers (interface + Turnstile in place;
> the other two follow the same shape), DiffPlex revision diff UI
> (Issue 79 partial — service + history in place; HTML diff view next).

## 1. API tokens (Issue 73)

| # | Action | Expected |
|---|--------|----------|
| 1.1 | `IApiTokenService.IssueAsync(userId, "iPhone App", "blog.post.read")` → returns `ApiTokenIssued{Token, PlaintextToken}`. Plaintext starts with `fcms_`, never re-derivable from DB. | Verified by `IssueAsync_returns_plaintext_only_once_and_persists_hash`. |
| 1.2 | `curl -H "Authorization: Bearer fcms_..." /api/something` → `FcmsApiTokenAuthenticationHandler` validates → `[Authorize]` controllers see a `ClaimsPrincipal` populated with the user's roles + per-scope claims (`fcms.api_scope`). | Bearer scheme registered alongside cookie scheme so both work. |
| 1.3 | Wrong / unknown / malformed token → `ValidateAsync` returns null → handler returns 401. | Verified by `ValidateAsync_rejects_unknown_or_malformed_tokens`. |
| 1.4 | Revoke → `IsRevoked=true` → next `ValidateAsync` returns null. | Verified. |
| 1.5 | Expired token (`ExpiresAt < now`) → rejected. | Verified by `ValidateAsync_rejects_expired_token`. |
| 1.6 | `LastUsedAt` bumped on every successful validation (best-effort — write failures don't fail the request). | Verified. |

## 2. Webhooks (Issue 74)

| # | Action | Expected |
|---|--------|----------|
| 2.1 | Configure `FcmsWebhookEndpoint{Url, Events="post.published,user.registered", Secret="topsecret"}`. Trigger `IWebhookDispatcher.FireAsync("post.published", payload)` → POST sent to URL with header `X-Fcms-Signature: sha256={hmac-hex}` + `X-Fcms-Event: post.published`. | Same signature scheme GitHub/Stripe use. |
| 2.2 | Receiver verifies HMAC: `HMACSHA256(secret, body) == Signature[7..]`. Wrong secret → mismatch. | Verified by `WebhookSignatureTests` symmetry. |
| 2.3 | Endpoint returns 5xx → `FcmsWebhookDelivery.AttemptCount=1`, `DeliveryStatus=Pending`. Next `RetryFailedAsync` invocation re-attempts. After `MaxAttempts=3` failures → `DeliveryStatus=Failed`. | `AttemptAsync` increments + sets terminal state at cap. |
| 2.4 | Endpoint subscribed to `*` → receives every event. | `SubscribedTo` wildcard branch. |
| 2.5 | Endpoint deleted while a delivery is Pending → next retry marks it Failed with "Endpoint not found or inactive." | `RetryFailedAsync` graceful path. |

## 3. CORS (Issue 75)

| # | Action | Expected |
|---|--------|----------|
| 3.1 | `SiteSettings.CorsEnabled=true`, `CorsAllowedOrigins="https://app.example.com"` → preflight OPTIONS from that origin → 204. | Future work: wire `app.UseCors()` from these settings (currently the settings exist + admin can save them; pipeline integration is the next pass). |
| 3.2 | Origin not in list → blocked. | Same. |

## 4. Captcha (Issue 76)

| # | Action | Expected |
|---|--------|----------|
| 4.1 | Configure Cloudflare Turnstile site/secret keys. Front-end widget renders. Submit valid token → `TurnstileCaptchaProvider.VerifyAsync` returns `Ok`. | Provider implemented. |
| 4.2 | Submit invalid token → `Fail("Captcha verification failed.")`. | Verified by `success: false` parse path. |
| 4.3 | Empty secret → `Fail("Captcha secret not configured.")`. | Guard rail. |
| 4.4 | Adaptive: `CaptchaSettings.AdaptiveLoginThreshold=3` → after 3 IP failures the next login form requires captcha (uses `LoginHistoryService.GetFailedCountSinceAsync`). | UI wiring is the next pass; the threshold is in `CaptchaSettings`. |

## 5. CDN + asset versioning (Issues 77-78)

| # | Action | Expected |
|---|--------|----------|
| 5.1 | `CdnSettings{Enabled=true, BaseUrl="https://cdn.example.com"}` → `ICdnUrlService.ResolveAsync("/uploads/x.jpg")` → `"https://cdn.example.com/uploads/x.jpg"`. | Verified at the service. |
| 5.2 | CDN disabled → returns the path unchanged. | Same. |
| 5.3 | Already-absolute URL → returned unchanged (no double-prefix). | `StartsWith("http")` guard. |
| 5.4 | `IAssetVersionService.Versioned("/css/site.css")` → `"/css/site.css?v=a1b2c3d4"`. Edit the file → next call computes a different hash. | Cache invalidates on `LastWriteTimeUtc` change. |
| 5.5 | Missing file → returns the path unchanged (no exception). | Defensive — page render keeps working. |

## 6. Content revisions (Issue 79)

| # | Action | Expected |
|---|--------|----------|
| 6.1 | Edit a page, save → `IContentRevisionService.SnapshotAsync(...)` writes `Version=1`. Save again → `Version=2`. | Auto-increment per (entityType, entityId). |
| 6.2 | Two pages each get their own version sequence — page A's v3 doesn't affect page B's v1. | Verified by `SnapshotAsync_versions_are_per_entity_independently`. |
| 6.3 | `GetForAsync` returns newest first. | Verified. |
| 6.4 | Diff viewer with DiffPlex → next pass. | Out of scope. |

## 7. Comments (Issue 80)

| # | Action | Expected |
|---|--------|----------|
| 7.1 | Submit clean comment → `Status=Pending`, in moderation queue. | Verified. |
| 7.2 | Submit comment with 6+ `https://...` links → auto-flagged Spam, never appears in moderation queue. | Verified by `Six_links_marks_spam`. |
| 7.3 | Submit comment containing keywords like "viagra", "lottery", "winner" → score ≥5 → Spam. | Verified. |
| 7.4 | Excessive caps lock (>70%) → score bumps but doesn't auto-spam alone. | Verified. |
| 7.5 | Admin → moderation queue → Approve → `Status=Approved` + `ModeratedByUserId` + `ModeratedAt` set. | Verified by `SetStatusAsync_records_moderator_metadata`. |
| 7.6 | Frontend: `GetApprovedAsync(entityType, entityId)` → only Approved rows, oldest first (chronological). | Verified. |
| 7.7 | Threaded reply: comment row with `ParentId=otherCommentId` → frontend renders nested. | Schema in place; UI render is per-theme. |

## 8. Subscribers + double opt-in (Issue 82)

| # | Action | Expected |
|---|--------|----------|
| 8.1 | Footer subscribe form → `SubscribeAsync(email)` → row inserted with `Status=PendingVerification`, fresh 32-char hex token. | Verified. |
| 8.2 | Email lower-cased on insert. | Verified by `SubscribeAsync_normalizes_email_lowercase`. |
| 8.3 | Click verify link `/newsletter/verify?token=...` → `VerifyAsync(token)` → `Status=Active`, `VerifiedAt` set. | Verified. |
| 8.4 | Click unsubscribe link `/newsletter/unsubscribe?token=...` (no login required) → `Status=Unsubscribed`. | Verified. |
| 8.5 | Re-subscribe after unsubscribe → row resets to PendingVerification with a NEW token (the old one is no longer valid). | Verified by `Resubscribe_after_unsubscribe_resets_to_pending_with_new_token`. |
| 8.6 | `GetActiveAsync()` → drives the recipient list for newsletter sends. Excludes Pending, Unsubscribed, Bounced. | Verified. |

## 9. Custom fields (Issue 83)

| # | Action | Expected |
|---|--------|----------|
| 9.1 | `SetAsync<int>("FcmsPost", postId, "ReadingTime", 5)` → `GetAsync<int>` returns 5. | Verified. |
| 9.2 | Same key set twice → row updated in place (no duplicate). | Verified by `SetAsync_overwrites_existing_value_for_same_key`. |
| 9.3 | Round-trip works for `string`, `int`, `bool`, `decimal`, `DateTime`, and arbitrary JSON-serializable objects (typed as `json`). | Verified by `SetAsync_handles_primitive_types`. |
| 9.4 | `RemoveAsync` deletes the row (hard delete — meta has no soft-delete value). | Verified. |
| 9.5 | Unique on (`EntityType`, `EntityId`, `Key`) — DB level. | Verified by composite index in DbContext. |

## 10. Database storage cross-check

- **EF**: `SELECT COUNT(*) FROM fcms_api_tokens WHERE is_revoked=false;` matches active token count.
- **Mongo indexes** added: `ux_api_tokens_hash`, `ix_webhook_deliveries_status_attempt`, `ix_content_revisions_entity_version`, `ix_comments_entity_status`, `ux_subscribers_email`, `ux_subscribers_token`, `ux_content_meta_entity_key`.
- **API token hashes**: `SELECT prefix, hash FROM fcms_api_tokens LIMIT 1;` — `hash` is 64-char SHA-256 hex; plaintext token NEVER appears.
- **Webhook signatures**: every outbound delivery row's payload + endpoint secret can be re-HMAC'd to match the delivered `X-Fcms-Signature`.

## 11. Out of scope (deferred)

- **Forms Builder drag-drop UI (Issue 81)** — substantial UI work; entity + service deferred until designed.
- **Newsletter compose UI + scheduled-send (Issue 82 partial)** — entity + opt-in flow done; admin compose page + send-via-IFcmsBackgroundQueue is next.
- **Comment notifications + reply-from-email (Issue 80 partial)** — `ICommentService.SubmitAsync` could enqueue an admin email via Phase 8 queue; not yet wired.
- **DiffPlex revision diff UI (Issue 79 partial)** — service stores snapshots; HTML diff view + restore button is the next pass.
- **hCaptcha + reCAPTCHA concrete providers** — interface + Turnstile shipped; the other two follow the same shape.
- **CORS pipeline wiring** — settings exist; `app.UseCors()` wiring against them is the next pass.
- **Adaptive captcha auto-show** — threshold is in settings; UI wiring uses Phase 13 `LoginHistoryService.GetFailedCountSinceAsync`.
