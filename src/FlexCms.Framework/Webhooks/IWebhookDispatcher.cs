namespace FlexCms.Framework.Webhooks;

public interface IWebhookDispatcher
{
    /// <summary>
    /// Find every active endpoint subscribed to <paramref name="eventName"/>,
    /// insert a <see cref="FcmsWebhookDelivery"/> row per endpoint, and
    /// attempt the POST. Failed attempts get retried by the same method on
    /// subsequent calls (the delivery row carries the attempt count).
    /// </summary>
    Task<int> FireAsync(string eventName, object payload, CancellationToken ct = default);

    /// <summary>Drain the failed-but-retriable backlog. Run periodically by the host.</summary>
    Task RetryFailedAsync(CancellationToken ct = default);
}
