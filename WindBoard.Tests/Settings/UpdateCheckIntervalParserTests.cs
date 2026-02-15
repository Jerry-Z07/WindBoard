using WindBoard.Settings;
using Xunit;

namespace WindBoard.Tests.Settings;

public sealed class UpdateCheckIntervalParserTests
{
    [Theory]
    [InlineData("weekly", "weekly")]
    [InlineData("WEEKLY", "weekly")]
    [InlineData("  weekly  ", "weekly")]
    [InlineData("biweekly", "biweekly")]
    [InlineData("BIWEEKLY", "biweekly")]
    [InlineData("  biweekly  ", "biweekly")]
    [InlineData("monthly", "monthly")]
    [InlineData("MONTHLY", "monthly")]
    [InlineData("  monthly  ", "monthly")]
    [InlineData("never", "never")]
    [InlineData("NEVER", "never")]
    [InlineData("  never  ", "never")]
    public void TryParse_ParsesKnownValues(string text, string expectedSettingValue)
    {
        bool ok = UpdateCheckIntervalParser.TryParse(text, out UpdateCheckInterval interval);
        Assert.True(ok);
        Assert.Equal(expectedSettingValue, UpdateCheckIntervalParser.ToSettingValue(interval));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("unknown")]
    [InlineData("week")]
    public void TryParse_ReturnsFalse_OnInvalid(string? text)
    {
        bool ok = UpdateCheckIntervalParser.TryParse(text, out _);
        Assert.False(ok);
    }

    [Fact]
    public void ToSettingValue_ReturnsExpected()
    {
        Assert.Equal(UpdateCheckIntervalParser.WeeklyValue, UpdateCheckIntervalParser.ToSettingValue(UpdateCheckInterval.Weekly));
        Assert.Equal(UpdateCheckIntervalParser.BiweeklyValue, UpdateCheckIntervalParser.ToSettingValue(UpdateCheckInterval.Biweekly));
        Assert.Equal(UpdateCheckIntervalParser.MonthlyValue, UpdateCheckIntervalParser.ToSettingValue(UpdateCheckInterval.Monthly));
        Assert.Equal(UpdateCheckIntervalParser.NeverValue, UpdateCheckIntervalParser.ToSettingValue(UpdateCheckInterval.Never));
    }
}
