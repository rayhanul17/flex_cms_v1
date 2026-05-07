using System.Net;
using FlexCms.Framework.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Xunit;

namespace FlexCms.Tests.Unit.Hardening;

/// <summary>
/// IpFilter is a critical admin-area lockdown — every connection passes
/// through it before authentication. Bugs here would either lock out the
/// admin entirely or silently bypass the allowlist. These tests pin down
/// each branch.
/// </summary>
public class IpFilterMiddlewareTests
{
    /// <summary>Drives the middleware against a mocked HttpContext + asserts the next-delegate hit count + response status.</summary>
    private static async Task<(int statusCode, bool nextCalled)> InvokeAsync(IpFilterOptions options, IPAddress? remoteIp)
    {
        var nextCalled = false;
        Task next(HttpContext _) { nextCalled = true; return Task.CompletedTask; }

        var middleware = new IpFilterMiddleware(next, Options.Create(options));
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = remoteIp;

        await middleware.InvokeAsync(ctx);
        return (ctx.Response.StatusCode, nextCalled);
    }

    [Fact]
    public async Task Filter_disabled_passes_every_request_through()
    {
        var (status, nextCalled) = await InvokeAsync(
            new IpFilterOptions { EnforceIpFilter = false, AllowedIps = ["10.0.0.1"] },
            IPAddress.Parse("8.8.8.8"));
        Assert.True(nextCalled);
        // DefaultHttpContext starts at 200 — middleware should not touch it.
        Assert.Equal(StatusCodes.Status200OK, status);
    }

    [Fact]
    public async Task Empty_allowlist_disables_filtering_even_when_enforced()
    {
        // Defensive: a misconfigured EnforceIpFilter=true with no IPs would
        // otherwise reject every request including the admin's own. Spec:
        // empty allowlist short-circuits to "allow all".
        var (status, nextCalled) = await InvokeAsync(
            new IpFilterOptions { EnforceIpFilter = true, AllowedIps = [] },
            IPAddress.Parse("8.8.8.8"));
        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status200OK, status);
    }

    [Fact]
    public async Task Allowed_ip_passes()
    {
        var (status, nextCalled) = await InvokeAsync(
            new IpFilterOptions { EnforceIpFilter = true, AllowedIps = ["10.0.0.1", "192.168.1.1"] },
            IPAddress.Parse("192.168.1.1"));
        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status200OK, status);
    }

    [Fact]
    public async Task Disallowed_ip_returns_403_and_short_circuits()
    {
        var (status, nextCalled) = await InvokeAsync(
            new IpFilterOptions { EnforceIpFilter = true, AllowedIps = ["10.0.0.1"] },
            IPAddress.Parse("8.8.8.8"));
        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, status);
    }

    [Fact]
    public async Task Null_remote_ip_with_filter_enabled_returns_403()
    {
        // Connection-less requests (e.g. unit-test setups, broken proxy
        // forwarding) should NOT bypass the filter — fail closed.
        var (status, nextCalled) = await InvokeAsync(
            new IpFilterOptions { EnforceIpFilter = true, AllowedIps = ["10.0.0.1"] },
            remoteIp: null);
        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, status);
    }

    [Fact]
    public async Task Malformed_allowed_entry_is_silently_skipped()
    {
        // A typo in the allowlist ("10.x.0.1") shouldn't poison the rest —
        // the entry just doesn't match anything.
        var (status, nextCalled) = await InvokeAsync(
            new IpFilterOptions { EnforceIpFilter = true, AllowedIps = ["10.x.0.1", "192.168.1.1"] },
            IPAddress.Parse("192.168.1.1"));
        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status200OK, status);
    }
}
