using System.Security.Claims;
using FlexCms.Framework.Db;
using FlexCms.Framework.Db.Ef;
using FlexCms.Framework.Sessions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FlexCms.Tests.Integration.Phase13Cleanup;

/// <summary>
/// Verifies the per-request enforcement contract: revoked sessions get
/// signed out + downgraded to anonymous; live sessions pass through
/// untouched (with a best-effort LastSeenAt touch); requests without a
/// session-id claim (anonymous, or Bearer-token API calls) skip the
/// middleware entirely so other auth modes keep working.
/// </summary>
public sealed class SessionValidationMiddlewareTests : IDisposable
{
    private readonly FcmsDbContext _db;
    private readonly SessionService _svc;

    public SessionValidationMiddlewareTests()
    {
        var opts = new DbContextOptionsBuilder<FcmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new FcmsDbContext(opts);
#pragma warning disable CA2000
        _svc = new SessionService(new EfRepository<FcmsUserSession>(_db), new EfUnitOfWork(_db));
#pragma warning restore CA2000
    }

    public void Dispose() => _db.Dispose();

    private static DefaultHttpContext BuildContext(string? sessionId, IServiceProvider sp)
    {
        var ctx = new DefaultHttpContext { RequestServices = sp };
        if (sessionId is not null)
        {
            var identity = new ClaimsIdentity(
                [new Claim(FcmsSessionValidationMiddleware.SessionIdClaim, sessionId)],
                "TestCookie");
            ctx.User = new ClaimsPrincipal(identity);
        }
        else
        {
            ctx.User = new ClaimsPrincipal(new ClaimsIdentity());
        }
        return ctx;
    }

    private static IServiceProvider BuildSp()
    {
        var sc = new ServiceCollection();
        sc.AddLogging();
        // SignOutAsync is invoked through IAuthenticationService — provide a no-op stub.
        sc.AddSingleton<IAuthenticationService, NoOpAuth>();
        sc.AddSingleton<IAuthenticationSchemeProvider, NoOpSchemeProvider>();
        return sc.BuildServiceProvider();
    }

    private sealed class NoOpAuth : IAuthenticationService
    {
        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) => Task.FromResult(AuthenticateResult.NoResult());
        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
        public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties) => Task.CompletedTask;
        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
    }

    private sealed class NoOpSchemeProvider : IAuthenticationSchemeProvider
    {
        public Task<AuthenticationScheme?> GetSchemeAsync(string name) => Task.FromResult<AuthenticationScheme?>(null);
        public Task<IEnumerable<AuthenticationScheme>> GetAllSchemesAsync() => Task.FromResult(Enumerable.Empty<AuthenticationScheme>());
        public Task<AuthenticationScheme?> GetDefaultAuthenticateSchemeAsync() => Task.FromResult<AuthenticationScheme?>(null);
        public Task<AuthenticationScheme?> GetDefaultChallengeSchemeAsync() => Task.FromResult<AuthenticationScheme?>(null);
        public Task<AuthenticationScheme?> GetDefaultForbidSchemeAsync() => Task.FromResult<AuthenticationScheme?>(null);
        public Task<AuthenticationScheme?> GetDefaultSignInSchemeAsync() => Task.FromResult<AuthenticationScheme?>(null);
        public Task<AuthenticationScheme?> GetDefaultSignOutSchemeAsync() => Task.FromResult<AuthenticationScheme?>(null);
        public Task<IEnumerable<AuthenticationScheme>> GetRequestHandlerSchemesAsync() => Task.FromResult(Enumerable.Empty<AuthenticationScheme>());
        public void AddScheme(AuthenticationScheme scheme) { }
        public void RemoveScheme(string name) { }
    }

    [Fact]
    public async Task Anonymous_request_passes_through_without_db_lookup()
    {
        var sp = BuildSp();
        var ctx = BuildContext(sessionId: null, sp);
        var nextRan = false;
        var mw = new FcmsSessionValidationMiddleware(_ => { nextRan = true; return Task.CompletedTask; });

        await mw.InvokeAsync(ctx, _svc);

        Assert.True(nextRan);
        Assert.Equal(0, await _db.UserSessions.CountAsync());   // no row, no lookup
    }

    [Fact]
    public async Task Authenticated_request_with_no_session_claim_passes_through()
    {
        var sp = BuildSp();
        var ctx = BuildContext(sessionId: null, sp);
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], "Bearer"));

        var nextRan = false;
        var mw = new FcmsSessionValidationMiddleware(_ => { nextRan = true; return Task.CompletedTask; });

        await mw.InvokeAsync(ctx, _svc);

        Assert.True(nextRan);
        // User principal preserved — token-based auth shouldn't get downgraded.
        Assert.True(ctx.User.Identity?.IsAuthenticated);
    }

    [Fact]
    public async Task Active_session_passes_through_and_touches_LastSeenAt()
    {
        var u = Guid.NewGuid();
        var session = await _svc.RecordLoginAsync(u, "sess1", "1.2.3.4", "ua", "Chrome");
        var firstSeen = session.LastSeenAt;
        await Task.Delay(10);

        var sp = BuildSp();
        var ctx = BuildContext(sessionId: "sess1", sp);
        var nextRan = false;
        var mw = new FcmsSessionValidationMiddleware(_ => { nextRan = true; return Task.CompletedTask; });

        await mw.InvokeAsync(ctx, _svc);

        Assert.True(nextRan);
        Assert.True(ctx.User.Identity?.IsAuthenticated);   // not downgraded
        var reloaded = await _db.UserSessions.AsNoTracking().FirstAsync();
        Assert.True(reloaded.LastSeenAt > firstSeen, "TouchAsync should have bumped LastSeenAt.");
    }

    [Fact]
    public async Task Revoked_session_downgrades_to_anonymous()
    {
        var u = Guid.NewGuid();
        await _svc.RecordLoginAsync(u, "sess1", "ip", "ua", "");
        await _svc.RevokeAsync("sess1", null, "admin force-logout");

        var sp = BuildSp();
        var ctx = BuildContext(sessionId: "sess1", sp);
        var nextRan = false;
        var mw = new FcmsSessionValidationMiddleware(_ => { nextRan = true; return Task.CompletedTask; });

        await mw.InvokeAsync(ctx, _svc);

        Assert.True(nextRan);
        Assert.False(ctx.User.Identity?.IsAuthenticated, "Revoked session must be downgraded to anonymous.");
    }

    [Fact]
    public async Task Unknown_session_id_downgrades_to_anonymous()
    {
        var sp = BuildSp();
        var ctx = BuildContext(sessionId: "never-existed", sp);
        var nextRan = false;
        var mw = new FcmsSessionValidationMiddleware(_ => { nextRan = true; return Task.CompletedTask; });

        await mw.InvokeAsync(ctx, _svc);

        Assert.True(nextRan);
        Assert.False(ctx.User.Identity?.IsAuthenticated);
    }
}
