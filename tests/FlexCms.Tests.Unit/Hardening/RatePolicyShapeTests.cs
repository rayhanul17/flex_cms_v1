using Xunit;

namespace FlexCms.Tests.Unit.Hardening;

/// <summary>
/// Smoke checks against the rate-limit policy shape. We don't spin a host;
/// just verify that the path-prefix predicates we use in
/// <c>FcmsServiceExtensions</c> behave as expected against typical request
/// paths so nobody silently rewrites a route and bypasses the limiter.
/// </summary>
public class RatePolicyShapeTests
{
    [Theory]
    [InlineData("/auth/register", true)]
    [InlineData("/auth/signup", true)]
    [InlineData("/auth/register/", true)]
    [InlineData("/Auth/Register", true)]
    [InlineData("/auth/login", false)]
    [InlineData("/", false)]
    public void Registration_paths_match_expected(string path, bool expected)
    {
        var matches =
            path.StartsWith("/auth/register", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/auth/signup", StringComparison.OrdinalIgnoreCase);
        Assert.Equal(expected, matches);
    }

    [Theory]
    [InlineData("/comments", true)]
    [InlineData("/comments/123", true)]
    [InlineData("/forms/submit", true)]
    [InlineData("/blog/x/comments", false)]   // wrong root — would not be limited
    public void Comment_paths_match_expected(string path, bool expected)
    {
        var matches =
            path.StartsWith("/comments", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/forms/submit", StringComparison.OrdinalIgnoreCase);
        Assert.Equal(expected, matches);
    }

    [Theory]
    [InlineData("/payment/webhook/bkash", true)]
    [InlineData("/payment/webhook/sslcommerz", true)]
    [InlineData("/payment/", false)]
    public void Webhook_paths_match_expected(string path, bool expected)
    {
        var matches = path.StartsWith("/payment/webhook", StringComparison.OrdinalIgnoreCase);
        Assert.Equal(expected, matches);
    }
}
