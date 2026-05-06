using FlexCms.Framework.Db.Ef;

namespace FlexCms.Framework.Newsletters;

public enum SubscriberStatus
{
    PendingVerification = 0,
    Active = 1,
    Unsubscribed = 2,
    Bounced = 3
}

/// <summary>
/// Newsletter subscriber. Double opt-in flow: user signs up →
/// <see cref="PendingVerification"/> + verification email enqueued →
/// click link → <see cref="Active"/>. Unsubscribe is a no-login one-click
/// link (the token in the URL identifies the row).
/// </summary>
public class FcmsSubscriber : BaseEfEntity
{
    public string Email { get; set; } = "";
    public string? Name { get; set; }

    public SubscriberStatus SubscriberStatus { get; set; } = SubscriberStatus.PendingVerification;

    /// <summary>Random token used in both the verify and unsubscribe URLs. Stable for the row's lifetime.</summary>
    public string Token { get; set; } = "";

    public DateTime? VerifiedAt { get; set; }
    public DateTime? UnsubscribedAt { get; set; }
}
