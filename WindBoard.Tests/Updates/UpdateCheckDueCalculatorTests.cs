using WindBoard.Settings;
using WindBoard.Updates;

namespace WindBoard.Tests.Updates;

public sealed class UpdateCheckDueCalculatorTests
{
    [Fact]
    public void IsDue_Should_Return_False_For_Never()
    {
        Assert.False(UpdateCheckDueCalculator.IsDue(UpdateCheckInterval.Never, lastCheckUtc: null, nowUtc: DateTimeOffset.UtcNow));
    }

    [Fact]
    public void IsDue_Should_Return_True_When_LastCheck_Is_Null()
    {
        Assert.True(UpdateCheckDueCalculator.IsDue(UpdateCheckInterval.Weekly, lastCheckUtc: null, nowUtc: DateTimeOffset.UtcNow));
    }

    [Fact]
    public void IsDue_Should_Follow_Weekly_Period()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        Assert.False(UpdateCheckDueCalculator.IsDue(UpdateCheckInterval.Weekly, now.AddDays(-6), now));
        Assert.True(UpdateCheckDueCalculator.IsDue(UpdateCheckInterval.Weekly, now.AddDays(-7), now));
    }

    [Fact]
    public void IsDue_Should_Follow_Biweekly_Period()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        Assert.False(UpdateCheckDueCalculator.IsDue(UpdateCheckInterval.Biweekly, now.AddDays(-13), now));
        Assert.True(UpdateCheckDueCalculator.IsDue(UpdateCheckInterval.Biweekly, now.AddDays(-14), now));
    }

    [Fact]
    public void IsDue_Should_Treat_Future_LastCheck_As_Due()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        Assert.True(UpdateCheckDueCalculator.IsDue(UpdateCheckInterval.Monthly, now.AddDays(1), now));
    }
}

