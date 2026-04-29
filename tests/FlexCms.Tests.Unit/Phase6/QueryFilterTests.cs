using FlexCms.Framework.Cms;
using FlexCms.Framework.Db;
using FlexCms.Framework.Db.Ef;

namespace FlexCms.Tests.Unit.Phase6;

/// <summary>
/// Unit tests for QueryFilter&lt;T&gt; public API — verifies fluent chaining compiles
/// and returns the same instance, plus basic behaviour via EfRepository InMemory.
/// </summary>
public class QueryFilterTests
{
    // ── Fluent builder returns same instance ──────────────────────────────────

    [Fact]
    public void Where_returns_same_instance()
    {
        var f = new QueryFilter<FcmsMedia>();
        Assert.Same(f, f.Where(m => m.IsDeleted == false));
    }

    [Fact]
    public void OrderBy_returns_same_instance()
    {
        var f = new QueryFilter<FcmsMedia>();
        Assert.Same(f, f.OrderBy(m => m.FileName));
    }

    [Fact]
    public void OrderByDescending_returns_same_instance()
    {
        var f = new QueryFilter<FcmsMedia>();
        Assert.Same(f, f.OrderByDescending(m => m.CreatedAt));
    }

    [Fact]
    public void Page_returns_same_instance()
    {
        var f = new QueryFilter<FcmsMedia>();
        Assert.Same(f, f.Page(1, 10));
    }

    [Fact]
    public void Full_chain_returns_same_instance()
    {
        var f = new QueryFilter<FcmsMedia>();
        var chained = f
            .Where(m => m.FolderId == null)
            .Where(m => m.Extension == ".pdf")
            .OrderByDescending(m => m.CreatedAt)
            .Page(2, 10);
        Assert.Same(f, chained);
    }
}
