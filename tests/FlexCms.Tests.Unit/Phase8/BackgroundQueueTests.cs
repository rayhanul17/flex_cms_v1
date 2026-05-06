using FlexCms.Framework.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FlexCms.Tests.Unit.Phase8;

/// <summary>
/// Verifies the bounded-channel queue behaviour and the processor's
/// scope/error semantics. The processor runs as a BackgroundService so the
/// tests start it explicitly and stop it when assertions are done.
/// </summary>
public class BackgroundQueueTests
{
    [Fact]
    public void TryEnqueue_returns_true_within_capacity_and_false_when_full()
    {
        var q = new FcmsBackgroundQueue(new FcmsBackgroundQueueOptions { Capacity = 2 });

        Assert.True(q.TryEnqueue((_, _) => Task.CompletedTask));
        Assert.True(q.TryEnqueue((_, _) => Task.CompletedTask));
        Assert.False(q.TryEnqueue((_, _) => Task.CompletedTask));
    }

    [Fact]
    public void TryEnqueue_rejects_null_work()
    {
        var q = new FcmsBackgroundQueue(new FcmsBackgroundQueueOptions { Capacity = 1 });
        Assert.False(q.TryEnqueue(null!));
    }

    [Fact]
    public async Task Processor_drains_enqueued_work_and_creates_a_scope_per_item()
    {
        var sc = new ServiceCollection();
        sc.AddScoped<MarkerService>();
        var sp = sc.BuildServiceProvider();
        var scopes = sp.GetRequiredService<IServiceScopeFactory>();

        var q = new FcmsBackgroundQueue(new FcmsBackgroundQueueOptions { Capacity = 10 });
#pragma warning disable CA2000
        var processor = new FcmsQueueProcessor(q, scopes, NullLogger<FcmsQueueProcessor>.Instance);
#pragma warning restore CA2000

        var seenIds = new List<Guid>();
#pragma warning disable CA2000
        var sync = new SemaphoreSlim(0);
#pragma warning restore CA2000

        q.TryEnqueue((sp1, _) => { seenIds.Add(sp1.GetRequiredService<MarkerService>().Id); sync.Release(); return Task.CompletedTask; });
        q.TryEnqueue((sp2, _) => { seenIds.Add(sp2.GetRequiredService<MarkerService>().Id); sync.Release(); return Task.CompletedTask; });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await ((IHostedService)processor).StartAsync(cts.Token);

        await sync.WaitAsync(cts.Token);
        await sync.WaitAsync(cts.Token);

        await ((IHostedService)processor).StopAsync(CancellationToken.None);

        // Two unique scopes → two unique MarkerService ids
        Assert.Equal(2, seenIds.Count);
        Assert.NotEqual(seenIds[0], seenIds[1]);
    }

    [Fact]
    public async Task Processor_swallows_exceptions_so_one_bad_item_doesnt_kill_the_pump()
    {
        var sp = new ServiceCollection().BuildServiceProvider();
        var scopes = sp.GetRequiredService<IServiceScopeFactory>();
        var q = new FcmsBackgroundQueue(new FcmsBackgroundQueueOptions { Capacity = 5 });
#pragma warning disable CA2000
        var processor = new FcmsQueueProcessor(q, scopes, NullLogger<FcmsQueueProcessor>.Instance);
#pragma warning restore CA2000

        var second = new TaskCompletionSource();
        q.TryEnqueue((_, _) => throw new InvalidOperationException("boom"));
        q.TryEnqueue((_, _) => { second.TrySetResult(); return Task.CompletedTask; });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await ((IHostedService)processor).StartAsync(cts.Token);

        // Second item must run despite first one throwing
        var done = await Task.WhenAny(second.Task, Task.Delay(3000, cts.Token));
        Assert.Same(second.Task, done);

        await ((IHostedService)processor).StopAsync(CancellationToken.None);
    }

    private sealed class MarkerService { public Guid Id { get; } = Guid.NewGuid(); }
}
