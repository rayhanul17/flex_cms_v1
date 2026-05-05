using FlexCms.Framework.Helpers;

namespace FlexCms.Tests.Unit.Phase4;

public class FcmsHelperTests
{
    // ── ToSnakeCase ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("FcmsUser", "fcms_user")]
    [InlineData("BlogPost", "blog_post")]
    [InlineData("AbcDef", "abc_def")]
    [InlineData("StudentRecord", "student_record")]
    [InlineData("A", "a")]
    [InlineData("HTTPRequest", "http_request")]
    [InlineData("XMLParser", "xml_parser")]
    [InlineData("user", "user")]
    [InlineData("", "")]
    public void ToSnakeCase_handles_common_cases(string input, string expected)
        => Assert.Equal(expected, FcmsHelper.ToSnakeCase(input));

    // ── Pluralize ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("user", "users")]
    [InlineData("post", "posts")]
    [InlineData("users", "users")]              // already plural
    [InlineData("category", "categories")]
    [InlineData("address", "addresses")]
    [InlineData("box", "boxes")]
    [InlineData("buzz", "buzzes")]
    [InlineData("dish", "dishes")]
    [InlineData("watch", "watches")]
    [InlineData("day", "days")]                 // vowel before y
    [InlineData("", "")]
    public void Pluralize_follows_basic_english_rules(string input, string expected)
        => Assert.Equal(expected, FcmsHelper.Pluralize(input));

    // ── GetTableName ─────────────────────────────────────────────────────────

    [Fact]
    public void GetTableName_framework_entity_with_fcms_prefix_already_in_name()
        => Assert.Equal("fcms_users", FcmsHelper.GetTableName<FcmsUser>("fcms"));

    [Fact]
    public void GetTableName_framework_entity_already_prefixed()
        => Assert.Equal("fcms_permissions", FcmsHelper.GetTableName<FcmsPermission>("fcms"));

    [Fact]
    public void GetTableName_module_entity_with_prefix_already_in_name()
        => Assert.Equal("blog_posts", FcmsHelper.GetTableName<BlogPost>("blog"));

    [Fact]
    public void GetTableName_module_entity_prefix_prepended()
        => Assert.Equal("blog_comments", FcmsHelper.GetTableName<Comment>("blog"));

    [Fact]
    public void GetTableName_compound_name_with_prefix_prepended()
        => Assert.Equal("school_student_records", FcmsHelper.GetTableName<StudentRecord>("school"));

    [Fact]
    public void GetTableName_no_prefix_just_snake_case_plural()
        => Assert.Equal("blog_posts", FcmsHelper.GetTableName<BlogPost>(""));

    [Fact]
    public void GetTableName_explicit_attribute_override_wins()
        => Assert.Equal("custom_table_name", FcmsHelper.GetTableName<EntityWithOverride>("blog"));

    // ── Test fixture types ────────────────────────────────────────────────────

    private class FcmsUser { }
    private class FcmsPermission { }
    private class BlogPost { }
    private class Comment { }
    private class StudentRecord { }

    [FcmsTable("custom_table_name")]
    private class EntityWithOverride { }
}
