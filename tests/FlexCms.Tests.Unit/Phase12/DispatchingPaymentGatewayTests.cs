using FlexCms.Framework.Payments;
using FlexCms.Framework.Payments.Services;
using Microsoft.Extensions.Caching.Memory;
using NSubstitute;
using Xunit;

namespace FlexCms.Tests.Unit.Phase12;

public class DispatchingPaymentGatewayTests
{
    private static IPaymentSettingsService Settings(PaymentSettings s)
    {
        var m = Substitute.For<IPaymentSettingsService>();
        m.GetGeneralAsync(Arg.Any<CancellationToken>()).Returns(s);
        return m;
    }

    private static IMemoryCache NewCache() => new MemoryCache(new MemoryCacheOptions());

    private static DispatchingPaymentGateway Build(IPaymentSettingsService settings, IEnumerable<IFcmsPaymentGateway> gateways)
        => new(settings, gateways, NewCache());

    private sealed class FakeGateway : IFcmsPaymentGateway
    {
        public string GatewayId { get; }
        public int InitiateCallCount { get; private set; }
        public bool InitiateCalled => InitiateCallCount > 0;
        public bool VerifyCalled { get; private set; }
        public bool WebhookCalled { get; private set; }

        // Lets a test toggle the next return — useful for cache-negative-result tests.
        public Func<PaymentInitiateRequest, PaymentInitiateResult> InitiateImpl { get; set; }
            = _ => PaymentInitiateResult.Ok("https://x", "tx1");

        public FakeGateway(string id) { GatewayId = id; }

        public Task<PaymentInitiateResult> InitiateAsync(PaymentInitiateRequest r, CancellationToken ct = default)
        { InitiateCallCount++; return Task.FromResult(InitiateImpl(r)); }

        public Task<PaymentResult> VerifyAsync(string tx, CancellationToken ct = default)
        { VerifyCalled = true; return Task.FromResult(PaymentResult.Ok(tx, 100m, "Completed")); }

        public Task<PaymentResult> HandleWebhookAsync(IDictionary<string, string> p, CancellationToken ct = default)
        { WebhookCalled = true; return Task.FromResult(PaymentResult.Ok("tx1", 100m, "Completed")); }
    }

