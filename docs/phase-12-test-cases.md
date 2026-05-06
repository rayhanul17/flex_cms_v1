# Phase 12 — Payment + PDF + Excel + Export: Manual Test Cases

> **Automated coverage**: 13 unit (PaymentSettingsService encryption,
> DispatchingPaymentGateway routing, PDF + Excel byte-level magic-byte
> assertions) + 6 integration (4 ExportProcessorService EF in-memory:
> happy path, missing handler, throwing handler, idempotent done-row
> skip; 2 Mongo via Testcontainers: pending-export persist + terminal-
> state round-trip). Project total: 264 unit + 216 integration.

> **Note on payment gateways**: bKash / SSLCommerz / Nagad concrete
> impls ship with the **structurally correct request/response shapes**
> wired up (sandbox + production endpoints, payload mapping, response
> parsing). Live merchant credentials and signature/RSA work are
> deferred per gateway:
> - **SSLCommerz** — full create-session + verify is implemented; works
>   end-to-end against a sandbox account once `store_id`/`store_passwd`
>   are configured.
> - **bKash** — Tokenized Checkout endpoints wired; webhook signature
>   verification deferred (rejects unsigned IPN calls until
>   implemented).
> - **Nagad** — initialize/verify endpoints wired; RSA encryption +
>   SHA-256 signature build deferred (placeholder fields will be
>   replaced when a real merchant keypair is available).

## 1. Payment settings (admin)

| # | Action | Expected |
|---|--------|----------|
| 1.1 | `/admin/messaging-settings` (or future Payments tab) lists three gateway choices: `bkash`, `sslcommerz`, `nagad`. | From `PaymentGateways.All`. |
| 1.2 | Enter API key + merchant password → save → reload → password placeholder shows "set", plaintext never round-tripped. | DataProtection encrypts; `PaymentSettingsService` mirrors the SMTP/SMS pattern. |
| 1.3 | Save with empty new secrets → existing ciphertext preserved. | Verified by `SaveAsync_with_null_secrets_preserves_existing_ciphertext`. |
| 1.4 | Toggle TestMode → next Initiate hits sandbox URL; toggle off → production URL. | Per gateway's `ResolveBaseUrl`. |

## 2. Payment flow (per gateway)

| # | Action | Expected |
|---|--------|----------|
| 2.1 | Module calls `DispatchingPaymentGateway.InitiateAsync(req)` → returns `PaymentInitiateResult` with redirect URL + transaction id; user-facing controller redirects there. | Verified by `Initiate_dispatches_to_active_gateway`. |
| 2.2 | After completion, gateway redirects user back to `CallbackUrl`; controller calls `VerifyAsync(transactionId)` → returns `PaymentResult.Ok` with amount + status. | Per-gateway impl. |
| 2.3 | Disabled in settings → `Initiate` / `Verify` returns `Fail("Payments not enabled.")`. | Verified. |
| 2.4 | Active gateway typo (`klingon`) → returns `Fail("Unknown gateway 'klingon'.")`. | Verified. |

## 3. Webhook (`/payment/webhook/{gatewayId}`)

