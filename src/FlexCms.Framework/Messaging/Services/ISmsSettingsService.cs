namespace FlexCms.Framework.Messaging.Services;

public interface ISmsSettingsService
{
    Task<SmsSettings> GetAsync(CancellationToken ct = default);
    Task<(SmsSettings Settings, string ApiKey)> GetWithKeyAsync(CancellationToken ct = default);
    Task SaveAsync(SmsSettings settings, string? newApiKey, CancellationToken ct = default);
}
