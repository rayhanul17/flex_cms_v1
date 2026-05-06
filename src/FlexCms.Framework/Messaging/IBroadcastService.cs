namespace FlexCms.Framework.Messaging;

public enum BroadcastTarget
{
    AllUsers = 0,
    ByRole = 1,
    Selected = 2
}

public record BroadcastRequest(
    MessageChannel Channel,
    BroadcastTarget Target,
    string Subject,
    string Body,
    bool IsHtml = true,
    string? RoleName = null,
    IReadOnlyList<Guid>? UserIds = null);

public record BroadcastResult(Guid BroadcastId, int Enqueued)
{
    public static BroadcastResult Empty => new(Guid.Empty, 0);
}

/// <summary>
/// Resolves a recipient list (all / by-role / selected) and inserts one
/// <see cref="FcmsPendingMessage"/> per recipient. Messages are picked up by
/// <see cref="MessageProcessorService"/> on its next poll — the broadcast call
/// itself never blocks on actual delivery, so admin pages stay snappy and
/// failures don't cascade into the request handler.
/// </summary>
public interface IBroadcastService
{
    Task<BroadcastResult> SendAsync(BroadcastRequest request, CancellationToken ct = default);
}
