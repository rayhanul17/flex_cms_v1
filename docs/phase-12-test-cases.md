# Phase 12 — Payment + PDF + Excel + Export: Manual Test Cases

> **Automated coverage**: 23 Phase12 unit tests — 13 PaymentChargeCalculator
> (Forward / Backward + ApplyChargeOnExtra toggle + NetCoreCMS-matching
> rounding + edge cases), 7 PaymentSettingsService (per-gateway encryption
> round-trip + cross-gateway purpose isolation), 5 DispatchingPaymentGateway
> (active-gateway routing + webhook id override), plus PDF + Excel
> magic-byte assertions. 6 integration (4 ExportProcessorService EF
> in-memory: happy path, missing handler, throwing handler, idempotent
> done-row skip; 2 Mongo via Testcontainers: pending-export persist +
> terminal-state round-trip). **Project total: 329 unit + 216 integration.**

> **Note on payment gateways**: bKash / SSLCommerz / Nagad concrete
> impls ship with the **structurally correct request/response shapes**
> wired up (sandbox + production endpoints, payload mapping, response
> parsing). Live merchant credentials and signature/RSA work are
> deferred per gateway:
> - **SSLCommerz** — full create-session + verify is implemented; works
>   end-to-end against a sandbox account once `store_id`/`store_passwd`
>   are configured.
> - **bKash** — Tokenized Checkout endpoints wired (X-APP-Key +
>   X-APP-Secret headers); webhook signature verification deferred
>   (rejects unsigned IPN calls until implemented).
> - **Nagad** — initialize/verify endpoints wired; RSA encryption +
>   SHA-256 signature build deferred (placeholder fields will be
>   replaced when a real merchant keypair is available).

> **Charge math is NetCoreCMS-compatible** — see
> [`PaymentChargeCalculator`](../src/FlexCms.Framework/Payments/PaymentChargeCalculator.cs)
> for the exact formulas. Verified against NetCoreCMS's `BkashHelper`
> for Forward (straight %) and Backward (back-calculated) modes; rounding
> mirrors `FormatTwoDecimalPointAmount` ("ceiling-on-third-digit, banker's
> otherwise"). The amount actually sent to the upstream gateway is
> `CustomerPays` (= order + fee + VAT + extra), not the raw order.

## 1. Payment settings (admin)

| # | Action | Expected |
|---|--------|----------|
| 1.1 | `/admin/payments-settings` opens with 4 sections: General + bKash tab + SSLCommerz tab + Nagad tab. | `PaymentsSettingsController.Index`. |
| 1.2 | General tab: enable toggle + active-gateway dropdown (`PaymentGateways.All`) + currency (defaults BDT). | From `PaymentSettings`. |
| 1.3 | Each gateway tab has its own credential shape: bKash (AppKey + AppSecret + Username + Password + MerchantNumber), SSLCommerz (StoreId + StorePassword), Nagad (MerchantId + MerchantNumber + Merchant Public/Private RSA PEM + Nagad Public PEM). | Per-gateway DTOs in `PaymentSettings.cs`. |
| 1.4 | Each gateway tab has a Charge config card: Bearer (Forward/Backward) + ChargePercent + FixedCharge + VatPercent + ExtraCharge + ApplyChargeOnExtra toggle. | `PaymentChargeConfig`. |
| 1.5 | Enter all secrets → save → reload → secret placeholders show "•••••••• (set)", plaintext never round-tripped. | DataProtection encrypts under per-gateway purposes (`FlexCms.Payments.Bkash` / `.Sslcommerz` / `.Nagad`). |
| 1.6 | Save with empty new secrets → existing ciphertext preserved. | Verified by `SaveBkashAsync_with_null_secrets_preserves_existing_ciphertext` (one per gateway). |
| 1.7 | Toggle TestMode (per gateway) → next Initiate hits sandbox URL; toggle off → production URL. | Per gateway's `ResolveBaseUrl(cfg)`. |
| 1.8 | Manually paste a bKash ciphertext into the SSLCommerz settings field → reload → SSLCommerz secret reads as empty. | Cross-gateway purpose isolation — verified by `Per_gateway_purposes_are_isolated`. |

## 1a. Charge calculator preview (admin)

| # | Action | Expected |
|---|--------|----------|
| 1a.1 | Set bKash to Forward 1.85% → Test bench → "Preview charge" with amount 1000 → JSON shows `GatewayCharge=18.50, CustomerPays=1018.50, MerchantReceives=1000`. | `Forward_straight_percent_on_order_amount`. |
| 1a.2 | Set bKash to Backward 1.85% → Preview 1000 → `GatewayCharge=18.85, CustomerPays=1018.85, MerchantReceives=1000`. | Back-calc: `1000/(1-0.0185)-1000=18.8487...→18.85` (ceiling-on-third). |
| 1a.3 | Set SSLCommerz to Backward 5% + ExtraCharge 10 + ApplyChargeOnExtra OFF → Preview 200 → `GatewayCharge=10.53, ExtraCharge=10, CustomerPays=220.53, MerchantReceives=210`. | Pass-through: extra ALWAYS adds to both customer + merchant. |
| 1a.4 | Same as 1a.3 but ApplyChargeOnExtra ON → fee calc on (200+10)=210 → `GatewayCharge=11.06`-ish (Backward 5% on 210). | Toggle controls only the fee base, not the pass-through. |
| 1a.5 | Forward + ExtraCharge + VAT 15% → VAT computed on `GatewayCharge` only, NOT on order or extra. | Matches NetCoreCMS — VAT is a tax on the gateway's commission. |
| 1a.6 | Backward 100% (pathological) → `GatewayCharge=0` (no div-by-zero), customer pays raw order. | Verified by `Backward_pathological_100_percent_rate_yields_zero_charge_no_div_by_zero`. |
| 1a.7 | "Test initiate" with amount 1.00 BDT against active gateway → returns redirect URL + transactionId; charge breakdown attached to the result. | Smoke test only — no completion call. |

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
- **Encrypted secrets** (one row per gateway, each with its own ciphertext fields):
  - `SELECT value FROM fcms_settings WHERE key='payments:bkash';` — `AppSecretEncrypted` + `PasswordEncrypted` are `CfDJ8...`-prefixed
  - `SELECT value FROM fcms_settings WHERE key='payments:sslcommerz';` — `StorePasswordEncrypted` is `CfDJ8...`-prefixed
  - `SELECT value FROM fcms_settings WHERE key='payments:nagad';` — `MerchantPrivateKeyEncrypted` is `CfDJ8...`-prefixed; `MerchantPublicKey` + `NagadPublicKey` stored plaintext (not secret)

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
