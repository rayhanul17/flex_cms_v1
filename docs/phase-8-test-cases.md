# Phase 8 — Email + SMS + Background Jobs: Manual Test Cases

> **Automated coverage**: 20 unit tests (queue, dispatcher, SMTP guard rails,
> settings encryption) + 9 integration tests (MessageProcessor on EF
> in-memory + Mongo pending-message persistence). All passing. Project total:
> 223 unit + 190 integration.

## 1. SMTP settings (admin)

| # | Action | Expected |
|---|--------|----------|
| 1.1 | Open `/admin/messaging-settings`. SMTP card visible with all fields, password placeholder shows "•••••••• (set — leave blank to keep)" if previously saved else "Enter password". | Password field is `type=password`, autocomplete off. |
| 1.2 | Save with valid SMTP host/port/username/password + From address → success toast. Reload page → password placeholder now shows "•••••••• (set...)". | The plaintext password is never echoed back. |
| 1.3 | Edit any non-secret field, save with empty password → ciphertext preserved, settings updated. | DB row's `passwordEncrypted` byte-identical to before. |
| 1.4 | Click [Send test email] with a valid recipient → "Test email sent." status. Inbox receives the email. | If SMTP fails, error reason shown. |
| 1.5 | Disable SMTP → [Send test email] returns "SMTP failed: SMTP not enabled.". | Guard rail. |

## 2. SMS settings (admin)

| # | Action | Expected |
|---|--------|----------|
| 2.1 | Choose Gateway = `alpha` / `mram` / `onnorokom` from dropdown. | All three gateways listed via `SmsGateways.All`. |
| 2.2 | Save with API key → ciphertext stored. Reload → placeholder shows "set". | API key never round-tripped to client. |
| 2.3 | [Send test SMS] to a real number → success/failure result with raw response surfaced. | Per-gateway success contract: Alpha "error":0, MRAM numeric body, Onnorokom responseCode "1900". |
| 2.4 | Switch gateway then re-test without rotating API key → uses new gateway with prior key. | Verified by `Sms_SaveWithEmptyApiKey_preserves_existing_ciphertext` unit test. |
| 2.5 | Set EndpointOverride → next send hits override URL instead of default. | Useful for local mock/relay testing. |

## 3. Forgot-password email (end-to-end)

| # | Action | Expected |
|---|--------|----------|
| 3.1 | Go to `/auth/forgot-password`, submit a known-good email address. Page shows confirmation. | Always shows confirmation regardless of whether email exists (avoids enumeration). |
| 3.2 | Within seconds, the inbox for that email receives "Reset your password" with a link to `/auth/reset-password?token=...&userId=...`. | Sent through `IFcmsBackgroundQueue` — request is fast even if SMTP is slow. |
| 3.3 | If SMTP is misconfigured, the request still returns the confirmation page; the queue worker logs the failure. | Loss-tolerant by design. |

## 4. Broadcast (admin)

| # | Action | Expected |
|---|--------|----------|
| 4.1 | `/admin/broadcast` → form with Channel (Email/SMS), Send-to (All / By role / Selected), Subject, Body, IsHtml toggle. | Only `MessagingView` perm required to view; `MessagingBroadcast` to send. |
| 4.2 | Send Email → All Users → success toast with broadcast ID + count. | One `FcmsPendingMessage` row per recipient. |
| 4.3 | Send SMS → By role → choose role → toast shows count. | Recipient list comes from `UserManager.GetUsersInRoleAsync`. |
| 4.4 | If 0 users match the target → warning "No recipients matched". | No rows inserted. |
| 4.5 | `/admin/broadcast/history` → most-recent 100 pending-message rows with channel, recipient, status badge (Pending/Sent/Failed), retry count, last error. | Updates on next page load as the worker drains. |

## 5. Pending message lifecycle (visible via Broadcast → History)

| # | Action | Expected |
|---|--------|----------|
| 5.1 | Send broadcast → rows appear with Pending badge. Within 30s the worker runs → rows flip to Sent. | `MessageProcessorService.PollInterval` defaults to 30s. |
| 5.2 | Misconfigure SMTP → broadcast email → status stays Pending with last error populated; retry count increments each poll. After 3 attempts, badge turns Failed. | Verified by `ProcessOnce_marks_failed_after_max_retries_exhausted` test. |
| 5.3 | Fix SMTP → next poll picks up Failed-with-retries-left rows? Once `RetryCount >= MaxRetries` they remain Failed. | Manual recovery: admin can re-broadcast. |
| 5.4 | Restart the app while messages are Pending → on startup the worker resumes the queue without losing rows. | Restart-safe by design (rows persisted in DB). |

## 6. Database storage cross-check

- **EF (MySQL/Postgres/MsSQL)**: `SELECT delivery_status, retry_count, COUNT(*) FROM fcms_pending_messages GROUP BY delivery_status, retry_count;`
- **Mongo**: `db.fcms_pending_messages.find({deliveryStatus: "Pending"})` — index `ix_pending_messages_status_retry` is used (run `.explain()`).
- **Mongo indexes** present: `ix_pending_messages_status_retry`, `ix_pending_messages_broadcast_id` — confirmed by [MongoIndexService](../src/FlexCms.Framework/Db/MongoDb/MongoIndexService.cs).

## 7. In-memory background queue (instant fire-and-forget)

Already covered by [BackgroundQueueTests](../tests/FlexCms.Tests.Unit/Phase8/BackgroundQueueTests.cs):

- TryEnqueue rejects when capacity (default 1000) reached.
- Each item runs in a fresh DI scope (proven via singleton scope-marker).
- A throwing item does not poison the pump — subsequent items still run.

## 8. Edge cases

| # | Action | Expected |
|---|--------|----------|
| 8.1 | Save SMTP with no password ever set → SmtpHasPassword=false → placeholder reads "Enter password". | UI signals state without leaking secrets. |
| 8.2 | Rotate the DataProtection key (delete `App_Data/keys/`) → existing ciphertext can't decrypt → settings service returns empty plaintext (treated as "not configured"). | App keeps running; admin must re-enter the secret. |
| 8.3 | Onnorokom API returns malformed JSON → gateway returns Fail with parse-exception message. | No process crash. |
| 8.4 | MRAM returns alphanumeric error string instead of numeric ID → Fail with the gateway's error text in `LastError`. | Easy admin debugging via History page. |

## 9. Out of scope (future phases)

- **Two-way SMS / DLR** (delivery-receipt webhooks) — Phase 14 (API + Integrations).
- **Email templates with variable substitution** (`{user.firstName}` etc.) — Phase 9 / 11.
- **Per-recipient personalization** in broadcasts — Phase 9.
- **OTP storage in IMemoryCache** (`FcmsOtpEntry`) — already in Phase 2 auth flow; no Phase 8 work needed.
- **Webhook receiver for inbound mail / sms** — Phase 14.
