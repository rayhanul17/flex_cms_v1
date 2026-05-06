using FlexCms.Framework.Health;
using FlexCms.Framework.Messaging;
using NSubstitute;
using Xunit;

namespace FlexCms.Tests.Unit.Phase13;

public class BuiltInHealthChecksTests
{
    [Fact]
    public async Task BackgroundQueue_health_returns_healthy_when_below_80pct()
    {
        var q = Substitute.For<IFcmsBackgroundQueue>();
        q.ApproximateCount.Returns(10);
        var opts = new FcmsBackgroundQueueOptions { Capacity = 100 };
        var check = new BackgroundQueueHealthCheck(q, opts);

        var r = await check.CheckAsync();
        Assert.Equal(HealthStatus.Healthy, r.Status);
    }

    [Fact]
    public async Task BackgroundQueue_health_returns_degraded_above_80pct()
    {
        var q = Substitute.For<IFcmsBackgroundQueue>();
        q.ApproximateCount.Returns(85);
        var opts = new FcmsBackgroundQueueOptions { Capacity = 100 };
        var check = new BackgroundQueueHealthCheck(q, opts);

        var r = await check.CheckAsync();
        Assert.Equal(HealthStatus.Degraded, r.Status);
    }

    [Fact]
    public async Task BackgroundQueue_health_returns_degraded_at_capacity()
    {
        var q = Substitute.For<IFcmsBackgroundQueue>();
        q.ApproximateCount.Returns(100);
        var opts = new FcmsBackgroundQueueOptions { Capacity = 100 };
        var check = new BackgroundQueueHealthCheck(q, opts);

        var r = await check.CheckAsync();
        Assert.Equal(HealthStatus.Degraded, r.Status);
        Assert.Contains("capacity", r.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BackgroundQueue_check_is_excluded_from_readiness_roll_up()
    {
        var q = Substitute.For<IFcmsBackgroundQueue>();
        var check = new BackgroundQueueHealthCheck(q, new FcmsBackgroundQueueOptions());
        Assert.False(check.IncludeInReadiness);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task DiskSpace_check_returns_healthy_for_normal_drive()
    {
        var check = new DiskSpaceHealthCheck(Path.GetTempPath());
        var r = await check.CheckAsync();
        // On any test machine the temp drive should have >500MB free.
        Assert.Equal(HealthStatus.Healthy, r.Status);
    }
}
