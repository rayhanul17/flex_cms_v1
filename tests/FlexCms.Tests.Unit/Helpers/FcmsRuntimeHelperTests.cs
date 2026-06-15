using FlexCms.Framework.Helpers;

namespace FlexCms.Tests.Unit.Helpers;

public class FcmsRuntimeHelperTests
{
    [Fact]
    public void Exactly_one_os_flag_is_true()
    {
        var flags = new[] { FcmsRuntimeHelper.IsWindows, FcmsRuntimeHelper.IsLinux, FcmsRuntimeHelper.IsMacOS };
        Assert.Single(flags, f => f);
    }

    [Fact]
    public void OsShortName_matches_active_flag()
    {
        var name = FcmsRuntimeHelper.OsShortName;
        Assert.Equal(
            FcmsRuntimeHelper.IsWindows ? "Windows"
            : FcmsRuntimeHelper.IsLinux ? "Linux"
            : FcmsRuntimeHelper.IsMacOS ? "macOS"
            : "Other",
            name);
    }

    [Fact]
    public void FrameworkDescription_is_non_empty()
        => Assert.False(string.IsNullOrWhiteSpace(FcmsRuntimeHelper.FrameworkDescription));

    [Fact]
    public void ProcessArchitecture_is_known_value()
        => Assert.Contains(FcmsRuntimeHelper.ProcessArchitecture, new[] { "X86", "X64", "Arm", "Arm64" });
}
