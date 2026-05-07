using FlexCms.Framework.Modules.SemVer;
using Xunit;

namespace FlexCms.Tests.Unit.Phase15;

public class SemVerTests
{
    // ── Parsing ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("1.2.3", 1, 2, 3)]
    [InlineData("0.0.1", 0, 0, 1)]
    [InlineData("10.20.30", 10, 20, 30)]
    [InlineData("1", 1, 0, 0)]
    [InlineData("1.2", 1, 2, 0)]
    public void Parses_valid_versions(string s, int maj, int min, int patch)
    {
        var v = SemVer.Parse(s);
        Assert.NotNull(v);
        Assert.Equal(new SemVer(maj, min, patch), v.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1.x")]
    [InlineData("1.2.3.4")]
    [InlineData("-1.0.0")]
    [InlineData("foo")]
    public void Rejects_invalid_versions(string s)
    {
        Assert.Null(SemVer.Parse(s));
    }

    // ── Comparison ──────────────────────────────────────────────────────────

    [Fact]
    public void CompareTo_orders_lexicographically_by_major_minor_patch()
    {
        Assert.True(new SemVer(2, 0, 0).CompareTo(new SemVer(1, 9, 9)) > 0);
        Assert.True(new SemVer(1, 1, 0).CompareTo(new SemVer(1, 0, 999)) > 0);
        Assert.True(new SemVer(1, 0, 1).CompareTo(new SemVer(1, 0, 0)) > 0);
        Assert.Equal(0, new SemVer(1, 2, 3).CompareTo(new SemVer(1, 2, 3)));
    }

    // ── Constraint operators ─────────────────────────────────────────────────

    [Theory]
    [InlineData(">=1.2.0", "1.2.0", true)]
    [InlineData(">=1.2.0", "1.2.5", true)]
    [InlineData(">=1.2.0", "2.0.0", true)]
    [InlineData(">=1.2.0", "1.1.99", false)]
    [InlineData("<=2.0.0", "1.99.99", true)]
    [InlineData("<=2.0.0", "2.0.1", false)]
    [InlineData(">1.0.0", "1.0.0", false)]
    [InlineData(">1.0.0", "1.0.1", true)]
    [InlineData("<2.0.0", "1.99.99", true)]
    [InlineData("<2.0.0", "2.0.0", false)]
    [InlineData("=1.2.3", "1.2.3", true)]
    [InlineData("=1.2.3", "1.2.4", false)]
    [InlineData("1.2.3", "1.2.3", true)]   // bare = exact
    [InlineData("1.2.3", "1.2.4", false)]
    [InlineData("^1.2.0", "1.5.0", true)]  // same major
    [InlineData("^1.2.0", "1.2.0", true)]
    [InlineData("^1.2.0", "2.0.0", false)] // major bump → out
    [InlineData("^1.2.0", "1.1.99", false)] // below the floor
    [InlineData("~1.2.0", "1.2.5", true)]  // same major+minor
    [InlineData("~1.2.0", "1.3.0", false)]
    [InlineData("~1.2.0", "1.2.0", true)]
    [InlineData("~1.2.0", "1.1.99", false)]
    public void Constraint_satisfaction_matches_operator_semantics(string constraint, string actual, bool expected)
    {
        var c = SemVerConstraint.Parse(constraint);
        Assert.NotNull(c);
        var v = SemVer.Parse(actual);
        Assert.NotNull(v);
        Assert.Equal(expected, c!.IsSatisfiedBy(v!.Value));
    }

    // ── Dependency-string parsing ────────────────────────────────────────────

    [Fact]
    public void ParseDependency_with_operator_splits_id_and_constraint()
    {
        var (id, c) = SemVerConstraint.ParseDependency("BlogModule>=1.2.0");
        Assert.Equal("BlogModule", id);
        Assert.NotNull(c);
        Assert.Equal(">=", c!.Operator);
        Assert.Equal(new SemVer(1, 2, 0), c.Version);
    }

    [Fact]
    public void ParseDependency_without_operator_returns_id_only()
    {
        var (id, c) = SemVerConstraint.ParseDependency("BlogModule");
        Assert.Equal("BlogModule", id);
        Assert.Null(c);   // no constraint = presence check only
    }

    [Fact]
    public void ParseDependency_handles_caret_form()
    {
        var (id, c) = SemVerConstraint.ParseDependency("CoreApi^1.0.0");
        Assert.Equal("CoreApi", id);
        Assert.Equal("^", c!.Operator);
    }

    // ── Dependency checker ──────────────────────────────────────────────────

    [Fact]
    public void Checker_returns_empty_when_all_dependencies_satisfied()
    {
        var failures = global::FlexCms.Framework.Modules.ModuleDependencyChecker.Check(
            ["BlogModule>=1.2.0", "CoreApi"],
            new Dictionary<string, string>
            {
                ["BlogModule"] = "1.5.0",
                ["CoreApi"] = "0.9.0",
            });
        Assert.Empty(failures);
    }

    [Fact]
    public void Checker_flags_missing_module()
    {
        var failures = global::FlexCms.Framework.Modules.ModuleDependencyChecker.Check(
            ["BlogModule>=1.2.0"],
            new Dictionary<string, string>());
        Assert.Single(failures);
        Assert.Contains("not installed", failures[0]);
    }

    [Fact]
    public void Checker_flags_too_old_version()
    {
        var failures = global::FlexCms.Framework.Modules.ModuleDependencyChecker.Check(
            ["BlogModule>=1.2.0"],
            new Dictionary<string, string> { ["BlogModule"] = "1.0.0" });
        Assert.Single(failures);
        Assert.Contains("does not satisfy", failures[0]);
    }
}
