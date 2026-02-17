using WindBoard.Settings;
using Xunit;

namespace WindBoard.Tests.Settings;

public sealed class AppLanguagePreferenceParserTests
{
    [Theory]
    [InlineData("system", AppLanguagePreferenceParser.SystemValue)]
    [InlineData("auto", AppLanguagePreferenceParser.SystemValue)]
    [InlineData("zh-CN", AppLanguagePreferenceParser.ChineseValue)]
    [InlineData("zh_CN", AppLanguagePreferenceParser.ChineseValue)]
    [InlineData("zh", AppLanguagePreferenceParser.ChineseValue)]
    [InlineData("en-US", AppLanguagePreferenceParser.EnglishValue)]
    [InlineData("en_US", AppLanguagePreferenceParser.EnglishValue)]
    [InlineData("en", AppLanguagePreferenceParser.EnglishValue)]
    public void TryParse_AcceptsKnownValues(string input, string expectedSettingValue)
    {
        bool ok = AppLanguagePreferenceParser.TryParse(input, out AppLanguagePreference parsed);

        Assert.True(ok);
        Assert.Equal(expectedSettingValue, AppLanguagePreferenceParser.ToSettingValue(parsed));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("unknown")]
    public void TryParse_RejectsInvalidValues(string? input)
    {
        bool ok = AppLanguagePreferenceParser.TryParse(input, out _);

        Assert.False(ok);
    }

    [Fact]
    public void ToSettingValue_ReturnsCanonicalValues()
    {
        Assert.Equal(AppLanguagePreferenceParser.SystemValue, AppLanguagePreferenceParser.ToSettingValue(AppLanguagePreference.System));
        Assert.Equal(AppLanguagePreferenceParser.ChineseValue, AppLanguagePreferenceParser.ToSettingValue(AppLanguagePreference.Chinese));
        Assert.Equal(AppLanguagePreferenceParser.EnglishValue, AppLanguagePreferenceParser.ToSettingValue(AppLanguagePreference.English));
    }
}