| # | Action | Expected |
|---|--------|----------|
| 3.1 | Gateway POSTs IPN (form or JSON body) → controller normalizes payload → `DispatchingPaymentGateway.HandleWebhookAsync(gatewayId, payload)`. | Anonymous endpoint (gateways can't auth). |
| 3.2 | SSLCommerz IPN with `val_id` → controller round-trips through `VerifyAsync` to confirm authenticity (server-to-server). | Implemented. |
| 3.3 | bKash / Nagad IPN → currently rejected with "Webhook verification not yet implemented." — update those once live merchant docs are obtained. | Stub note in gateway docs. |
| 3.4 | Webhook with missing required field (e.g. SSLCommerz no `val_id`) → 400 with `{"ok": false, "error": "Missing val_id."}`. | Verified by gateway impl. |
| 3.5 | Verify the dispatcher uses the URL's `gatewayId`, NOT `SiteSettings.ActiveGateway` — payments could be in transition (e.g. switching from bKash to SSLCommerz) when an in-flight webhook for the old gateway arrives. | Verified by `HandleWebhook_uses_explicit_gateway_id_not_active`. |

## 4. PDF (`IFcmsPdfService`)

| # | Action | Expected |
|---|--------|----------|
| 4.1 | Call `RenderTextAsync("Invoice", lines)` from a controller, return as `File(bytes, "application/pdf", "invoice.pdf")` → browser downloads valid PDF. | Magic bytes `%PDF-` verified by automated tests. |
| 4.2 | `RenderTableAsync("Q4 Sales", headers, rows)` → single-page table PDF, equal-width columns, bold header. | Single-page in v1 — for paginated output, modules ship their own impl. |
| 4.3 | Open in Adobe Reader / browser PDF viewer → renders cleanly with Arial 11pt body, 16pt bold title. | Manual visual check. |
| 4.4 | 200 lines of body text → only fits on first page; remainder dropped (v1 limit). | Documented in PdfSharpPdfService XML doc. |

## 5. Excel (`IFcmsExcelService`)

| # | Action | Expected |
|---|--------|----------|
| 5.1 | `RenderTableAsync("Sheet1", headers, rows)` → returns `.xlsx` bytes; opens in Excel/LibreOffice. | Magic bytes `PK\x03\x04` (ZIP) verified by automated tests. |
| 5.2 | Header row is bold + light-grey fill. | Manual visual check. |
| 5.3 | Numeric cells (`int`, `decimal`, `double`) render as right-aligned numbers; bool as TRUE/FALSE; DateTime in default cell format. | Verified by `ToXLValue` mapping. |
| 5.4 | Sheet name with `>31 chars` or `:\/?*[]` chars → silently truncated/sanitized. | Verified by `Excel_truncates_long_sheet_names`. |
| 5.5 | Empty `rows` collection → file still valid, just header-only. | Verified by `Excel_handles_empty_rows_without_throwing`. |

## 6. Async export pipeline

| # | Action | Expected |
|---|--------|----------|
| 6.1 | Module ships `services.AddSingleton<IFcmsExportHandler, StudentResultExportHandler>();` → handler registered. Admin chooses "Student results" + parameters → row inserted into `fcms_pending_exports` with `Status=Pending`. | One row per request. |
| 6.2 | Within 30s the `ExportProcessorService` poll picks it up → flips `Status=Running`, calls `handler.RenderAsync(format, parametersJson)` → bytes returned → saved to `App_Data/storage/exports/{yyyy}/{MM}/{guid}.{ext}` → URL written back → `Status=Done`. | Verified by `ProcessOnceAsync_marks_pending_done_and_persists_bytes`. |
| 6.3 | On Done → `IFcmsNotificationService.NotifyUserAsync(requesterId, "Export ready: ...", ..., url, "bi bi-download")` → bell icon badge bumps for the requester. | Wired in processor. |
| 6.4 | Handler with that id not registered → `Status=Failed`, `FailureReason="No handler registered for '...'"`. | Verified. |
| 6.5 | Handler throws mid-render → `Status=Failed`, `FailureReason=ex.Message`. | Verified by `ProcessOnceAsync_marks_failed_when_handler_throws`. |
| 6.6 | Restart-safety: kill app while job is `Running` → on restart, the row stays Running (operator manually re-queues if needed). | Future enhancement: auto-retry stale-Running rows after a heartbeat timeout. |
| 6.7 | Done rows are not re-processed on subsequent polls. | Verified by `ProcessOnceAsync_already_done_rows_are_ignored`. |

## 7. Database storage cross-check

- **EF**: `SELECT export_status, COUNT(*) FROM fcms_pending_exports GROUP BY export_status;`
- **Mongo**: `db.fcms_pending_exports.find({exportStatus: "Pending"})` uses index `ix_pending_exports_status_created`.
- **Encrypted secrets**: `SELECT api_key_encrypted, merchant_password_encrypted FROM fcms_settings WHERE key='payments:default';` — both columns are CfDJ8...-prefixed ciphertext, never plaintext.

## 8. Permissions

| # | Action | Expected |
|---|--------|----------|
| 8.1 | User with `payments.view` can see settings; `payments.manage` required to save. | Seeded by SeedService. |
| 8.2 | `exports.request` — required to enqueue an export. `exports.view` — see the global queue (admin-only). | Seeded. |

## 9. Out of scope (future work)

- **bKash / Nagad webhook signature verification** — needs live merchant docs + sandbox keys. Stubs reject all webhooks until then.
- **Multi-page PDF + paginated tables** — module-supplied `IFcmsPdfService` can override.
- **Stale-Running auto-recovery for crashed export jobs** — heartbeat-based reaper, future Phase 13/15.
- **Export download authentication** — currently the URL is whatever `IFcmsFileStorage.SaveAsync` returns; signed/expiring URLs are storage-impl dependent.
- **Refunds + recurring payments** — gateway-specific, future module.
- **Currency conversion / multi-currency** — out of scope; settings hardcode to BDT.
