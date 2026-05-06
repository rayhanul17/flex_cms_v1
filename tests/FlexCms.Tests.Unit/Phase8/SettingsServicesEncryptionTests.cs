using FlexCms.Framework.Messaging;
using FlexCms.Framework.Messaging.Services;
using FlexCms.Framework.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace FlexCms.Tests.Unit.Phase8;

/// <summary>
/// SmtpSettingsService and SmsSettingsService both wrap ISettingsService with
/// IDataProtector for the secret field. These tests verify:
///   1. Save with non-empty new secret → ciphertext (not plaintext) is what gets persisted
///   2. Save with null/empty new secret → previous ciphertext is preserved
///   3. Get round-trip returns the original plaintext
/// A real ephemeral DataProtector keyring is used so the assertions exercise
/// actual encryption rather than a mock.
/// </summary>
public class SettingsServicesEncryptionTests
{
    private static IDataProtectionProvider RealProtectionProvider()
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
    public async Task Smtp_SaveWithPassword_persists_ciphertext_and_decrypts_back_to_plaintext()
    {
        var svc = new SmtpSettingsService(new StubSettings(), RealProtectionProvider());
        await svc.SaveAsync(new SmtpSettings { Enabled = true, Host = "h" }, newPassword: "Secret!1", ct: default);

        var stored = await svc.GetAsync();
        Assert.NotEqual("Secret!1", stored.PasswordEncrypted);   // not plaintext
        Assert.NotEmpty(stored.PasswordEncrypted);

        var (_, plain) = await svc.GetWithPasswordAsync();
        Assert.Equal("Secret!1", plain);
    }

    [Fact]
    public async Task Smtp_SaveWithNullPassword_preserves_existing_ciphertext()
    {
        var svc = new SmtpSettingsService(new StubSettings(), RealProtectionProvider());
        await svc.SaveAsync(new SmtpSettings { Host = "h" }, "OldSecret!1");
        var first = (await svc.GetAsync()).PasswordEncrypted;

        await svc.SaveAsync(new SmtpSettings { Host = "newhost" }, newPassword: null);
        var second = (await svc.GetAsync()).PasswordEncrypted;

        Assert.Equal(first, second);
        Assert.Equal("newhost", (await svc.GetAsync()).Host);
    }

    [Fact]
    public async Task Sms_SaveWithApiKey_persists_ciphertext_and_decrypts_back()
    {
        var svc = new SmsSettingsService(new StubSettings(), RealProtectionProvider());
        await svc.SaveAsync(new SmsSettings { Enabled = true, Gateway = SmsGateways.Alpha }, newApiKey: "k1");

        var stored = await svc.GetAsync();
        Assert.NotEqual("k1", stored.ApiKeyEncrypted);
        Assert.NotEmpty(stored.ApiKeyEncrypted);

        var (_, plain) = await svc.GetWithKeyAsync();
        Assert.Equal("k1", plain);
    }

    [Fact]
    public async Task Sms_SaveWithEmptyApiKey_preserves_existing_ciphertext()
    {
        var svc = new SmsSettingsService(new StubSettings(), RealProtectionProvider());
        await svc.SaveAsync(new SmsSettings { Gateway = SmsGateways.Alpha }, "k1");
        var first = (await svc.GetAsync()).ApiKeyEncrypted;

        await svc.SaveAsync(new SmsSettings { Gateway = SmsGateways.Mram }, newApiKey: "");
        var second = (await svc.GetAsync()).ApiKeyEncrypted;

        Assert.Equal(first, second);
        Assert.Equal(SmsGateways.Mram, (await svc.GetAsync()).Gateway);
    }
}
