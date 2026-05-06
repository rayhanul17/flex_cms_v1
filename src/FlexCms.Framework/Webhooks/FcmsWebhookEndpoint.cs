using FlexCms.Framework.Db.Ef;

namespace FlexCms.Framework.Webhooks;

/// <summary>
/// Subscriber for outbound webhooks. The framework fires events
/// (<c>post.published</c>, <c>user.registered</c>, etc.); for every active
/// endpoint that lists the event in <see cref="Events"/>, the dispatcher
/// builds an HMAC-signed JSON body and POSTs to <see cref="Url"/>.
/// </summary>
public class FcmsWebhookEndpoint : BaseEfEntity
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";

    /// <summary>Comma-separated list of event names this endpoint subscribes to. Empty = none.</summary>
    public string Events { get; set; } = "";

    /// <summary>Shared secret used to sign delivery payloads. <see cref="WebhookDispatcher"/> stamps the HMAC into the request header.</summary>
    public string Secret { get; set; } = "";

    public bool IsActive { get; set; } = true;
}
