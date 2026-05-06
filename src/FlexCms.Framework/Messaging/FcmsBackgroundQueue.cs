using System.Threading.Channels;

namespace FlexCms.Framework.Messaging;

public sealed class FcmsBackgroundQueueOptions
{
    /// <summary>Capacity ceiling. New work above this limit is rejected. Default 1000.</summary>
    public int Capacity { get; init; } = 1000;
}

/// <summary>
/// Bounded singleton channel. <see cref="FcmsQueueProcessor"/> consumes; any
/// scoped service the work item needs is resolved by creating a child scope
/// inside the processor before invoking the delegate.
/// </summary>
public sealed class FcmsBackgroundQueue : IFcmsBackgroundQueue
{
    private readonly Channel<Func<IServiceProvider, CancellationToken, Task>> _channel;

    public FcmsBackgroundQueue(FcmsBackgroundQueueOptions options)
    {
        _channel = Channel.CreateBounded<Func<IServiceProvider, CancellationToken, Task>>(
            new BoundedChannelOptions(Math.Max(1, options.Capacity))
            {
                // Wait + TryWrite returns false when full → caller knows the
                // enqueue was rejected (rather than silently dropped).
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });
    }

    public bool TryEnqueue(Func<IServiceProvider, CancellationToken, Task> work)
    {
        if (work is null) return false;
        return _channel.Writer.TryWrite(work);
    }

    public IAsyncEnumerable<Func<IServiceProvider, CancellationToken, Task>> ReadAllAsync(CancellationToken ct)
        => _channel.Reader.ReadAllAsync(ct);

    public int ApproximateCount => _channel.Reader.TryPeek(out _) ? _channel.Reader.Count : 0;
}
