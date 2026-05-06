using FlexCms.Framework.Payments;
using FlexCms.Framework.Payments.Services;
using NSubstitute;
using Xunit;

namespace FlexCms.Tests.Unit.Phase12;

public class DispatchingPaymentGatewayTests
{
    private static IPaymentSettingsService Settings(PaymentSettings s, string apiKey = "k", string pwd = "p")
    {
        var m = Substitute.For<IPaymentSettingsService>();
        m.GetWithSecretsAsync(Arg.Any<CancellationToken>()).Returns((s, apiKey, pwd));
        return m;
    }

    private sealed class FakeGateway : IFcmsPaymentGateway
    {
        public string GatewayId { get; }
        public bool InitiateCalled { get; private set; }
        public bool VerifyCalled { get; private set; }
        public bool WebhookCalled { get; private set; }

        public FakeGateway(string id) { GatewayId = id; }

        public Task<PaymentInitiateResult> InitiateAsync(PaymentInitiateRequest r, PaymentSettings s, string k, CancellationToken ct = default)
        { InitiateCalled = true; return Task.FromResult(PaymentInitiateResult.Ok("https://x", "tx1")); }

        public Task<PaymentResult> VerifyAsync(string tx, PaymentSettings s, string k, CancellationToken ct = default)
        { VerifyCalled = true; return Task.FromResult(PaymentResult.Ok(tx, 100m, "Completed")); }

        public Task<PaymentResult> HandleWebhookAsync(IDictionary<string, string> p, PaymentSettings s, string k, CancellationToken ct = default)
        { WebhookCalled = true; return Task.FromResult(PaymentResult.Ok("tx1", 100m, "Completed")); }
    }

    [Fact]
    public async Task Initiate_returns_fail_when_disabled()
    {
        var d = new DispatchingPaymentGateway(Settings(new PaymentSettings { Enabled = false }), [new FakeGateway(PaymentGateways.Bkash)]);
        var r = await d.InitiateAsync(new PaymentInitiateRequest(100, "BDT", "ord1", "https://cb"));
        Assert.False(r.Success);
        Assert.Contains("not enabled", r.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Initiate_returns_fail_when_gateway_unknown()
    {
        var d = new DispatchingPaymentGateway(
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
        var d = new DispatchingPaymentGateway(
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
        var d = new DispatchingPaymentGateway(
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
        var bkash = new FakeGateway(PaymentGateways.Bkash);
        var ssl = new FakeGateway(PaymentGateways.Sslcommerz);
        var d = new DispatchingPaymentGateway(
            Settings(new PaymentSettings { Enabled = true, ActiveGateway = PaymentGateways.Bkash }),
            [bkash, ssl]);

        await d.HandleWebhookAsync(PaymentGateways.Sslcommerz, new Dictionary<string, string>());

        Assert.False(bkash.WebhookCalled);
        Assert.True(ssl.WebhookCalled);
    }
}
