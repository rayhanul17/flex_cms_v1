namespace FlexCms.Framework.Payments.Services;

public interface IPaymentSettingsService
{
    Task<PaymentSettings> GetAsync(CancellationToken ct = default);
    Task<(PaymentSettings Settings, string ApiKey, string MerchantPassword)> GetWithSecretsAsync(CancellationToken ct = default);
    Task SaveAsync(PaymentSettings settings, string? newApiKey, string? newMerchantPassword, CancellationToken ct = default);
}
