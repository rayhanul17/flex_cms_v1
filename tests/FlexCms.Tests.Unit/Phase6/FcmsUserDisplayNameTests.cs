using FlexCms.Framework.Auth;
using Xunit;

namespace FlexCms.Tests.Unit.Phase6;

public class FcmsUserDisplayNameTests
{
    // ── ResolvedDisplayName ───────────────────────────────────────────────────

    [Fact]
    public void ResolvedDisplayName_returns_DisplayName_when_set()
    {
        var user = new FcmsUser { FullName = "John Doe", DisplayName = "JD" };
        Assert.Equal("JD", user.ResolvedDisplayName);
    }

    [Fact]
    public void ResolvedDisplayName_falls_back_to_FullName_when_DisplayName_null()
    {
        var user = new FcmsUser { FullName = "John Doe", DisplayName = null };
        Assert.Equal("John Doe", user.ResolvedDisplayName);
    }

    [Fact]
    public void ResolvedDisplayName_falls_back_to_FullName_when_DisplayName_whitespace()
    {
        var user = new FcmsUser { FullName = "John Doe", DisplayName = "   " };
        Assert.Equal("John Doe", user.ResolvedDisplayName);
    }

    [Fact]
    public void ResolvedDisplayName_falls_back_to_FullName_when_DisplayName_empty()
    {
        var user = new FcmsUser { FullName = "John Doe", DisplayName = "" };
        Assert.Equal("John Doe", user.ResolvedDisplayName);
    }

    // ── FullName default ──────────────────────────────────────────────────────

    [Fact]
    public void FullName_defaults_to_empty_string()
    {
        var user = new FcmsUser();
        Assert.Equal("", user.FullName);
    }

    [Fact]
    public void DisplayName_defaults_to_null()
    {
        var user = new FcmsUser();
        Assert.Null(user.DisplayName);
    }
}
