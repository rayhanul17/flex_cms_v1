using FlexCms.Framework.Chat;
using FlexCms.Framework.Db;
using FlexCms.Framework.Db.Ef;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FlexCms.Tests.Integration.Phase10;

/// <summary>
/// ChatService against EF in-memory: thread state machine
/// (open / start-new / resolve), message append + thread metadata bump,
/// recent-list ordering, mark-read directionality.
/// </summary>
public sealed class ChatServiceTests : IDisposable
{
    private readonly FcmsDbContext _db;
    private readonly ChatService _svc;

    public ChatServiceTests()
    {
        var opts = new DbContextOptionsBuilder<FcmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new FcmsDbContext(opts);
#pragma warning disable CA2000
        _svc = new ChatService(
            new EfRepository<FcmsChatThread>(_db),
            new EfRepository<FcmsChatMessage>(_db),
            new EfUnitOfWork(_db));
#pragma warning restore CA2000
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task GetOrCreateOpenThreadAsync_creates_when_none_exists_then_returns_existing()
    {
        var u = Guid.NewGuid();
        var first = await _svc.GetOrCreateOpenThreadAsync(u, "Alice");
        var second = await _svc.GetOrCreateOpenThreadAsync(u, "Alice");

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, await _db.ChatThreads.CountAsync());
        Assert.Equal(ChatThreadStatus.Open, first.ThreadStatus);
    }

    [Fact]
    public async Task StartNewThreadAsync_closes_open_and_creates_fresh()
    {
        var u = Guid.NewGuid();
        var orig = await _svc.GetOrCreateOpenThreadAsync(u, "Alice");

        var fresh = await _svc.StartNewThreadAsync(u, "Alice");

        Assert.NotEqual(orig.Id, fresh.Id);
        Assert.Equal(ChatThreadStatus.Open, fresh.ThreadStatus);

        // Original thread must now be Closed
        var origReloaded = await _db.ChatThreads.AsNoTracking().FirstAsync(t => t.Id == orig.Id);
        Assert.Equal(ChatThreadStatus.Closed, origReloaded.ThreadStatus);
        Assert.Equal(2, await _db.ChatThreads.CountAsync());
    }

    [Fact]
    public async Task AddMessageAsync_inserts_message_and_bumps_thread_preview()
    {
        var u = Guid.NewGuid();
        var t = await _svc.GetOrCreateOpenThreadAsync(u, "Alice");

        var m = await _svc.AddMessageAsync(new FcmsChatMessage
        {
            ThreadId = t.Id,
            SenderUserId = u,
            SenderRole = ChatSenderRole.User,
            SenderDisplayName = "Alice",
            Body = "Hello there"
        });

        Assert.NotEqual(Guid.Empty, m.Id);
        var reloaded = await _db.ChatThreads.AsNoTracking().FirstAsync(x => x.Id == t.Id);
        Assert.NotNull(reloaded.LastMessageAt);
        Assert.Equal("Hello there", reloaded.LastMessagePreview);
    }

    [Fact]
    public async Task AddMessageAsync_long_body_is_trimmed_to_120_chars_for_preview()
    {
        var u = Guid.NewGuid();
        var t = await _svc.GetOrCreateOpenThreadAsync(u, "Alice");
        var longBody = new string('x', 200);

        await _svc.AddMessageAsync(new FcmsChatMessage
        {
            ThreadId = t.Id,
            SenderUserId = u,
            SenderRole = ChatSenderRole.User,
            SenderDisplayName = "Alice",
            Body = longBody
        });

        var reloaded = await _db.ChatThreads.AsNoTracking().FirstAsync(x => x.Id == t.Id);
        Assert.Equal(121, reloaded.LastMessagePreview!.Length);   // 120 + ellipsis
        Assert.EndsWith("…", reloaded.LastMessagePreview);
    }

    [Fact]
    public async Task GetMessagesAsync_returns_oldest_first()
    {
        var u = Guid.NewGuid();
        var t = await _svc.GetOrCreateOpenThreadAsync(u, "Alice");

        await _svc.AddMessageAsync(new FcmsChatMessage { ThreadId = t.Id, SenderUserId = u, SenderRole = ChatSenderRole.User, Body = "1" });
        await Task.Delay(5);   // ensure CreatedAt ordering is deterministic
        await _svc.AddMessageAsync(new FcmsChatMessage { ThreadId = t.Id, SenderUserId = u, SenderRole = ChatSenderRole.User, Body = "2" });

        var msgs = await _svc.GetMessagesAsync(t.Id);
        Assert.Equal("1", msgs[0].Body);
        Assert.Equal("2", msgs[1].Body);
    }

    [Fact]
    public async Task MarkReadAsync_user_marks_admin_messages_only()
    {
        var u = Guid.NewGuid();
        var admin = Guid.NewGuid();
        var t = await _svc.GetOrCreateOpenThreadAsync(u, "Alice");
        await _svc.AddMessageAsync(new FcmsChatMessage { ThreadId = t.Id, SenderUserId = u, SenderRole = ChatSenderRole.User, Body = "user1" });
        await _svc.AddMessageAsync(new FcmsChatMessage { ThreadId = t.Id, SenderUserId = admin, SenderRole = ChatSenderRole.Admin, Body = "admin1" });
        await _svc.AddMessageAsync(new FcmsChatMessage { ThreadId = t.Id, SenderUserId = admin, SenderRole = ChatSenderRole.Admin, Body = "admin2" });

        var n = await _svc.MarkReadAsync(t.Id, ChatSenderRole.User);

        Assert.Equal(2, n);
        var msgs = await _db.ChatMessages.AsNoTracking().Where(m => m.ThreadId == t.Id).ToListAsync();
        Assert.True(msgs.Where(m => m.SenderRole == ChatSenderRole.Admin).All(m => m.IsRead));
        Assert.False(msgs.First(m => m.SenderRole == ChatSenderRole.User).IsRead);
    }

    [Fact]
    public async Task ResolveThreadAsync_flips_status_and_records_resolver()
    {
        var u = Guid.NewGuid();
        var admin = Guid.NewGuid();
        var t = await _svc.GetOrCreateOpenThreadAsync(u, "Alice");

        await _svc.ResolveThreadAsync(t.Id, admin);

        var reloaded = await _db.ChatThreads.AsNoTracking().FirstAsync(x => x.Id == t.Id);
        Assert.Equal(ChatThreadStatus.Resolved, reloaded.ThreadStatus);
        Assert.Equal(admin, reloaded.ResolvedByUserId);
        Assert.NotNull(reloaded.ResolvedAt);
    }

    [Fact]
    public async Task GetRecentThreadsAsync_orders_by_LastMessageAt_desc_capped_at_max()
    {
        for (int i = 0; i < 5; i++)
        {
            var t = await _svc.GetOrCreateOpenThreadAsync(Guid.NewGuid(), $"User{i}");
            await _svc.AddMessageAsync(new FcmsChatMessage { ThreadId = t.Id, SenderRole = ChatSenderRole.User, Body = $"msg{i}" });
            await Task.Delay(5);
        }

        var recent = await _svc.GetRecentThreadsAsync(3);
        Assert.Equal(3, recent.Count);
        // newest user-N must be first
        Assert.Equal("User4", recent[0].UserDisplayName);
    }
}
