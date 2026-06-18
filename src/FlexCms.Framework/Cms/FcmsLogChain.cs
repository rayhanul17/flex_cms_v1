using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace FlexCms.Framework.Cms;

/// <summary>
/// Computes the tamper-evident hash chain for <see cref="FcmsLog"/> rows.
/// Each row's <see cref="FcmsLog.Hash"/> covers the previous row's hash
/// plus this row's core fields — flipping a single byte anywhere
/// downstream invalidates every chain link after it. See
/// security-audit-fix-plan §5.3.
///
/// <para>
/// The chain is intentionally informational: write paths never block on
/// it (a chain miscomputation must not stop a security event from being
/// recorded), and the admin verifier reports the first broken row rather
/// than refusing to display the log.
/// </para>
/// </summary>
public static class FcmsLogChain
{
    /// <summary>
    /// Compute <see cref="FcmsLog.Hash"/> for a row whose <see cref="FcmsLog.PrevHash"/>
    /// is already set. Returns lowercase hex of the SHA-256.
    /// </summary>
    public static string Compute(FcmsLog row)
    {
        var data =
            (row.PrevHash ?? string.Empty) +
            "|" + row.CreatedAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", System.Globalization.CultureInfo.InvariantCulture) +
            "|" + (row.UserId?.ToString() ?? "") +
            "|" + row.Action +
            "|" + row.EntityType +
            "|" + row.EntityId +
            "|" + (row.Value ?? "");
        return ToHex(SHA256.HashData(Encoding.UTF8.GetBytes(data)));
    }

    /// <summary>Returns the hash of the most-recent FcmsLog row, or null if the table is empty.</summary>
    public static async Task<string?> ReadLatestHashAsync(DbContext ctx, CancellationToken ct = default)
    {
        return await ctx.Set<FcmsLog>()
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Select(x => x.Hash)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Re-walk the chain and return a tuple describing whether it is intact
    /// and, if not, the id of the first broken row. The verifier admin
    /// action calls this to surface tampering. Reads up to <paramref name="limit"/>
    /// rows from newest to oldest so very large logs don't OOM the verifier.
    /// </summary>
    public static async Task<(bool intact, Guid? firstBrokenRowId, int rowsChecked)> VerifyAsync(
        DbContext ctx, int limit = 50_000, CancellationToken ct = default)
    {
        var rows = await ctx.Set<FcmsLog>()
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id)
            .Take(limit)
            .Select(x => new FcmsLog
            {
                Id = x.Id,
                CreatedAt = x.CreatedAt,
                UserId = x.UserId,
                Action = x.Action,
                EntityType = x.EntityType,
                EntityId = x.EntityId,
                Value = x.Value,
                PrevHash = x.PrevHash,
                Hash = x.Hash,
            })
            .ToListAsync(ct);

        string? expectedPrev = null;
        foreach (var row in rows)
        {
            // Skip un-chained legacy rows (pre-§5.3 entries written before the
            // schema upgrade). Tamper detection starts from the first row that
            // *was* chained — anything before that is grandfathered.
            if (row.Hash is null) { expectedPrev = null; continue; }

            if (row.PrevHash != expectedPrev) return (false, row.Id, rows.Count);
            var recomputed = Compute(row);
            if (!string.Equals(recomputed, row.Hash, StringComparison.OrdinalIgnoreCase))
                return (false, row.Id, rows.Count);
            expectedPrev = row.Hash;
        }
        return (true, null, rows.Count);
    }

    private static string ToHex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();
}
