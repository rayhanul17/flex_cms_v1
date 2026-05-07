using FlexCms.Framework.Auth;
using FlexCms.Framework.Payments;
using FlexCms.Framework.Payments.Services;
using FlexCms.Host.Models.Admin;
using Microsoft.AspNetCore.Mvc;

namespace FlexCms.Host.Controllers.Admin;

/// <summary>
/// Admin UI for the three BD payment gateways (bKash / SSLCommerz / Nagad).
/// Each gateway has its own credential shape + charge config — they're saved
/// as one form post to avoid the "did I forget the second tab?" footgun.
///
/// <para>
/// Secret fields (bKash AppSecret + Password, SSLCommerz StorePassword,
/// Nagad MerchantPrivateKey) follow the SMTP "blank means keep" pattern:
/// the underlying settings service re-encrypts only when a non-empty value
/// is posted.
/// </para>
/// </summary>
[Route("admin/payments-settings")]
public class PaymentsSettingsController : BaseAdminController
{
    private readonly IPaymentSettingsService _settings;
    private readonly DispatchingPaymentGateway _dispatcher;
    private readonly IPaymentChargeCalculator _charges;

    public PaymentsSettingsController(
        IPaymentSettingsService settings,
        DispatchingPaymentGateway dispatcher,
        IPaymentChargeCalculator charges)
    {
        _settings = settings;
        _dispatcher = dispatcher;
        _charges = charges;
    }

