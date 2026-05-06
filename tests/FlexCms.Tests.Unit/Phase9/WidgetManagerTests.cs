using FlexCms.Framework.Db;
using FlexCms.Framework.Db.Ef;
using FlexCms.Framework.Widgets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FlexCms.Tests.Unit.Phase9;

/// <summary>
/// Verifies the widget rendering contract: zone composition, ordering by
/// SortOrder, disabled rows skipped, unknown widget id silently ignored, and
/// a throwing widget doesn't break the rest of the zone.
/// </summary>
public sealed class WidgetManagerTests : IDisposable
{
    private readonly FcmsDbContext _db;
    private readonly EfRepository<FcmsWidgetPlacement> _repo;
    private readonly EfUnitOfWork _uow;

    public WidgetManagerTests()
    {
        var opts = new DbContextOptionsBuilder<FcmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new FcmsDbContext(opts);
        _repo = new EfRepository<FcmsWidgetPlacement>(_db);
#pragma warning disable CA2000
        _uow = new EfUnitOfWork(_db);
#pragma warning restore CA2000
    }

    public void Dispose()
    {
        // EfUnitOfWork only implements IAsyncDisposable.
        _uow.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _db.Dispose();
    }

    private FcmsWidgetManager Build(IEnumerable<IFcmsWidget> widgets)
    {
        var sp = new ServiceCollection().BuildServiceProvider();
        return new FcmsWidgetManager(_repo, _uow, widgets, sp, NullLogger<FcmsWidgetManager>.Instance);
    }

    private sealed class StaticWidget : IFcmsWidget
    {
        public string Id { get; }
        public string DisplayName { get; }
        public string? Description => null;
        private readonly Func<string?, Task<string>> _render;
        public StaticWidget(string id, Func<string?, Task<string>> render)
        { Id = id; DisplayName = id; _render = render; }
        public Task<string> RenderAsync(string? configJson, IServiceProvider services, CancellationToken ct = default)
            => _render(configJson);
    }

    [Fact]
    public async Task RenderZoneAsync_renders_enabled_placements_in_sort_order()
    {
        var w1 = new StaticWidget("a", _ => Task.FromResult("<a/>"));
        var w2 = new StaticWidget("b", _ => Task.FromResult("<b/>"));
        var mgr = Build([w1, w2]);

        await mgr.AddAsync("a", "Side", sortOrder: 10);
        await mgr.AddAsync("b", "Side", sortOrder: 0);

        var html = await mgr.RenderZoneAsync("Side");
        Assert.Equal("<b/><a/>", html);
    }

    [Fact]
    public async Task RenderZoneAsync_skips_disabled_placements()
    {
        var w1 = new StaticWidget("a", _ => Task.FromResult("<a/>"));
        var mgr = Build([w1]);
        var p = await mgr.AddAsync("a", "Side");
        p.Enabled = false;
        await mgr.UpdateAsync(p);

        var html = await mgr.RenderZoneAsync("Side");
        Assert.Equal("", html);
    }

    [Fact]
    public async Task RenderZoneAsync_silently_skips_unknown_widget_ids()
    {
        var mgr = Build([]);   // no widgets registered
        await mgr.AddAsync("ghost", "Side");

        var html = await mgr.RenderZoneAsync("Side");
        Assert.Equal("", html);   // no exception
    }

    [Fact]
    public async Task RenderZoneAsync_isolates_per_widget_exceptions()
    {
        var bad = new StaticWidget("bad", _ => throw new InvalidOperationException("boom"));
        var good = new StaticWidget("good", _ => Task.FromResult("<g/>"));
        var mgr = Build([bad, good]);

        await mgr.AddAsync("bad", "Side", sortOrder: 0);
        await mgr.AddAsync("good", "Side", sortOrder: 1);

        var html = await mgr.RenderZoneAsync("Side");
        Assert.Equal("<g/>", html);   // bad widget swallowed, good still rendered
    }

    [Fact]
    public async Task ReorderZoneAsync_updates_SortOrder_to_match_input_order()
    {
        var mgr = Build([new StaticWidget("a", _ => Task.FromResult("")),
                         new StaticWidget("b", _ => Task.FromResult("")),
                         new StaticWidget("c", _ => Task.FromResult(""))]);

        var pa = await mgr.AddAsync("a", "Side", sortOrder: 0);
        var pb = await mgr.AddAsync("b", "Side", sortOrder: 1);
        var pc = await mgr.AddAsync("c", "Side", sortOrder: 2);

        await mgr.ReorderZoneAsync("Side", [pc.Id, pa.Id, pb.Id]);

        var rows = (await mgr.GetPlacementsAsync("Side")).OrderBy(p => p.SortOrder).Select(p => p.WidgetId).ToList();
        Assert.Equal(["c", "a", "b"], rows);
    }

    [Fact]
    public async Task DeleteAsync_removes_placement_row()
    {
        var mgr = Build([new StaticWidget("a", _ => Task.FromResult(""))]);
        var p = await mgr.AddAsync("a", "Side");

        await mgr.DeleteAsync(p.Id);

        Assert.Empty(await mgr.GetPlacementsAsync("Side"));
    }

    [Fact]
    public void Get_returns_registered_widget_by_id()
    {
        var w = new StaticWidget("xyz", _ => Task.FromResult(""));
        var mgr = Build([w]);
        Assert.Same(w, mgr.Get("xyz"));
        Assert.Null(mgr.Get("nope"));
    }
}
