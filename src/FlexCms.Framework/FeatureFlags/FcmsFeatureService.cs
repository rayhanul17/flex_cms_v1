using System.Security.Cryptography;
using System.Text;
using FlexCms.Framework.Db;

namespace FlexCms.Framework.FeatureFlags;

public sealed class FcmsFeatureService : IFcmsFeatureService
{
    // Short TTL — flag changes propagate within 30s without forcing a
    // restart, but in-loop reads stay cheap.
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    private readonly IRepository<FcmsFeatureFlag> _repo;
    private readonly IFcmsUnitOfWork _uow;
    private readonly object _gate = new();
    private DateTime _cacheLoadedAt = DateTime.MinValue;
    private Dictionary<string, FcmsFeatureFlag> _cache = new(StringComparer.OrdinalIgnoreCase);

    public FcmsFeatureService(IRepository<FcmsFeatureFlag> repo, IFcmsUnitOfWork uow)
    {
        _repo = repo;
        _uow = uow;
    }

    public async Task<bool> IsEnabledAsync(string key, Guid? userId = null, IEnumerable<string>? userRoles = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        var flag = await GetAsync(key, ct);
        if (flag is null || !flag.IsEnabled) return false;

        // Target-role bypass — explicit allowlist for early-access cohorts.
        if (!string.IsNullOrWhiteSpace(flag.TargetRolesCsv) && userRoles is not null)
        {
            var targets = flag.TargetRolesCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (targets.Any(t => userRoles.Contains(t, StringComparer.OrdinalIgnoreCase)))
                return true;
        }

        if (flag.RolloutPercent >= 100) return true;
        if (flag.RolloutPercent <= 0) return false;

        // Stable per-user hash so the same user sees consistent on/off across
        // requests. Anonymous users → never in the rollout cohort (returns
        // false) — anonymous A/B testing needs a different signal (sampled
        // session id) we don't have here.
        if (userId is null) return false;
        var bucket = StableBucket(userId.Value, key);
        return bucket < flag.RolloutPercent;
    }

    public async Task<IReadOnlyList<FcmsFeatureFlag>> ListAsync(CancellationToken ct = default)
    {
        await EnsureCacheAsync(ct);
        return _cache.Values.OrderBy(f => f.Key).ToList();
    }

    public async Task<FcmsFeatureFlag?> GetAsync(string key, CancellationToken ct = default)
    {
        await EnsureCacheAsync(ct);
        return _cache.TryGetValue(key, out var f) ? f : null;
    }

    public async Task UpsertAsync(FcmsFeatureFlag flag, CancellationToken ct = default)
    {
        if (flag is null) throw new ArgumentNullException(nameof(flag));
        if (string.IsNullOrWhiteSpace(flag.Key)) throw new ArgumentException("Key required.", nameof(flag));

        var rows = await _repo.GetAllAsync(ct);
        var existing = rows.FirstOrDefault(r => string.Equals(r.Key, flag.Key, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            await _repo.AddAsync(flag, ct);
        }
        else
        {
            existing.DisplayName = flag.DisplayName;
            existing.Description = flag.Description;
            existing.IsEnabled = flag.IsEnabled;
            existing.RolloutPercent = Math.Clamp(flag.RolloutPercent, 0, 100);
            existing.TargetRolesCsv = flag.TargetRolesCsv ?? "";
            await _repo.UpdateAsync(existing, ct);
        }
        await _uow.SaveChangesAsync(ct);
        InvalidateCache();
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default)
    {
        var rows = await _repo.GetAllAsync(ct);
        var existing = rows.FirstOrDefault(r => string.Equals(r.Key, key, StringComparison.OrdinalIgnoreCase));
        if (existing is null) return;
        await _repo.DeleteAsync(existing, ct);
        await _uow.SaveChangesAsync(ct);
        InvalidateCache();
    }

    private async Task EnsureCacheAsync(CancellationToken ct)
    {
        if (DateTime.UtcNow - _cacheLoadedAt < CacheTtl) return;
        var rows = await _repo.GetAllAsync(ct);
        lock (_gate)
        {
            _cache = rows.ToDictionary(r => r.Key, r => r, StringComparer.OrdinalIgnoreCase);
            _cacheLoadedAt = DateTime.UtcNow;
        }
    }

    private void InvalidateCache()
    {
        lock (_gate) _cacheLoadedAt = DateTime.MinValue;
    }

    /// <summary>
    /// Bucket the user 0–99 from a SHA-256 of (userId + key). Salted by key
    /// so that user X can be in flag-A's 50% cohort and NOT in flag-B's 50%
    /// cohort independently — otherwise we'd correlate every flag.
    /// </summary>
    internal static int StableBucket(Guid userId, string key)
    {
        var input = Encoding.UTF8.GetBytes($"{userId:N}:{key}");
        var hash = SHA256.HashData(input);
        var u = BitConverter.ToUInt32(hash, 0);
        return (int)(u % 100);
    }
}
