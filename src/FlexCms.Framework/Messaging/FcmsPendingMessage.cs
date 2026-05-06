using FlexCms.Framework.Db.Ef;

namespace FlexCms.Framework.Messaging;

public enum MessageChannel
{
    Email = 0,
    Sms = 1
}

public enum MessageDeliveryStatus
{
    Pending = 0,
    Sent = 1,
    Failed = 2
}

/// <summary>
/// Restart-safe outgoing message. Inserted by <see cref="BroadcastService"/> or
/// any feature that needs delivery survival across crashes/restarts. Drained by
/// <see cref="MessageProcessorService"/> on a 30-second loop with up to
/// <c>MaxRetries</c> attempts before marking <see cref="MessageDeliveryStatus.Failed"/>.
/// <para>
/// Delivery state lives on <see cref="DeliveryStatus"/> rather than reusing the
/// inherited row-lifecycle <see cref="BaseEfEntity.Status"/> — the two
/// concepts are independent (a soft-deleted row can still be Pending if the
/// admin trashed a broadcast mid-send).
/// </para>
/// </summary>
public class FcmsPendingMessage : BaseEfEntity
{
    public MessageChannel Channel { get; set; }

    /// <summary>Recipient address — email for <see cref="MessageChannel.Email"/>, phone for <see cref="MessageChannel.Sms"/>.</summary>
    public string To { get; set; } = "";

    /// <summary>Used only for email. Empty for SMS.</summary>
    public string Subject { get; set; } = "";

    public string Body { get; set; } = "";
    public bool IsHtml { get; set; }

    public MessageDeliveryStatus DeliveryStatus { get; set; } = MessageDeliveryStatus.Pending;
    public int RetryCount { get; set; }
    public DateTime? LastAttemptAt { get; set; }
    public string? LastError { get; set; }

    /// <summary>Optional broadcast grouping — multiple rows from one admin "send" share this id.</summary>
    public Guid? BroadcastId { get; set; }
}
