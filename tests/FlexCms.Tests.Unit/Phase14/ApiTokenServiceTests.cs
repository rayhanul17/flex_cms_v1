using FlexCms.Framework.Api;
using Xunit;

namespace FlexCms.Tests.Unit.Phase14;

public class ApiTokenServiceTests
{
    [Fact]
    public void HashTokenString_is_stable_and_lowercased_hex()
    {
        var h1 = ApiTokenService.HashTokenString("fcms_abc");
        var h2 = ApiTokenService.HashTokenString("fcms_abc");
        Assert.Equal(h1, h2);
        Assert.Equal(64, h1.Length);   // SHA-256 hex
        Assert.Equal(h1, h1.ToLowerInvariant());
    }

    [Fact]
    public void HashTokenString_changes_with_input()
    {
        Assert.NotEqual(
            ApiTokenService.HashTokenString("fcms_abc"),
            ApiTokenService.HashTokenString("fcms_abd"));
    }

    [Fact]
    public void Token_prefix_is_advertised_for_DI_consumers()
    {
        Assert.Equal("fcms_", ApiTokenService.TokenPrefix);
        Assert.Equal(32, ApiTokenService.TokenByteLength);
    }
}
