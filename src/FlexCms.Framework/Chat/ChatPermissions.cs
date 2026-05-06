namespace FlexCms.Framework.Chat;

/// <summary>
/// Permission keys for the chat module. Both keys are seeded by
/// <c>SeedService</c> alongside the rest of the core permission catalog.
/// </summary>
public static class ChatPermissions
{
    /// <summary>End-user permission to send chat messages. Without it the FAB widget renders nothing.</summary>
    public const string Send = "chat.send";

    /// <summary>Admin permission to reply to / resolve chat threads. Without it <see cref="ChatHub"/> rejects calls with <c>HubException</c>.</summary>
    public const string Reply = "chat.reply";
}
