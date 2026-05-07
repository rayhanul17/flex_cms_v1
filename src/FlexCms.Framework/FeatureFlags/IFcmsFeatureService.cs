namespace FlexCms.Framework.FeatureFlags;

/// <summary>
/// Read/write feature-flag state. Reads are hot — cached for a short TTL
/// inside the impl so checking a flag in a render loop doesn't hammer EF.
/// </summary>
public interface IFcmsFeatureService
{
    /// <summary>Fast check used in code paths and tag helpers. Anonymous user → no target-role bypass, only percent gate (using a request-scoped hash).</summary>
    Task<bool> IsEnabledAsync(string key, Guid? userId = null, IEnumerable<string>? userRoles = null, CancellationToken ct = default);

    Task<IReadOnlyList<FcmsFeatureFlag>> ListAsync(CancellationToken ct = default);
    Task<FcmsFeatureFlag?> GetAsync(string key, CancellationToken ct = default);
    Task UpsertAsync(FcmsFeatureFlag flag, CancellationToken ct = default);
    Task DeleteAsync(string key, CancellationToken ct = default);
}
