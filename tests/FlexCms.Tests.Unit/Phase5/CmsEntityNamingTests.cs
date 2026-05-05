using FlexCms.Framework.Cms;
using FlexCms.Framework.Helpers;

namespace FlexCms.Tests.Unit.Phase5;

public class CmsEntityNamingTests
{
    [Theory]
    [InlineData(typeof(FcmsPage), "fcms_pages")]
    [InlineData(typeof(FcmsPost), "fcms_posts")]
    [InlineData(typeof(FcmsCategory), "fcms_categories")]
    [InlineData(typeof(FcmsTag), "fcms_tags")]
    public void Cms_entities_get_correct_table_names(Type entityType, string expected)
    {
        var actual = FcmsHelper.GetTableName(entityType, "fcms");
        Assert.Equal(expected, actual);
    }
}
