using FlexCms.Framework.Payments;
using FlexCms.Framework.Payments.Services;
using FlexCms.Framework.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FlexCms.Tests.Unit.Phase12;

/// <summary>
/// PaymentSettingsService encrypts each gateway's credentials via DataProtection
/// using gateway-scoped purposes. These tests use a real ephemeral keyring so
/// they exercise actual encryption rather than a mock.
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

    // ---------- General ----------

    [Fact]
    public async Task SaveGeneralAsync_round_trips_settings()
    {
        var svc = new PaymentSettingsService(new StubSettings(), Provider(), Microsoft.Extensions.Logging.Abstractions.NullLogger<PaymentSettingsService>.Instance);
        await svc.SaveGeneralAsync(new PaymentSettings { Enabled = true, ActiveGateway = PaymentGateways.Sslcommerz });
        var stored = await svc.GetGeneralAsync();
        Assert.True(stored.Enabled);
        Assert.Equal(PaymentGateways.Sslcommerz, stored.ActiveGateway);
    }

    // ---------- bKash ----------

    [Fact]
    public async Task SaveBkashAsync_encrypts_both_secrets()
    {
        var svc = new PaymentSettingsService(new StubSettings(), Provider(), Microsoft.Extensions.Logging.Abstractions.NullLogger<PaymentSettingsService>.Instance);
        await svc.SaveBkashAsync(
            new BkashSettings { Enabled = true, AppKey = "akey", Username = "u" },
            newAppSecret: "Secret1!", newPassword: "Pwd1!");

        var stored = await svc.GetBkashAsync();
        Assert.NotEqual("Secret1!", stored.AppSecretEncrypted);
        Assert.NotEmpty(stored.AppSecretEncrypted);
        Assert.NotEqual("Pwd1!", stored.PasswordEncrypted);
        Assert.NotEmpty(stored.PasswordEncrypted);

        var (_, secret, pwd) = await svc.GetBkashWithSecretsAsync();
        Assert.Equal("Secret1!", secret);
        Assert.Equal("Pwd1!", pwd);
    }

    [Fact]
    public async Task SaveBkashAsync_with_null_secrets_preserves_existing_ciphertext()
    {
        var svc = new PaymentSettingsService(new StubSettings(), Provider(), Microsoft.Extensions.Logging.Abstractions.NullLogger<PaymentSettingsService>.Instance);
        await svc.SaveBkashAsync(new BkashSettings { AppKey = "akey" }, "Secret1!", "Pwd1!");
        var firstSecret = (await svc.GetBkashAsync()).AppSecretEncrypted;
        var firstPwd = (await svc.GetBkashAsync()).PasswordEncrypted;

        await svc.SaveBkashAsync(new BkashSettings { AppKey = "akey-changed" }, newAppSecret: null, newPassword: null);

        var stored = await svc.GetBkashAsync();
        Assert.Equal(firstSecret, stored.AppSecretEncrypted);
        Assert.Equal(firstPwd, stored.PasswordEncrypted);
        Assert.Equal("akey-changed", stored.AppKey);
    }

    [Fact]
    public async Task GetBkashWithSecretsAsync_returns_empty_when_no_ciphertext()
    {
        var svc = new PaymentSettingsService(new StubSettings(), Provider(), Microsoft.Extensions.Logging.Abstractions.NullLogger<PaymentSettingsService>.Instance);
        var (_, secret, pwd) = await svc.GetBkashWithSecretsAsync();
        Assert.Equal("", secret);
        Assert.Equal("", pwd);
    }

    // ---------- SSLCommerz ----------

    [Fact]
    public async Task SaveSslcommerzAsync_encrypts_store_password()
    {
        var svc = new PaymentSettingsService(new StubSettings(), Provider(), Microsoft.Extensions.Logging.Abstractions.NullLogger<PaymentSettingsService>.Instance);
        await svc.SaveSslcommerzAsync(new SslcommerzSettings { Enabled = true, StoreId = "sid" }, "StorePwd1!");

        var stored = await svc.GetSslcommerzAsync();
        Assert.NotEqual("StorePwd1!", stored.StorePasswordEncrypted);
        Assert.NotEmpty(stored.StorePasswordEncrypted);

        var (_, pwd) = await svc.GetSslcommerzWithSecretsAsync();
        Assert.Equal("StorePwd1!", pwd);
    }

    [Fact]
    public async Task SaveSslcommerzAsync_with_null_secret_preserves_ciphertext()
    {
        var svc = new PaymentSettingsService(new StubSettings(), Provider(), Microsoft.Extensions.Logging.Abstractions.NullLogger<PaymentSettingsService>.Instance);
        await svc.SaveSslcommerzAsync(new SslcommerzSettings { StoreId = "sid" }, "StorePwd1!");
        var firstPwd = (await svc.GetSslcommerzAsync()).StorePasswordEncrypted;

        await svc.SaveSslcommerzAsync(new SslcommerzSettings { StoreId = "sid2" }, newStorePassword: null);

        var stored = await svc.GetSslcommerzAsync();
        Assert.Equal(firstPwd, stored.StorePasswordEncrypted);
        Assert.Equal("sid2", stored.StoreId);
    }

    // ---------- Nagad ----------

    [Fact]
    public async Task SaveNagadAsync_encrypts_merchant_private_key()
    {
        var svc = new PaymentSettingsService(new StubSettings(), Provider(), Microsoft.Extensions.Logging.Abstractions.NullLogger<PaymentSettingsService>.Instance);
        await svc.SaveNagadAsync(
            new NagadSettings { Enabled = true, MerchantId = "mid" },
            "-----BEGIN PRIVATE KEY-----...");

        var stored = await svc.GetNagadAsync();
        Assert.DoesNotContain("BEGIN PRIVATE KEY", stored.MerchantPrivateKeyEncrypted);
        Assert.NotEmpty(stored.MerchantPrivateKeyEncrypted);

        var (_, pk) = await svc.GetNagadWithSecretsAsync();
        Assert.StartsWith("-----BEGIN PRIVATE KEY-----", pk);
    }

    [Fact]
    public async Task SaveNagadAsync_with_null_secret_preserves_ciphertext()
    {
        var svc = new PaymentSettingsService(new StubSettings(), Provider(), Microsoft.Extensions.Logging.Abstractions.NullLogger<PaymentSettingsService>.Instance);
        await svc.SaveNagadAsync(new NagadSettings { MerchantId = "mid" }, "-----BEGIN-----xxx");
        var firstPk = (await svc.GetNagadAsync()).MerchantPrivateKeyEncrypted;

        await svc.SaveNagadAsync(new NagadSettings { MerchantId = "mid2" }, newMerchantPrivateKey: null);

        var stored = await svc.GetNagadAsync();
        Assert.Equal(firstPk, stored.MerchantPrivateKeyEncrypted);
        Assert.Equal("mid2", stored.MerchantId);
    }

    // ---------- Cross-gateway isolation ----------

    [Fact]
    public async Task Per_gateway_purposes_are_isolated()
    {
        // Same plaintext encrypted under one gateway's purpose must NOT decrypt
        // under another's — this guards the per-gateway IDataProtector isolation.
        var svc = new PaymentSettingsService(new StubSettings(), Provider(), Microsoft.Extensions.Logging.Abstractions.NullLogger<PaymentSettingsService>.Instance);

        await svc.SaveBkashAsync(new BkashSettings(), newAppSecret: "shared", newPassword: null);
        var bkashCipher = (await svc.GetBkashAsync()).AppSecretEncrypted;

        // Manually inject the bKash ciphertext into the SSLCommerz settings —
        // GetSslcommerzWithSecretsAsync should silently return "" instead of
        // decrypting to "shared".
        await svc.SaveSslcommerzAsync(
            new SslcommerzSettings { StoreId = "x", StorePasswordEncrypted = bkashCipher },
            newStorePassword: null);
        var (_, sslPwd) = await svc.GetSslcommerzWithSecretsAsync();
        Assert.Equal("", sslPwd);
    }
}
