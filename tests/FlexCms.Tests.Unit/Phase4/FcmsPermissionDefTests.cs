using FlexCms.Framework.Modules;

namespace FlexCms.Tests.Unit.Phase4;

public class FcmsPermissionDefTests
{
    [Fact]
    public void Records_with_same_values_are_equal()
    {
        var a = new FcmsPermissionDef("invest.create", "Create", "Investments");
        var b = new FcmsPermissionDef("invest.create", "Create", "Investments");
        Assert.Equal(a, b);
    }

    [Fact]
    public void Group_defaults_to_empty_string()
    {
        var def = new FcmsPermissionDef("invest.create", "Create");
        Assert.Equal("", def.Group);
    }
}
