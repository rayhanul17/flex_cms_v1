using FlexCms.Framework.Caching;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace FlexCms.Tests.Unit.Phase6;

public class FcmsGroupCacheServiceTests
{
    private static FcmsGroupCacheService Build()
        => new(new MemoryCache(new MemoryCacheOptions()));

    // ── Get / Set ─────────────────────────────────────────────────────────────

    [Fact]
    public void Get_returns_null_on_cache_miss()
    {
        var svc = Build();
        var result = svc.Get<string>("grp", "missing");
        Assert.Null(result);
    }

    [Fact]
    public void Get_returns_stored_value_on_hit()
    {
        var svc = Build();
        svc.Set("grp", "k", "hello", TimeSpan.FromMinutes(5));
        Assert.Equal("hello", svc.Get<string>("grp", "k"));
    }

    [Fact]
    public void Set_then_Get_roundtrips_complex_type()
    {
        var svc = Build();
        var obj = new List<int> { 1, 2, 3 };
        svc.Set("grp", "list", obj, TimeSpan.FromMinutes(5));
        var result = svc.Get<List<int>>("grp", "list");
        Assert.NotNull(result);
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void Same_key_different_groups_do_not_collide()
    {
        var svc = Build();
        svc.Set("group1", "key", "value-one", TimeSpan.FromMinutes(5));
        svc.Set("group2", "key", "value-two", TimeSpan.FromMinutes(5));

        Assert.Equal("value-one", svc.Get<string>("group1", "key"));
        Assert.Equal("value-two", svc.Get<string>("group2", "key"));
    }

    [Fact]
    public void Get_returns_default_for_value_type_on_miss()
    {
        var svc = Build();
        var result = svc.Get<int?>("grp", "missing");
        Assert.Null(result);
    }

    // ── Invalidate single key ─────────────────────────────────────────────────

    [Fact]
    public void Invalidate_removes_specific_key()
    {
        var svc = Build();
        svc.Set("grp", "a", "aa", TimeSpan.FromMinutes(5));
        svc.Set("grp", "b", "bb", TimeSpan.FromMinutes(5));

        svc.Invalidate("grp", "a");

        Assert.Null(svc.Get<string>("grp", "a"));
        Assert.Equal("bb", svc.Get<string>("grp", "b"));
    }

    [Fact]
    public void Invalidate_nonexistent_key_does_not_throw()
    {
        var svc = Build();
        var ex = Record.Exception(() => svc.Invalidate("grp", "no-such-key"));
        Assert.Null(ex);
    }

    [Fact]
    public void Invalidate_only_removes_own_group_key()
    {
        var svc = Build();
        svc.Set("g1", "k", "v1", TimeSpan.FromMinutes(5));
        svc.Set("g2", "k", "v2", TimeSpan.FromMinutes(5));

        svc.Invalidate("g1", "k");

        Assert.Null(svc.Get<string>("g1", "k"));
        Assert.Equal("v2", svc.Get<string>("g2", "k"));
    }

    // ── InvalidateGroup ───────────────────────────────────────────────────────

    [Fact]
    public void InvalidateGroup_removes_all_keys_in_group()
    {
        var svc = Build();
        svc.Set("grp", "x", "1", TimeSpan.FromMinutes(5));
        svc.Set("grp", "y", "2", TimeSpan.FromMinutes(5));
        svc.Set("grp", "z", "3", TimeSpan.FromMinutes(5));

        svc.InvalidateGroup("grp");

        Assert.Null(svc.Get<string>("grp", "x"));
        Assert.Null(svc.Get<string>("grp", "y"));
        Assert.Null(svc.Get<string>("grp", "z"));
    }

    [Fact]
    public void InvalidateGroup_does_not_affect_other_groups()
    {
        var svc = Build();
        svc.Set("target", "k", "gone", TimeSpan.FromMinutes(5));
        svc.Set("other",  "k", "kept", TimeSpan.FromMinutes(5));

        svc.InvalidateGroup("target");

        Assert.Null(svc.Get<string>("target", "k"));
        Assert.Equal("kept", svc.Get<string>("other", "k"));
    }

    [Fact]
    public void InvalidateGroup_nonexistent_group_does_not_throw()
    {
        var svc = Build();
        var ex = Record.Exception(() => svc.InvalidateGroup("no-such-group"));
        Assert.Null(ex);
    }

    [Fact]
    public void After_InvalidateGroup_new_values_in_same_group_are_stored()
    {
        var svc = Build();
        svc.Set("grp", "k", "old", TimeSpan.FromMinutes(5));
        svc.InvalidateGroup("grp");

        svc.Set("grp", "k", "new", TimeSpan.FromMinutes(5));
        Assert.Equal("new", svc.Get<string>("grp", "k"));
    }

    // ── InvalidateAll ─────────────────────────────────────────────────────────

    [Fact]
    public void InvalidateAll_clears_all_groups()
    {
        var svc = Build();
        svc.Set("settings",    "site",  "s", TimeSpan.FromMinutes(5));
        svc.Set("permissions", "perm1", "p", TimeSpan.FromMinutes(5));
        svc.Set("menu",        "admin", "m", TimeSpan.FromMinutes(5));

        svc.InvalidateAll();

        Assert.Null(svc.Get<string>("settings",    "site"));
        Assert.Null(svc.Get<string>("permissions", "perm1"));
        Assert.Null(svc.Get<string>("menu",        "admin"));
    }

    [Fact]
    public void InvalidateAll_on_empty_cache_does_not_throw()
    {
        var svc = Build();
        var ex = Record.Exception(() => svc.InvalidateAll());
        Assert.Null(ex);
    }

    [Fact]
    public void After_InvalidateAll_cache_accepts_new_entries()
    {
        var svc = Build();
        svc.Set("grp", "k", "v", TimeSpan.FromMinutes(5));
        svc.InvalidateAll();

        svc.Set("grp", "k2", "v2", TimeSpan.FromMinutes(5));
        Assert.Equal("v2", svc.Get<string>("grp", "k2"));
    }

    // ── TTL ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Expired_entry_is_not_returned()
    {
        var svc = Build();
        svc.Set("grp", "k", "v", TimeSpan.FromMilliseconds(1));
        Thread.Sleep(50);
        Assert.Null(svc.Get<string>("grp", "k"));
    }

    // ── Multiple sets to same key ─────────────────────────────────────────────

    [Fact]
    public void Second_Set_overwrites_first_value()
    {
        var svc = Build();
        svc.Set("grp", "k", "first",  TimeSpan.FromMinutes(5));
        svc.Set("grp", "k", "second", TimeSpan.FromMinutes(5));
        Assert.Equal("second", svc.Get<string>("grp", "k"));
    }
}
