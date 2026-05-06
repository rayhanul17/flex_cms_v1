using System.Security.Cryptography;
using FlexCms.Framework.Db;

namespace FlexCms.Framework.Api;

public sealed class ApiTokenService : IApiTokenService
{
    public const string TokenPrefix = "fcms_";
    public const int TokenByteLength = 32;

    private readonly IRepository<FcmsApiToken> _repo;
    private readonly IFcmsUnitOfWork _uow;

    public ApiTokenService(IRepository<FcmsApiToken> repo, IFcmsUnitOfWork uow)
    {
        _repo = repo;
        _uow = uow;
    }

    public async Task<ApiTokenIssued> IssueAsync(Guid userId, string name, string scopes, DateTime? expiresAt = null, CancellationToken ct = default)
    {
        var raw = RandomNumberGenerator.GetBytes(TokenByteLength);
        var rawString = Convert.ToBase64String(raw)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');   // base64url
        var plaintext = TokenPrefix + rawString;
        var hash = HashTokenString(plaintext);

        var token = new FcmsApiToken
        {
            UserId = userId,
            Name = name?.Trim() ?? "",
            Hash = hash,
            Prefix = rawString.Length >= 8 ? rawString[..8] : rawString,
            Scopes = scopes ?? "",
            ExpiresAt = expiresAt
        };
        await _repo.AddAsync(token, ct);
        await _uow.SaveChangesAsync(ct);
        return new ApiTokenIssued(token, plaintext);
    }

    public async Task<FcmsApiToken?> ValidateAsync(string plaintextToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(plaintextToken)) return null;
        if (!plaintextToken.StartsWith(TokenPrefix, StringComparison.Ordinal)) return null;

        var hash = HashTokenString(plaintextToken);
        var token = await _repo.FirstOrDefaultAsync(t => t.Hash == hash, ct);
        if (token is null) return null;
        if (token.IsRevoked) return null;
        if (token.ExpiresAt.HasValue && token.ExpiresAt.Value < Clock.FcmsTime.Now) return null;

        // Touch LastUsedAt — best-effort, don't fail the request if the write fails.
        try
        {
            token.LastUsedAt = Clock.FcmsTime.Now;
            await _repo.UpdateAsync(token, ct);
            await _uow.SaveChangesAsync(ct);
        }
        catch { /* ignore — touch is non-critical */ }

        return token;
    }

    public async Task<List<FcmsApiToken>> GetUserTokensAsync(Guid userId, CancellationToken ct = default)
    {
        var rows = await _repo.FindAsync(t => t.UserId == userId, ct);
        return rows.OrderByDescending(t => t.CreatedAt).ToList();
    }

    public async Task RevokeAsync(Guid tokenId, Guid? actingUserId, CancellationToken ct = default)
    {
        var token = await _repo.GetByIdAsync(tokenId, ct);
        if (token is null || token.IsRevoked) return;
        token.IsRevoked = true;
        token.RevokedAt = Clock.FcmsTime.Now;
        await _repo.UpdateAsync(token, ct);
        await _uow.SaveChangesAsync(ct);
    }

    /// <summary>Visible for tests + the Bearer auth handler. Constant-time comparison isn't needed since DB lookup is by hash equality.</summary>
    public static string HashTokenString(string plaintext)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(plaintext);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