    [HttpGet("")]
    [FcmsAuthorize(FcmsPermissions.PaymentsView)]
    public async Task<IActionResult> Index(CancellationToken ct)
        => View(await BuildVmAsync(ct));

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize(FcmsPermissions.PaymentsManage)]
    [FcmsLog("payments-settings.save", "PaymentsSettings")]
    public async Task<IActionResult> Index(PaymentsSettingsViewModel vm, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            // Re-prime the "Has..." flags so the form re-renders correctly.
            await PrimeHasFlagsAsync(vm, ct);
            return View(vm);
        }

        await _settings.SaveGeneralAsync(new PaymentSettings
        {
            Enabled = vm.Enabled,
            ActiveGateway = vm.ActiveGateway,
            Currency = string.IsNullOrWhiteSpace(vm.Currency) ? "BDT" : vm.Currency.Trim().ToUpperInvariant()
        }, ct);

        await _settings.SaveBkashAsync(new BkashSettings
        {
            Enabled = vm.Bkash.Enabled,
            TestMode = vm.Bkash.TestMode,
            AppKey = vm.Bkash.AppKey?.Trim() ?? "",
            Username = vm.Bkash.Username?.Trim() ?? "",
            MerchantNumber = vm.Bkash.MerchantNumber?.Trim() ?? "",
            EndpointOverride = vm.Bkash.EndpointOverride?.Trim() ?? "",
            Charge = vm.Bkash.Charge.ToConfig()
        }, vm.Bkash.AppSecret, vm.Bkash.Password, ct);

        await _settings.SaveSslcommerzAsync(new SslcommerzSettings
        {
            Enabled = vm.Sslcommerz.Enabled,
            TestMode = vm.Sslcommerz.TestMode,
            StoreId = vm.Sslcommerz.StoreId?.Trim() ?? "",
            EndpointOverride = vm.Sslcommerz.EndpointOverride?.Trim() ?? "",
            Charge = vm.Sslcommerz.Charge.ToConfig()
        }, vm.Sslcommerz.StorePassword, ct);

        await _settings.SaveNagadAsync(new NagadSettings
        {
            Enabled = vm.Nagad.Enabled,
            TestMode = vm.Nagad.TestMode,
            MerchantId = vm.Nagad.MerchantId?.Trim() ?? "",
            MerchantNumber = vm.Nagad.MerchantNumber?.Trim() ?? "",
            MerchantPublicKey = vm.Nagad.MerchantPublicKey ?? "",
            NagadPublicKey = vm.Nagad.NagadPublicKey ?? "",
            EndpointOverride = vm.Nagad.EndpointOverride?.Trim() ?? "",
            Charge = vm.Nagad.Charge.ToConfig()
        }, vm.Nagad.MerchantPrivateKey, ct);

        ShowSuccess("Payment settings saved.");
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Test the active gateway end-to-end with a 1.00 BDT initiate. Returns
    /// the upstream redirect URL on success — the admin can paste it into a
    /// browser to walk the full flow. Doesn't actually charge anything since
    /// no completion is performed.
    /// </summary>
    [HttpPost("test-initiate")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize(FcmsPermissions.PaymentsManage)]
    public async Task<IActionResult> TestInitiate([FromForm] decimal amount, CancellationToken ct)
    {
        if (amount <= 0) return FcmsFail("Amount must be positive.");
        var callbackUrl = Url.Action("Index", "Home", null, Request.Scheme) ?? "/";
        var r = await _dispatcher.InitiateAsync(new PaymentInitiateRequest(
            amount, "BDT", $"TEST-{DateTime.UtcNow:yyyyMMddHHmmss}", callbackUrl), ct);
        return r.Success
            ? FcmsOk($"OK — redirect: {r.RedirectUrl}", new { r.RedirectUrl, r.TransactionId, r.Charge })
            : FcmsFail($"Initiate failed: {r.Error}");
    }

    /// <summary>
    /// Pure preview — shows the customer/merchant breakdown for an arbitrary
    /// amount under a chosen gateway's current charge config. Doesn't hit
    /// the upstream API, so it works even with no creds configured.
    /// </summary>
    [HttpPost("preview-charge")]
    [ValidateAntiForgeryToken]
    [FcmsAuthorize(FcmsPermissions.PaymentsView)]
    public async Task<IActionResult> PreviewCharge([FromForm] string gateway, [FromForm] decimal amount, CancellationToken ct)
    {
        if (amount <= 0) return FcmsFail("Amount must be positive.");
        PaymentChargeConfig cfg = gateway switch
        {
            PaymentGateways.Bkash => (await _settings.GetBkashAsync(ct)).Charge,
            PaymentGateways.Sslcommerz => (await _settings.GetSslcommerzAsync(ct)).Charge,
            PaymentGateways.Nagad => (await _settings.GetNagadAsync(ct)).Charge,
            _ => new PaymentChargeConfig()
        };
        var c = _charges.Calculate(amount, cfg);
        return FcmsOk("OK", c);
    }

    private async Task<PaymentsSettingsViewModel> BuildVmAsync(CancellationToken ct)
    {
        var general = await _settings.GetGeneralAsync(ct);
        var bkash = await _settings.GetBkashAsync(ct);
        var ssl = await _settings.GetSslcommerzAsync(ct);
        var nagad = await _settings.GetNagadAsync(ct);

        return new PaymentsSettingsViewModel
        {
            Enabled = general.Enabled,
            ActiveGateway = string.IsNullOrWhiteSpace(general.ActiveGateway) ? PaymentGateways.Bkash : general.ActiveGateway,
            Currency = string.IsNullOrWhiteSpace(general.Currency) ? "BDT" : general.Currency,

            Bkash = new PaymentsSettingsViewModel.BkashVm
            {
                Enabled = bkash.Enabled,
                TestMode = bkash.TestMode,
                AppKey = bkash.AppKey,
                Username = bkash.Username,
                MerchantNumber = bkash.MerchantNumber,
                EndpointOverride = bkash.EndpointOverride,
                HasAppSecret = !string.IsNullOrEmpty(bkash.AppSecretEncrypted),
                HasPassword = !string.IsNullOrEmpty(bkash.PasswordEncrypted),
                Charge = PaymentsSettingsViewModel.ChargeVm.From(bkash.Charge)
            },
            Sslcommerz = new PaymentsSettingsViewModel.SslcommerzVm
            {
                Enabled = ssl.Enabled,
                TestMode = ssl.TestMode,
                StoreId = ssl.StoreId,
                EndpointOverride = ssl.EndpointOverride,
                HasStorePassword = !string.IsNullOrEmpty(ssl.StorePasswordEncrypted),
                Charge = PaymentsSettingsViewModel.ChargeVm.From(ssl.Charge)
            },
            Nagad = new PaymentsSettingsViewModel.NagadVm
            {
                Enabled = nagad.Enabled,
                TestMode = nagad.TestMode,
                MerchantId = nagad.MerchantId,
                MerchantNumber = nagad.MerchantNumber,
                MerchantPublicKey = nagad.MerchantPublicKey,
                NagadPublicKey = nagad.NagadPublicKey,
                EndpointOverride = nagad.EndpointOverride,
                HasMerchantPrivateKey = !string.IsNullOrEmpty(nagad.MerchantPrivateKeyEncrypted),
                Charge = PaymentsSettingsViewModel.ChargeVm.From(nagad.Charge)
            }
        };
    }

    private async Task PrimeHasFlagsAsync(PaymentsSettingsViewModel vm, CancellationToken ct)
    {
        // On a validation re-render the user's typed values must survive (so
        // we can't refetch the whole VM), but the "Has..." flags need to
        // reflect persisted state so the placeholders render correctly.
        var bkash = await _settings.GetBkashAsync(ct);
        var ssl = await _settings.GetSslcommerzAsync(ct);
        var nagad = await _settings.GetNagadAsync(ct);
        vm.Bkash.HasAppSecret = !string.IsNullOrEmpty(bkash.AppSecretEncrypted);
        vm.Bkash.HasPassword = !string.IsNullOrEmpty(bkash.PasswordEncrypted);
        vm.Sslcommerz.HasStorePassword = !string.IsNullOrEmpty(ssl.StorePasswordEncrypted);
        vm.Nagad.HasMerchantPrivateKey = !string.IsNullOrEmpty(nagad.MerchantPrivateKeyEncrypted);
    }
}
