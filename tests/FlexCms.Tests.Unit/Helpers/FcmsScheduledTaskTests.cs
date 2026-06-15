using FlexCms.Framework.Hosting;

namespace FlexCms.Tests.Unit.Helpers;

public class FcmsScheduledTaskTests
{
    [Fact]
    public void EveryMinute_fires_at_any_minute()
    {
        var task = new FcmsScheduledTask(Cron.EveryMinute);
        Assert.True(task.ShouldRun(new DateTime(2026, 6, 15, 10, 0, 0)));
    }

    [Fact]
    public void ShouldRun_returns_false_when_called_twice_in_the_same_minute()
    {
        var task = new FcmsScheduledTask(Cron.EveryMinute);
        var t = new DateTime(2026, 6, 15, 10, 0, 30);
        Assert.True(task.ShouldRun(t));
        Assert.False(task.ShouldRun(t.AddSeconds(15)));
    }

    [Fact]
    public void DailyAt_fires_only_at_the_configured_time()
    {
        var task = new FcmsScheduledTask(Cron.DailyAt(hour: 2, minute: 30));
        Assert.True(task.ShouldRun(new DateTime(2026, 6, 15, 2, 30, 0)));

        var task2 = new FcmsScheduledTask(Cron.DailyAt(hour: 2, minute: 30));
        Assert.False(task2.ShouldRun(new DateTime(2026, 6, 15, 2, 29, 0)));

        var task3 = new FcmsScheduledTask(Cron.DailyAt(hour: 2, minute: 30));
        Assert.False(task3.ShouldRun(new DateTime(2026, 6, 15, 3, 30, 0)));
    }

    [Fact]
    public void HourlyAtMinute_matches_only_at_specified_minute()
    {
        var task = new FcmsScheduledTask(Cron.HourlyAtMinute(15));
        Assert.True(task.ShouldRun(new DateTime(2026, 6, 15, 7, 15, 0)));

        var t2 = new FcmsScheduledTask(Cron.HourlyAtMinute(15));
        Assert.False(t2.ShouldRun(new DateTime(2026, 6, 15, 7, 10, 0)));
    }

    [Fact]
    public void EveryFiveMin_fires_at_each_five_minute_step()
    {
        var task = new FcmsScheduledTask(Cron.EveryFiveMin);
        Assert.True(task.ShouldRun(new DateTime(2026, 6, 15, 10, 0, 0)));

        var t2 = new FcmsScheduledTask(Cron.EveryFiveMin);
        Assert.True(t2.ShouldRun(new DateTime(2026, 6, 15, 10, 5, 0)));

        var t3 = new FcmsScheduledTask(Cron.EveryFiveMin);
        Assert.False(t3.ShouldRun(new DateTime(2026, 6, 15, 10, 7, 0)));
    }

    [Fact]
    public void WeeklyAt_fires_only_on_correct_day()
    {
        // Monday = 1
        var task = new FcmsScheduledTask(Cron.WeeklyAt(dayOfWeek: 1, hour: 9, minute: 0));
        // 2026-06-15 is a Monday
        Assert.True(task.ShouldRun(new DateTime(2026, 6, 15, 9, 0, 0)));

        var t2 = new FcmsScheduledTask(Cron.WeeklyAt(dayOfWeek: 1, hour: 9, minute: 0));
        // Tuesday
        Assert.False(t2.ShouldRun(new DateTime(2026, 6, 16, 9, 0, 0)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("* * *")]
    [InlineData("60 * * * *")]   // minute out of range
    [InlineData("* 25 * * *")]   // hour out of range
    [InlineData("* * 0 * *")]    // day below range
    public void Invalid_expressions_throw(string expr)
    {
        Assert.Throws<ArgumentException>(() => new FcmsScheduledTask(expr));
    }

    [Fact]
    public void Reset_clears_the_once_per_minute_guard()
    {
        var task = new FcmsScheduledTask(Cron.EveryMinute);
        var t = new DateTime(2026, 6, 15, 10, 0, 0);
        Assert.True(task.ShouldRun(t));
        Assert.False(task.ShouldRun(t));
        task.Reset();
        Assert.True(task.ShouldRun(t));
    }

    [Fact]
    public void Range_in_minute_field_matches_inclusive_endpoints()
    {
        var task = new FcmsScheduledTask("10-12 * * * *");
        Assert.True(task.ShouldRun(new DateTime(2026, 6, 15, 0, 10, 0)));

        var t2 = new FcmsScheduledTask("10-12 * * * *");
        Assert.True(t2.ShouldRun(new DateTime(2026, 6, 15, 0, 12, 0)));

        var t3 = new FcmsScheduledTask("10-12 * * * *");
        Assert.False(t3.ShouldRun(new DateTime(2026, 6, 15, 0, 13, 0)));
    }

    [Fact]
    public void List_in_minute_field_matches_each_entry()
    {
        var task = new FcmsScheduledTask("0,15,30,45 * * * *");
        foreach (var m in new[] { 0, 15, 30, 45 })
        {
            var t = new FcmsScheduledTask("0,15,30,45 * * * *");
            Assert.True(t.ShouldRun(new DateTime(2026, 6, 15, 10, m, 0)),
                $"Expected match at minute {m}");
        }
    }
}
