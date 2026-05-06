using FlexCms.Framework.Helpers;

namespace FlexCms.Tests.Unit.Phase6;

/// <summary>
/// Unit tests for FcmsHelper.HashPagePassword — verifies HMACSHA256 behaviour.
/// </summary>
public class FcmsHelperSecurityTests
{
    [Fact]
    public void HashPagePassword_same_input_returns_same_hash()
    {
        var h1 = FcmsHelper.HashPagePassword("secret123");
        var h2 = FcmsHelper.HashPagePassword("secret123");
        Assert.Equal(h1, h2);
    }

    [Fact]
    public void HashPagePassword_different_inputs_return_different_hashes()
    {
        var h1 = FcmsHelper.HashPagePassword("secret123");
        var h2 = FcmsHelper.HashPagePassword("secret456");
        Assert.NotEqual(h1, h2);
    }

    [Fact]
    public void HashPagePassword_returns_lowercase_hex()
    {
        var hash = FcmsHelper.HashPagePassword("test");
        Assert.Matches("^[0-9a-f]+$", hash);
    }

    [Fact]
    public void HashPagePassword_returns_64_char_sha256_output()
    {
        var hash = FcmsHelper.HashPagePassword("any");
        Assert.Equal(64, hash.Length);
    }

    [Fact]
    public void HashPagePassword_differs_from_plain_sha256()
    {
        // Ensure it's NOT a simple unsalted SHA-256 (rainbow-table protection)
        var plainSha256 = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes("password"))).ToLowerInvariant();

        var hmacHash = FcmsHelper.HashPagePassword("password");
        Assert.NotEqual(plainSha256, hmacHash);
    }

    [Fact]
    public void HashPagePassword_empty_string_does_not_throw()
    {
        var hash = FcmsHelper.HashPagePassword("");
        Assert.NotEmpty(hash);
    }
}
