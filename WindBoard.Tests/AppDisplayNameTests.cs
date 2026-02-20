using System.Globalization;
using Xunit;

namespace WindBoard.Tests;

public sealed class AppDisplayNameTests
{
    [Theory]
    [InlineData("zh-CN", "轻风白板")]
    [InlineData("zh-TW", "轻风白板")]
    [InlineData("zh-Hans", "轻风白板")]
    [InlineData("en-US", "WindBoard")]
    [InlineData("ja-JP", "WindBoard")]
    public void Get_ReturnsExpectedName_ByCulture(string cultureName, string expected)
    {
        var culture = new CultureInfo(cultureName);
        Assert.Equal(expected, AppDisplayName.Get(culture));
    }
}

