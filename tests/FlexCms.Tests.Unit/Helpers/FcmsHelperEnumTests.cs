using System.ComponentModel;
using FlexCms.Framework.Db;
using FlexCms.Framework.Helpers;

namespace FlexCms.Tests.Unit.Helpers;

public class FcmsHelperEnumTests
{
    private enum Severity
    {
        [Description("Low priority")]
        Low = 1,
        [Description("High priority")]
        High = 2,
        Unlabelled = 3 // no Description attribute
    }

    [Fact]
    public void GetEnumDescription_returns_attribute_text_when_present()
        => Assert.Equal("Low priority", FcmsHelper.GetEnumDescription(Severity.Low));

    [Fact]
    public void GetEnumDescription_falls_back_to_member_name_when_attribute_missing()
        => Assert.Equal("Unlabelled", FcmsHelper.GetEnumDescription(Severity.Unlabelled));

    [Fact]
    public void GetEnumDescriptionFromId_resolves_by_integer_value()
        => Assert.Equal("High priority", FcmsHelper.GetEnumDescriptionFromId<Severity>(2));

    [Fact]
    public void GetEnumDescriptionFromId_returns_fallback_for_unknown_id()
        => Assert.Equal("?", FcmsHelper.GetEnumDescriptionFromId<Severity>(99, "?"));

    [Fact]
    public void GetEnumFromDescription_is_case_insensitive()
        => Assert.Equal(Severity.High, FcmsHelper.GetEnumFromDescription<Severity>("high priority"));

    [Fact]
    public void GetEnumFromDescription_returns_null_on_no_match()
        => Assert.Null(FcmsHelper.GetEnumFromDescription<Severity>("nope"));

    [Fact]
    public void EnumToSelectList_includes_All_when_requested()
    {
        var list = FcmsHelper.EnumToSelectList<Severity>(includeAll: true);
        Assert.Equal((0, "All"), list[0]);
        Assert.Contains((1, "Low priority"), list);
    }

    [Fact]
    public void EnumToSelectList_excludes_specified_ids()
    {
        var list = FcmsHelper.EnumToSelectList<Severity>(excludeList: new List<int> { 1 });
        Assert.DoesNotContain(list, t => t.Value == 1);
    }

    [Fact]
    public void EntityStatus_has_descriptions_for_all_members()
    {
        Assert.Equal("Inactive", FcmsHelper.GetEnumDescription(EntityStatus.InActive));
        Assert.Equal("Active", FcmsHelper.GetEnumDescription(EntityStatus.Active));
        Assert.Equal("Deleted", FcmsHelper.GetEnumDescription(EntityStatus.Deleted));
    }
}
