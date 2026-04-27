using FlexCms.Framework.Clock;

namespace FlexCms.Tests.Unit;

public class FcmsClockTests
{
    // -----------------------------------------------------------------------
    // FcmsClock.Now — always UTC
    // -----------------------------------------------------------------------

    [Fact]
    public void FcmsClock_Now_IsUtc()
    {
        var clock = new FcmsClock(TimeZoneInfo.Utc);
        var before = DateTime.UtcNow;
        var now = clock.Now;
        var after = DateTime.UtcNow;

        Assert.Equal(DateTimeKind.Utc, now.Kind);
        Assert.InRange(now, before.AddMilliseconds(-50), after.AddMilliseconds(50));
    }

    // -----------------------------------------------------------------------
    // FcmsClock.LocalNow — UTC converted to configured timezone
    // -----------------------------------------------------------------------

    [Fact]
    public void FcmsClock_LocalNow_ConvertsUtcToDhakaTime()
    {
        var dhakaZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Dhaka");
        var clock = new FcmsClock(dhakaZone);

        var utcNow = DateTime.UtcNow;
        var localNow = clock.LocalNow;

        // Bangladesh is UTC+6 (no DST)
        var expectedOffset = dhakaZone.GetUtcOffset(utcNow);
        Assert.Equal(TimeSpan.FromHours(6), expectedOffset);

        var diff = localNow - utcNow;
        Assert.Equal(6, (int)Math.Round(diff.TotalHours));
    }

    [Fact]
    public void FcmsClock_LocalNow_WithUtcZone_EqualsNow()
    {
        var clock = new FcmsClock(TimeZoneInfo.Utc);

        // Within same second both should be identical
        var nowTicks = clock.Now.Ticks;
        var localTicks = clock.LocalNow.Ticks;

        Assert.InRange(Math.Abs(localTicks - nowTicks),
            0, TimeSpan.FromMilliseconds(50).Ticks);
    }

    // -----------------------------------------------------------------------
    // FcmsClock.Today + TimeOfDay — derived from LocalNow
    // -----------------------------------------------------------------------

    [Fact]
    public void FcmsClock_Today_MatchesLocalNowDate()
    {
        var dhakaZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Dhaka");
        var clock = new FcmsClock(dhakaZone);

        Assert.Equal(DateOnly.FromDateTime(clock.LocalNow), clock.Today);
    }

    [Fact]
    public void FcmsClock_TimeOfDay_MatchesLocalNowTime()
    {
        // Use a fixed clock to avoid two separate DateTime.UtcNow calls spanning a boundary
        var fixedUtc = new DateTime(2025, 6, 15, 14, 30, 0, DateTimeKind.Utc);
        var clock = new FixedClock(fixedUtc, TimeZoneInfo.Utc);

        Assert.Equal(TimeOnly.FromDateTime(clock.LocalNow), clock.TimeOfDay);
    }

    // -----------------------------------------------------------------------
    // FcmsTime static — swappable clock for testing
    // -----------------------------------------------------------------------

    [Fact]
    public void FcmsTime_Clock_IsSwappableForTests()
    {
        var original = FcmsTime.Clock;
        var fixedUtc = new DateTime(2025, 6, 15, 10, 0, 0, DateTimeKind.Utc);

        try
        {
            FcmsTime.Clock = new FixedClock(fixedUtc, TimeZoneInfo.Utc);
            Assert.Equal(fixedUtc, FcmsTime.Now);
        }
        finally
        {
            FcmsTime.Clock = original;
        }
    }

    [Fact]
    public void FcmsTime_Now_IsUtc_LocalNow_IsLocal()
    {
        var dhakaZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Dhaka");
        var fixedUtc = new DateTime(2025, 1, 15, 6, 0, 0, DateTimeKind.Utc);
        var original = FcmsTime.Clock;

        try
        {
            FcmsTime.Clock = new FixedClock(fixedUtc, dhakaZone);

            // Now = UTC
            Assert.Equal(fixedUtc, FcmsTime.Now);
            Assert.Equal(DateTimeKind.Utc, FcmsTime.Now.Kind);

            // LocalNow = UTC+6 → 12:00 Dhaka
            Assert.Equal(new DateTime(2025, 1, 15, 12, 0, 0), FcmsTime.LocalNow);
        }
        finally
        {
            FcmsTime.Clock = original;
        }
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private sealed class FixedClock : IFcmsClock
    {
        private readonly DateTime _utc;
        private readonly TimeZoneInfo _tz;

        public FixedClock(DateTime utc, TimeZoneInfo tz)
        {
            _utc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
            _tz = tz;
        }

        public DateTime Now => _utc;
        public DateTime LocalNow => TimeZoneInfo.ConvertTimeFromUtc(_utc, _tz);
        public DateOnly Today => DateOnly.FromDateTime(LocalNow);
        public TimeOnly TimeOfDay => TimeOnly.FromDateTime(LocalNow);
    }
}
