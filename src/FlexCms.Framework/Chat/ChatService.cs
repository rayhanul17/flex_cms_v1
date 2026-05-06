using FlexCms.Framework.Db;

namespace FlexCms.Framework.Chat;

public sealed class ChatService : IChatService
{
    private readonly IRepository<FcmsChatThread> _threads;
    private readonly IRepository<FcmsChatMessage> _messages;
    private readonly IFcmsUnitOfWork _uow;

    public ChatService(
        IRepository<FcmsChatThread> threads,
        IRepository<FcmsChatMessage> messages,
        IFcmsUnitOfWork uow)
    {
        _threads = threads;
        _messages = messages;
        _uow = uow;
    }

    public async Task<FcmsChatThread> GetOrCreateOpenThreadAsync(Guid userId, string userDisplayName, CancellationToken ct = default)
    {
        var existing = await _threads.FirstOrDefaultAsync(
            t => t.UserId == userId && t.ThreadStatus == ChatThreadStatus.Open, ct);
        if (existing is not null) return existing;

        var t = new FcmsChatThread
        {
            UserId = userId,
            UserDisplayName = userDisplayName,
            ThreadStatus = ChatThreadStatus.Open
        };
        await _threads.AddAsync(t, ct);
        await _uow.SaveChangesAsync(ct);
        return t;
    }

    public async Task<FcmsChatThread> StartNewThreadAsync(Guid userId, string userDisplayName, CancellationToken ct = default)
    {
        // Close any currently-open thread for this user — only one Open at a time.
        var open = await _threads.FindAsync(t => t.UserId == userId && t.ThreadStatus == ChatThreadStatus.Open, ct);
        foreach (var o in open)
        {
            o.ThreadStatus = ChatThreadStatus.Closed;
            await _threads.UpdateAsync(o, ct);
        }

        var fresh = new FcmsChatThread
        {
            UserId = userId,
            UserDisplayName = userDisplayName,
            ThreadStatus = ChatThreadStatus.Open
        };
        await _threads.AddAsync(fresh, ct);
        await _uow.SaveChangesAsync(ct);
        return fresh;
    }

    public Task<FcmsChatThread?> GetThreadAsync(Guid threadId, CancellationToken ct = default)
        => _threads.GetByIdAsync(threadId, ct);

    public async Task<List<FcmsChatThread>> GetRecentThreadsAsync(int max = 50, CancellationToken ct = default)
    {
        var all = await _threads.FindAsync(t => true, ct);
        return all.OrderByDescending(t => t.LastMessageAt ?? t.CreatedAt).Take(max).ToList();
    }

    public async Task<List<FcmsChatMessage>> GetMessagesAsync(Guid threadId, CancellationToken ct = default)
    {
        var rows = await _messages.FindAsync(m => m.ThreadId == threadId, ct);
        return rows.OrderBy(m => m.CreatedAt).ToList();
    }

    public async Task<FcmsChatMessage> AddMessageAsync(FcmsChatMessage message, CancellationToken ct = default)
    {
        await _messages.AddAsync(message, ct);

        var thread = await _threads.GetByIdAsync(message.ThreadId, ct);
        if (thread is not null)
        {
            thread.LastMessageAt = Clock.FcmsTime.Now;
            thread.LastMessagePreview = TrimPreview(message.Body, 120);
            await _threads.UpdateAsync(thread, ct);
        }

        await _uow.SaveChangesAsync(ct);
        return message;
    }

    public async Task<int> MarkReadAsync(Guid threadId, ChatSenderRole markerRole, CancellationToken ct = default)
    {
        // Mark messages from the OTHER side as read — a user marks admin
        // messages, an admin marks user messages, and a system message is
        // considered already-read so we exclude it from both sides.
        var otherSide = markerRole == ChatSenderRole.User ? ChatSenderRole.Admin : ChatSenderRole.User;

        var unread = await _messages.FindAsync(
            m => m.ThreadId == threadId && m.SenderRole == otherSide && !m.IsRead, ct);
        if (unread.Count == 0) return 0;

        foreach (var m in unread)
        {
            m.IsRead = true;
            await _messages.UpdateAsync(m, ct);
        }
        await _uow.SaveChangesAsync(ct);
        return unread.Count;
    }

    public async Task ResolveThreadAsync(Guid threadId, Guid adminUserId, CancellationToken ct = default)
    {
        var t = await _threads.GetByIdAsync(threadId, ct);
        if (t is null) return;
        t.ThreadStatus = ChatThreadStatus.Resolved;
        t.ResolvedAt = Clock.FcmsTime.Now;
        t.ResolvedByUserId = adminUserId;
        await _threads.UpdateAsync(t, ct);
        await _uow.SaveChangesAsync(ct);
    }

    private static string TrimPreview(string body, int max)
    {
        if (string.IsNullOrEmpty(body)) return "";
        var s = body.Length <= max ? body : body[..max] + "…";
        return s.Replace('\r', ' ').Replace('\n', ' ');
    }
}
