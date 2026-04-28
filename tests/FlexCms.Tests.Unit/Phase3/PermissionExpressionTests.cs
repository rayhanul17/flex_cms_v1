using FlexCms.Framework.Services;

namespace FlexCms.Tests.Unit.Phase3;

public class PermissionExpressionTests
{
    // ── Single key ────────────────────────────────────────────────────────────

    [Fact]
    public void Single_key_present_returns_true()
    {
        var keys = Keys("users.create");
        Assert.True(PermissionExpression.Evaluate("users.create", keys));
    }

    [Fact]
    public void Single_key_absent_returns_false()
    {
        var keys = Keys("users.view");
        Assert.False(PermissionExpression.Evaluate("users.create", keys));
    }

    [Fact]
    public void Single_key_with_extra_whitespace_matches()
    {
        var keys = Keys("users.create");
        Assert.True(PermissionExpression.Evaluate("  users.create  ", keys));
    }

    // ── AND expression ────────────────────────────────────────────────────────

    [Fact]
    public void AND_both_present_returns_true()
    {
        var keys = Keys("users.create", "users.edit");
        Assert.True(PermissionExpression.Evaluate("users.create&users.edit", keys));
    }

    [Fact]
    public void AND_only_first_present_returns_false()
    {
        var keys = Keys("users.create");
        Assert.False(PermissionExpression.Evaluate("users.create&users.edit", keys));
    }

    [Fact]
    public void AND_only_second_present_returns_false()
    {
        var keys = Keys("users.edit");
        Assert.False(PermissionExpression.Evaluate("users.create&users.edit", keys));
    }

    [Fact]
    public void AND_neither_present_returns_false()
    {
        var keys = Keys("posts.view");
        Assert.False(PermissionExpression.Evaluate("users.create&users.edit", keys));
    }

    [Fact]
    public void AND_three_keys_all_present_returns_true()
    {
        var keys = Keys("a", "b", "c");
        Assert.True(PermissionExpression.Evaluate("a&b&c", keys));
    }

    [Fact]
    public void AND_three_keys_one_missing_returns_false()
    {
        var keys = Keys("a", "c");
        Assert.False(PermissionExpression.Evaluate("a&b&c", keys));
    }

    // ── OR expression ─────────────────────────────────────────────────────────

    [Fact]
    public void OR_first_present_returns_true()
    {
        var keys = Keys("users.create");
        Assert.True(PermissionExpression.Evaluate("users.create|users.edit", keys));
    }

    [Fact]
    public void OR_second_present_returns_true()
    {
        var keys = Keys("users.edit");
        Assert.True(PermissionExpression.Evaluate("users.create|users.edit", keys));
    }

    [Fact]
    public void OR_both_present_returns_true()
    {
        var keys = Keys("users.create", "users.edit");
        Assert.True(PermissionExpression.Evaluate("users.create|users.edit", keys));
    }

    [Fact]
    public void OR_neither_present_returns_false()
    {
        var keys = Keys("posts.view");
        Assert.False(PermissionExpression.Evaluate("users.create|users.edit", keys));
    }

    [Fact]
    public void OR_three_keys_last_present_returns_true()
    {
        var keys = Keys("c");
        Assert.True(PermissionExpression.Evaluate("a|b|c", keys));
    }

    // ── Edge cases ────────────────────────────────────────────────────────────

    [Fact]
    public void Empty_user_keys_always_returns_false()
    {
        var keys = Keys();
        Assert.False(PermissionExpression.Evaluate("users.create", keys));
        Assert.False(PermissionExpression.Evaluate("a&b", keys));
        Assert.False(PermissionExpression.Evaluate("a|b", keys));
    }

    [Fact]
    public void AND_with_whitespace_around_keys_matches()
    {
        var keys = Keys("a", "b");
        Assert.True(PermissionExpression.Evaluate("a & b", keys));
    }

    [Fact]
    public void OR_with_whitespace_around_keys_matches()
    {
        var keys = Keys("b");
        Assert.True(PermissionExpression.Evaluate("a | b", keys));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IReadOnlySet<string> Keys(params string[] keys)
        => new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
}