    [Fact]
    public async Task Initiate_returns_fail_when_disabled()
    {
        var d = Build(Settings(new PaymentSettings { Enabled = false }), [new FakeGateway(PaymentGateways.Bkash)]);
        var r = await d.InitiateAsync(new PaymentInitiateRequest(100, "BDT", "ord1", "https://cb"));
        Assert.False(r.Success);
        Assert.Contains("not enabled", r.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Initiate_returns_fail_when_gateway_unknown()
    {
        var d = Build(
            Settings(new PaymentSettings { Enabled = true, ActiveGateway = "klingon" }),
            [new FakeGateway(PaymentGateways.Bkash)]);
        var r = await d.InitiateAsync(new PaymentInitiateRequest(100, "BDT", "ord1", "https://cb"));
        Assert.False(r.Success);
        Assert.Contains("Unknown gateway", r.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Initiate_dispatches_to_active_gateway()
    {
        var bkash = new FakeGateway(PaymentGateways.Bkash);
        var ssl = new FakeGateway(PaymentGateways.Sslcommerz);
        var d = Build(
            Settings(new PaymentSettings { Enabled = true, ActiveGateway = PaymentGateways.Sslcommerz }),
            [bkash, ssl]);

        await d.InitiateAsync(new PaymentInitiateRequest(100, "BDT", "ord1", "https://cb"));

        Assert.False(bkash.InitiateCalled);
        Assert.True(ssl.InitiateCalled);
    }

    [Fact]
    public async Task Verify_uses_active_gateway()
    {
        var bkash = new FakeGateway(PaymentGateways.Bkash);
        var d = Build(
            Settings(new PaymentSettings { Enabled = true, ActiveGateway = PaymentGateways.Bkash }),
            [bkash]);

        var r = await d.VerifyAsync("tx1");
        Assert.True(r.Success);
        Assert.True(bkash.VerifyCalled);
    }

    [Fact]
    public async Task HandleWebhook_uses_explicit_gateway_id_not_active()
    {
        // Active = bKash but webhook arrives for SSLCommerz → SSLCommerz handles it.
        // Webhook is also independent of the global Enabled toggle (gateways may
        // need to ack disable-but-still-arriving notifications).
        var bkash = new FakeGateway(PaymentGateways.Bkash);
        var ssl = new FakeGateway(PaymentGateways.Sslcommerz);
        var d = Build(
            Settings(new PaymentSettings { Enabled = true, ActiveGateway = PaymentGateways.Bkash }),
            [bkash, ssl]);

        await d.HandleWebhookAsync(PaymentGateways.Sslcommerz, new Dictionary<string, string>());

        Assert.False(bkash.WebhookCalled);
        Assert.True(ssl.WebhookCalled);
    }

    // ─── Idempotency ────────────────────────────────────────────────────────

    [Fact]
    public async Task Initiate_with_explicit_idempotency_key_returns_cached_result_on_replay()
    {
        // The same idempotency key → gateway is called exactly once; second
        // call returns the cached PaymentInitiateResult verbatim. This is the
        // primary defense against double-charging from a browser retry or
        // network blip.
        var gw = new FakeGateway(PaymentGateways.Bkash);
        var d = Build(
            Settings(new PaymentSettings { Enabled = true, ActiveGateway = PaymentGateways.Bkash }),
            [gw]);

        var req = new PaymentInitiateRequest(100, "BDT", "ord1", "https://cb", IdempotencyKey: "abc-123");
        var first = await d.InitiateAsync(req);
        var second = await d.InitiateAsync(req);

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.Equal(1, gw.InitiateCallCount);
        Assert.Equal(first.RedirectUrl, second.RedirectUrl);
        Assert.Equal(first.TransactionId, second.TransactionId);
    }

    [Fact]
    public async Task Initiate_without_explicit_key_auto_derives_one_from_order_ref_and_amount()
    {
        // Defense-in-depth: callers that haven't yet adopted the idempotency
        // pattern still get duplicate-protection within an order, because the
        // dispatcher derives a key from gateway:order:amount when none is
        // supplied. Same order ref + amount = same derived key = cached.
        var gw = new FakeGateway(PaymentGateways.Bkash);
        var d = Build(
            Settings(new PaymentSettings { Enabled = true, ActiveGateway = PaymentGateways.Bkash }),
            [gw]);

        var req = new PaymentInitiateRequest(100, "BDT", "ord1", "https://cb");

        await d.InitiateAsync(req);
        await d.InitiateAsync(req);

        Assert.Equal(1, gw.InitiateCallCount);
    }

    [Fact]
    public async Task Initiate_with_different_idempotency_keys_calls_gateway_twice()
    {
        // Different keys = different attempts = both go through.
        var gw = new FakeGateway(PaymentGateways.Bkash);
        var d = Build(
            Settings(new PaymentSettings { Enabled = true, ActiveGateway = PaymentGateways.Bkash }),
            [gw]);

        await d.InitiateAsync(new PaymentInitiateRequest(100, "BDT", "ord1", "https://cb", IdempotencyKey: "key-a"));
        await d.InitiateAsync(new PaymentInitiateRequest(100, "BDT", "ord1", "https://cb", IdempotencyKey: "key-b"));

        Assert.Equal(2, gw.InitiateCallCount);
    }

    [Fact]
    public async Task Initiate_failed_call_is_NOT_cached_so_customer_can_retry()
    {
        // A gateway failure shouldn't lock the customer into the failure for
        // 10 minutes — they need to be able to retry. Only successful results
        // are cached for replay.
        var gw = new FakeGateway(PaymentGateways.Bkash)
        {
            InitiateImpl = _ => PaymentInitiateResult.Fail("gateway down")
        };
        var d = Build(
            Settings(new PaymentSettings { Enabled = true, ActiveGateway = PaymentGateways.Bkash }),
            [gw]);

        var req = new PaymentInitiateRequest(100, "BDT", "ord1", "https://cb", IdempotencyKey: "key-a");
        await d.InitiateAsync(req);
        await d.InitiateAsync(req);

        Assert.Equal(2, gw.InitiateCallCount);
    }
}
