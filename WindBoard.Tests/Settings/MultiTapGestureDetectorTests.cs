using WindBoard.Settings;
using Xunit;

namespace WindBoard.Tests.Settings;

public sealed class MultiTapGestureDetectorTests
{
    [Fact]
    public void RegisterTap_Should_Trigger_OnRequiredTapsWithinInterval()
    {
        var detector = new MultiTapGestureDetector(requiredTaps: 5, maxInterval: TimeSpan.FromMilliseconds(800));
        DateTimeOffset t0 = new(2026, 2, 15, 0, 0, 0, TimeSpan.Zero);

        Assert.False(detector.RegisterTap(t0));
        Assert.False(detector.RegisterTap(t0.AddMilliseconds(100)));
        Assert.False(detector.RegisterTap(t0.AddMilliseconds(200)));
        Assert.False(detector.RegisterTap(t0.AddMilliseconds(300)));
        Assert.True(detector.RegisterTap(t0.AddMilliseconds(400)));

        // 触发后会自动重置：下一次应从头计数。
        Assert.False(detector.RegisterTap(t0.AddMilliseconds(500)));
    }

    [Fact]
    public void RegisterTap_Should_Reset_WhenIntervalExceeded()
    {
        var detector = new MultiTapGestureDetector(requiredTaps: 3, maxInterval: TimeSpan.FromMilliseconds(800));
        DateTimeOffset t0 = new(2026, 2, 15, 0, 0, 0, TimeSpan.Zero);

        Assert.False(detector.RegisterTap(t0));

        // 超过窗口：应重置为第 1 次而不是第 2 次。
        Assert.False(detector.RegisterTap(t0.AddMilliseconds(900)));
        Assert.False(detector.RegisterTap(t0.AddMilliseconds(1000)));
        Assert.True(detector.RegisterTap(t0.AddMilliseconds(1100)));
    }
}

