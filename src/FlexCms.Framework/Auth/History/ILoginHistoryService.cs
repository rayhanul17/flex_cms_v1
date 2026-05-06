namespace FlexCms.Framework.Auth.History;

public interface ILoginHistoryService
{
    Task RecordAsync(string attemptedUserName, Guid? userId, LoginOutcome outcome, string ip, string userAgent, string? failReason = null, CancellationToken ct = default);

    Task<List<FcmsLoginHistory>> GetRecentAsync(int max = 100, CancellationToken ct = default);

    Task<int> GetFailedCountSinceAsync(DateTime sinceUtc, CancellationToken ct = default);
}
