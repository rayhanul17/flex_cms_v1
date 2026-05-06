using FlexCms.Framework.Payments;
using FlexCms.Framework.Payments.Services;
using FlexCms.Framework.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FlexCms.Tests.Unit.Phase12;

/// <summary>
/// PaymentSettingsService encrypts the API key + merchant password via
/// DataProtection. These tests use a real ephemeral keyring so they
/// exercise actual encryption rather than a mock.
/// </summary>
public class PaymentSettingsServiceTests
{
    private static IDataProtectionProvider Provider()
    {
        var sc = new ServiceCollection();
        sc.AddDataProtection().SetApplicationName("FlexCms.Tests");
        return sc.BuildServiceProvider().GetRequiredService<IDataProtectionProvider>();
    }

    private sealed class StubSettings : ISettingsService
    {
        private readonly Dictionary<string, object> _store = new();
        public Task<T> GetAsync<T>(string key, CancellationToken ct = default) where T : class, new()
            => Task.FromResult(_store.TryGetValue(key, out var v) ? (T)v : new T());
        public Task SaveAsync<T>(string key, T value, CancellationToken ct = default)
        { _store[key] = value!; return Task.CompletedTask; }
    }

    [Fact]
    public async Task SaveAsync_encrypts_both_secrets()
    {
        var svc = new PaymentSettingsService(new StubSettings(), Provider());
        await svc.SaveAsync(new PaymentSettings { Enabled = true, ActiveGateway = PaymentGateways.Bkash },
            newApiKey: "Key1!", newMerchantPassword: "Pwd1!");

        var stored = await svc.GetAsync();
        Assert.NotEqual("Key1!", stored.ApiKeyEncrypted);
        Assert.NotEmpty(stored.ApiKeyEncrypted);
        Assert.NotEqual("Pwd1!", stored.MerchantPasswordEncrypted);
        Assert.NotEmpty(stored.MerchantPasswordEncrypted);

        var (_, key, pwd) = await svc.GetWithSecretsAsync();
        Assert.Equal("Key1!", key);
        Assert.Equal("Pwd1!", pwd);
    }

    [Fact]
    public async Task SaveAsync_with_null_secrets_preserves_existing_ciphertext()
    {
        var svc = new PaymentSettingsService(new StubSettings(), Provider());
        await svc.SaveAsync(new PaymentSettings { ActiveGateway = PaymentGateways.Bkash },
            "Key1!", "Pwd1!");
        var firstKey = (await svc.GetAsync()).ApiKeyEncrypted;
        var firstPwd = (await svc.GetAsync()).MerchantPasswordEncrypted;

        await svc.SaveAsync(new PaymentSettings { ActiveGateway = PaymentGateways.Sslcommerz },
            newApiKey: null, newMerchantPassword: null);

        var stored = await svc.GetAsync();
        Assert.Equal(firstKey, stored.ApiKeyEncrypted);
        Assert.Equal(firstPwd, stored.MerchantPasswordEncrypted);
        Assert.Equal(PaymentGateways.Sslcommerz, stored.ActiveGateway);
    }

    [Fact]
    public async Task GetWithSecretsAsync_returns_empty_when_no_ciphertext()
    {
        var svc = new PaymentSettingsService(new StubSettings(), Provider());
        var (_, key, pwd) = await svc.GetWithSecretsAsync();
        Assert.Equal("", key);
        Assert.Equal("", pwd);
    }
}
