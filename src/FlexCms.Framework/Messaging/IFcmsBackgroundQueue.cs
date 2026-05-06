namespace FlexCms.Framework.Messaging;

/// <summary>
/// Fire-and-forget background queue for transient work (single OTP email,
/// single SMS, etc). Backed by <see cref="System.Threading.Channels.Channel"/>
/// — bounded so a runaway producer can't OOM the process. Loss-tolerant by
/// design; for restart-safe delivery use <see cref="FcmsPendingMessage"/>
/// instead and let <see cref="MessageProcessorService"/> drain it.
/// </summary>
public interface IFcmsBackgroundQueue
{
    /// <summary>
    /// Try to enqueue a unit of work. Returns false if the queue is full.
    /// Caller decides how to react — typically log + fall back to direct send
    /// or persist a <see cref="FcmsPendingMessage"/>.
    /// </summary>
    bool TryEnqueue(Func<IServiceProvider, CancellationToken, Task> work);

    /// <summary>Used by the processor to drain. Not for application code.</summary>
    IAsyncEnumerable<Func<IServiceProvider, CancellationToken, Task>> ReadAllAsync(CancellationToken ct);

    /// <summary>Approximate item count — useful for diagnostics + admin dashboards.</summary>
    int ApproximateCount { get; }
}
