using FlexCms.Framework.Messaging;
using FlexCms.Framework.Messaging.Services;
using NSubstitute;
using Xunit;

namespace FlexCms.Tests.Unit.Phase8;

public class DispatchingSmsSenderTests
{
    private static ISmsSettingsService Settings(SmsSettings s, string apiKey = "key")
    {
        var m = Substitute.For<ISmsSettingsService>();
        m.GetWithKeyAsync(Arg.Any<CancellationToken>()).Returns((s, apiKey));
        return m;
    }

    private sealed class FakeGateway : ISmsGateway
    {
        public string GatewayId { get; }
        public bool Called { get; private set; }
        public FakeGateway(string id) { GatewayId = id; }

        public Task<SmsSendResult> SendAsync(SmsMessage message, SmsSettings settings, string apiKey, CancellationToken ct = default)
        {
            Called = true;
            return Task.FromResult(SmsSendResult.Ok("fake"));
        }
    }

    [Fact]
    public async Task Returns_fail_when_disabled()
    {
        var sender = new DispatchingSmsSender(Settings(new SmsSettings { Enabled = false }), [new FakeGateway(SmsGateways.Alpha)]);
        var r = await sender.SendAsync(new SmsMessage("01700000000", "hi"));
        Assert.False(r.Success);
        Assert.Contains("not enabled", r.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Returns_fail_when_api_key_missing()
    {
        var sender = new DispatchingSmsSender(Settings(new SmsSettings { Enabled = true }, apiKey: ""), [new FakeGateway(SmsGateways.Alpha)]);
        var r = await sender.SendAsync(new SmsMessage("01700000000", "hi"));
        Assert.False(r.Success);
        Assert.Contains("API key", r.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Returns_fail_when_gateway_unknown()
    {
        var sender = new DispatchingSmsSender(
            Settings(new SmsSettings { Enabled = true, Gateway = "klingon" }),
            [new FakeGateway(SmsGateways.Alpha)]);
        var r = await sender.SendAsync(new SmsMessage("01700000000", "hi"));
        Assert.False(r.Success);
        Assert.Contains("Unknown SMS gateway", r.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Returns_fail_when_recipient_missing()
    {
        var sender = new DispatchingSmsSender(
            Settings(new SmsSettings { Enabled = true, Gateway = SmsGateways.Alpha }),
            [new FakeGateway(SmsGateways.Alpha)]);
        var r = await sender.SendAsync(new SmsMessage("", "hi"));
        Assert.False(r.Success);
    }

    [Fact]
    public async Task Dispatches_to_matching_gateway()
    {
        var alpha = new FakeGateway(SmsGateways.Alpha);
        var mram = new FakeGateway(SmsGateways.Mram);
        var sender = new DispatchingSmsSender(
            Settings(new SmsSettings { Enabled = true, Gateway = SmsGateways.Mram }),
            [alpha, mram]);

        var r = await sender.SendAsync(new SmsMessage("01700000000", "hi"));

        Assert.True(r.Success);
        Assert.False(alpha.Called);
        Assert.True(mram.Called);
    }
}
