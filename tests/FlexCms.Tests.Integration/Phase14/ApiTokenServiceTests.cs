using FlexCms.Framework.Api;
using FlexCms.Framework.Db;
using FlexCms.Framework.Db.Ef;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FlexCms.Tests.Integration.Phase14;

public sealed class ApiTokenServiceTests : IDisposable
{
    private readonly FcmsDbContext _db;
    private readonly ApiTokenService _svc;

    public ApiTokenServiceTests()
    {
        var opts = new DbContextOptionsBuilder<FcmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new FcmsDbContext(opts);
#pragma warning disable CA2000
        _svc = new ApiTokenService(new EfRepository<FcmsApiToken>(_db), new EfUnitOfWork(_db));
#pragma warning restore CA2000
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task IssueAsync_returns_plaintext_only_once_and_persists_hash()
    {
        var u = Guid.NewGuid();
        var issued = await _svc.IssueAsync(u, "iPhone App", "blog.post.read");

        Assert.StartsWith(ApiTokenService.TokenPrefix, issued.PlaintextToken);
        Assert.Equal(64, issued.Token.Hash.Length);   // SHA-256 hex
        Assert.NotEqual(issued.PlaintextToken, issued.Token.Hash);   // hash, not plaintext
        Assert.Equal(8, issued.Token.Prefix.Length);

        // Second call returns a brand-new token; hashes must differ.
        var issued2 = await _svc.IssueAsync(u, "Other", "");
        Assert.NotEqual(issued.PlaintextToken, issued2.PlaintextToken);
        Assert.NotEqual(issued.Token.Hash, issued2.Token.Hash);
    }

    [Fact]
    public async Task ValidateAsync_returns_token_for_valid_plaintext_and_bumps_LastUsedAt()
    {
        var u = Guid.NewGuid();
        var issued = await _svc.IssueAsync(u, "x", "");
        Assert.Null(issued.Token.LastUsedAt);

        var validated = await _svc.ValidateAsync(issued.PlaintextToken);
        Assert.NotNull(validated);
        Assert.Equal(issued.Token.Id, validated!.Id);

        var reloaded = await _db.ApiTokens.AsNoTracking().FirstAsync(t => t.Id == issued.Token.Id);
        Assert.NotNull(reloaded.LastUsedAt);
    }

    [Fact]
    public async Task ValidateAsync_rejects_revoked_token()
    {
        var u = Guid.NewGuid();
        var issued = await _svc.IssueAsync(u, "x", "");
        await _svc.RevokeAsync(issued.Token.Id, u);

        Assert.Null(await _svc.ValidateAsync(issued.PlaintextToken));
    }

    [Fact]
    public async Task ValidateAsync_rejects_expired_token()
    {
        var u = Guid.NewGuid();
        var issued = await _svc.IssueAsync(u, "x", "", expiresAt: DateTime.UtcNow.AddDays(-1));
        Assert.Null(await _svc.ValidateAsync(issued.PlaintextToken));
    }

    [Fact]
    public async Task ValidateAsync_rejects_unknown_or_malformed_tokens()
    {
        Assert.Null(await _svc.ValidateAsync(""));
        Assert.Null(await _svc.ValidateAsync("not-a-fcms-token"));
        Assert.Null(await _svc.ValidateAsync("fcms_garbage"));
    }

    [Fact]
    public async Task GetUserTokensAsync_returns_only_caller_user_tokens_newest_first()
    {
        var u = Guid.NewGuid();
        var other = Guid.NewGuid();
        await _svc.IssueAsync(u, "first", "");
        await Task.Delay(5);
        var second = await _svc.IssueAsync(u, "second", "");
        await _svc.IssueAsync(other, "their token", "");

        var mine = await _svc.GetUserTokensAsync(u);
        Assert.Equal(2, mine.Count);
        Assert.Equal(second.Token.Id, mine[0].Id);   // newest first
    }
}
