using FlexCms.Framework.Db.Ef;

namespace FlexCms.Framework.Webhooks;

public enum WebhookDeliveryStatus
{
    Pending = 0,
    Succeeded = 1,
    Failed = 2
}

/// <summary>
/// Per-attempt log row. One <see cref="FcmsWebhookEndpoint"/> may have many
/// deliveries — both successful and failed. Failed rows are retried up to
/// 3 times; the row's <see cref="DeliveryStatus"/> reflects the final state.
/// </summary>
public class FcmsWebhookDelivery : BaseEfEntity
{
    public Guid EndpointId { get; set; }
    public string EventName { get; set; } = "";
    public string PayloadJson { get; set; } = "";

    public WebhookDeliveryStatus DeliveryStatus { get; set; } = WebhookDeliveryStatus.Pending;
    public int AttemptCount { get; set; }

    public int? LastResponseStatus { get; set; }
    public string? LastResponseBody { get; set; }
    public string? LastError { get; set; }

    public DateTime? LastAttemptAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
