using FlexCms.Framework.Cms.Preview;
using Xunit;

namespace FlexCms.Tests.Unit.Hardening;

public class PreviewTokenServiceTests
{
    [Fact]
    public void GenerateToken_returns_url_safe_high_entropy_string()
    {
        var t = PreviewTokenService.GenerateToken();
        // 32 bytes → unpadded URL-safe base64 ≈ 43 chars.
        Assert.InRange(t.Length, 40, 50);
        Assert.DoesNotContain('+', t);
        Assert.DoesNotContain('/', t);
        Assert.DoesNotContain('=', t);
    }

    [Fact]
    public void GenerateToken_is_unique_across_calls()
    {
        var seen = new HashSet<string>();
        for (var i = 0; i < 200; i++)
            Assert.True(seen.Add(PreviewTokenService.GenerateToken()),
                "Duplicate token within 200 calls — RNG isn't doing its job.");
    }
}
