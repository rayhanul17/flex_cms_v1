using FlexCms.Framework.Cms.CustomFields;
using FlexCms.Framework.Db;
using FlexCms.Framework.Db.Ef;
using FlexCms.Framework.Newsletters;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FlexCms.Tests.Integration.Phase14;

public sealed class SubscriberAndCustomFieldTests : IDisposable
{
    private readonly FcmsDbContext _db;
    private readonly SubscriberService _subs;
    private readonly CustomFieldService _meta;

    public SubscriberAndCustomFieldTests()
    {
        var opts = new DbContextOptionsBuilder<FcmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new FcmsDbContext(opts);
#pragma warning disable CA2000
        _subs = new SubscriberService(new EfRepository<FcmsSubscriber>(_db), new EfUnitOfWork(_db));
        _meta = new CustomFieldService(new EfRepository<FcmsContentMeta>(_db), new EfUnitOfWork(_db));
#pragma warning restore CA2000
    }

    public void Dispose() => _db.Dispose();

    // ── Subscriber double opt-in flow ────────────────────────────────────────

    [Fact]
    public async Task SubscribeAsync_creates_pending_row_with_token()
    {
        var sub = await _subs.SubscribeAsync("a@b.c", "Alice");

        Assert.Equal("a@b.c", sub.Email);
        Assert.Equal(SubscriberStatus.PendingVerification, sub.SubscriberStatus);
        Assert.Equal(32, sub.Token.Length);   // 16 bytes hex
    }

    [Fact]
    public async Task VerifyAsync_flips_to_active()
    {
        var sub = await _subs.SubscribeAsync("a@b.c");
        var ok = await _subs.VerifyAsync(sub.Token);

        Assert.True(ok);
        var reloaded = await _db.Subscribers.AsNoTracking().FirstAsync();
        Assert.Equal(SubscriberStatus.Active, reloaded.SubscriberStatus);
        Assert.NotNull(reloaded.VerifiedAt);
    }

    [Fact]
    public async Task UnsubscribeAsync_flips_to_unsubscribed()
    {
        var sub = await _subs.SubscribeAsync("a@b.c");
        await _subs.VerifyAsync(sub.Token);
        await _subs.UnsubscribeAsync(sub.Token);

        var reloaded = await _db.Subscribers.AsNoTracking().FirstAsync();
        Assert.Equal(SubscriberStatus.Unsubscribed, reloaded.SubscriberStatus);
    }

    [Fact]
    public async Task Resubscribe_after_unsubscribe_resets_to_pending_with_new_token()
    {
        var sub = await _subs.SubscribeAsync("a@b.c");
        await _subs.VerifyAsync(sub.Token);
        await _subs.UnsubscribeAsync(sub.Token);
        var firstToken = sub.Token;

        var sub2 = await _subs.SubscribeAsync("a@b.c");

        Assert.Equal(SubscriberStatus.PendingVerification, sub2.SubscriberStatus);
        Assert.NotEqual(firstToken, sub2.Token);
    }

    [Fact]
    public async Task SubscribeAsync_normalizes_email_lowercase()
    {
        var sub = await _subs.SubscribeAsync("Alice@Example.com");
        Assert.Equal("alice@example.com", sub.Email);
    }

    [Fact]
    public async Task GetActiveAsync_returns_only_active_rows()
    {
        var s1 = await _subs.SubscribeAsync("a@x.c");
        await _subs.VerifyAsync(s1.Token);
        await _subs.SubscribeAsync("b@x.c");   // pending

        var active = await _subs.GetActiveAsync();
        Assert.Single(active);
        Assert.Equal("a@x.c", active[0].Email);
    }

    // ── Custom fields ────────────────────────────────────────────────────────

    [Fact]
    public async Task SetAsync_then_GetAsync_round_trips_int_value()
    {
        var entityId = Guid.NewGuid();
        await _meta.SetAsync("FcmsPost", entityId, "ReadingTime", 5);

        var v = await _meta.GetAsync<int>("FcmsPost", entityId, "ReadingTime");
        Assert.Equal(5, v);
    }

    [Fact]
    public async Task SetAsync_overwrites_existing_value_for_same_key()
    {
        var entityId = Guid.NewGuid();
        await _meta.SetAsync("FcmsPost", entityId, "k", "first");
        await _meta.SetAsync("FcmsPost", entityId, "k", "second");

        Assert.Equal("second", await _meta.GetAsync<string>("FcmsPost", entityId, "k"));
        Assert.Equal(1, await _db.ContentMeta.CountAsync());
    }

    [Theory]
    [InlineData(typeof(int), 42)]
    [InlineData(typeof(bool), true)]
    [InlineData(typeof(string), "hello")]
    public async Task SetAsync_handles_primitive_types(Type t, object value)
    {
        var entityId = Guid.NewGuid();
        var setMethod = typeof(CustomFieldService).GetMethods()
            .First(m => m.Name == nameof(CustomFieldService.SetAsync) && m.IsGenericMethod)
            .MakeGenericMethod(t);
        await (Task)setMethod.Invoke(_meta, ["FcmsPost", entityId, "k", value, default(CancellationToken)])!;

        var getMethod = typeof(CustomFieldService).GetMethods()
            .First(m => m.Name == nameof(CustomFieldService.GetAsync) && m.IsGenericMethod)
            .MakeGenericMethod(t);
        var task = (Task)getMethod.Invoke(_meta, ["FcmsPost", entityId, "k", default(CancellationToken)])!;
        await task;
        var resultProp = task.GetType().GetProperty("Result");
        var result = resultProp!.GetValue(task);

        Assert.Equal(value, result);
    }

    [Fact]
    public async Task RemoveAsync_deletes_row()
    {
        var entityId = Guid.NewGuid();
        await _meta.SetAsync("FcmsPost", entityId, "k", "v");
        await _meta.RemoveAsync("FcmsPost", entityId, "k");

        Assert.Equal(0, await _db.ContentMeta.CountAsync());
    }
}
