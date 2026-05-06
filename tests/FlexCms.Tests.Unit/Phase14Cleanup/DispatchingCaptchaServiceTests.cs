using FlexCms.Framework.Captcha;
using FlexCms.Framework.Services;
using NSubstitute;
using Xunit;

namespace FlexCms.Tests.Unit.Phase14Cleanup;

public class DispatchingCaptchaServiceTests
{
    private static ISettingsService Settings(bool enabled, string provider, string secret = "k")
    {
        var dto = new DispatchingCaptchaService.CaptchaSettingsDto
        {
            Enabled = enabled,
            Provider = provider,
            SiteKey = "sk",
            SecretKey = secret,
            AdaptiveLoginThreshold = 3
        };
        var m = Substitute.For<ISettingsService>();
        m.GetAsync<DispatchingCaptchaService.CaptchaSettingsDto>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(dto);
        return m;
    }

    private sealed class FakeProvider : IFcmsCaptchaProvider
    {
        public string ProviderId { get; }
        public bool Called { get; private set; }
        public FakeProvider(string id) { ProviderId = id; }

        public Task<CaptchaResult> VerifyAsync(string r, string? ip, CaptchaSettings s, CancellationToken ct = default)
        {
            Called = true;
            return Task.FromResult(CaptchaResult.Ok());
        }
    }

    [Fact]
    public async Task IsEnabledAsync_reflects_settings()
    {
        var enabled = new DispatchingCaptchaService(Settings(true, CaptchaProviders.Turnstile), [new FakeProvider(CaptchaProviders.Turnstile)]);
        var disabled = new DispatchingCaptchaService(Settings(false, CaptchaProviders.Turnstile), [new FakeProvider(CaptchaProviders.Turnstile)]);
        Assert.True(await enabled.IsEnabledAsync());
        Assert.False(await disabled.IsEnabledAsync());
    }

    [Fact]
    public async Task VerifyAsync_returns_fail_when_disabled()
    {
        var svc = new DispatchingCaptchaService(Settings(false, CaptchaProviders.Turnstile), [new FakeProvider(CaptchaProviders.Turnstile)]);
        var r = await svc.VerifyAsync("token", null);
        Assert.False(r.Success);
        Assert.Contains("not enabled", r.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task VerifyAsync_returns_fail_when_provider_unknown()
    {
        var svc = new DispatchingCaptchaService(Settings(true, "klingon"), [new FakeProvider(CaptchaProviders.Turnstile)]);
        var r = await svc.VerifyAsync("token", null);
        Assert.False(r.Success);
        Assert.Contains("Unknown captcha provider", r.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task VerifyAsync_dispatches_to_active_provider()
    {
        var turnstile = new FakeProvider(CaptchaProviders.Turnstile);
        var hcaptcha = new FakeProvider(CaptchaProviders.Hcaptcha);
        var svc = new DispatchingCaptchaService(
            Settings(true, CaptchaProviders.Hcaptcha),
            [turnstile, hcaptcha]);

        var r = await svc.VerifyAsync("token", null);

        Assert.True(r.Success);
        Assert.False(turnstile.Called);
        Assert.True(hcaptcha.Called);
    }
}
