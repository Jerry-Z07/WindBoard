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
    public void TryNormalize_AcceptsKnownValues(string input, string expectedSettingValue)
    {
        bool ok = AppLanguagePreferenceParser.TryNormalize(input, out string normalized);

        Assert.True(ok);
        Assert.Equal(expectedSettingValue, normalized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("unknown")]
    public void TryNormalize_RejectsInvalidValues(string? input)
    {
        bool ok = AppLanguagePreferenceParser.TryNormalize(input, out _);

        Assert.False(ok);
    }

    [Fact]
    public void NormalizeOrDefault_ReturnsSystem_ForInvalidValues()
    {
        Assert.Equal(AppLanguagePreferenceParser.SystemValue, AppLanguagePreferenceParser.NormalizeOrDefault(null));
        Assert.Equal(AppLanguagePreferenceParser.SystemValue, AppLanguagePreferenceParser.NormalizeOrDefault(string.Empty));
        Assert.Equal(AppLanguagePreferenceParser.SystemValue, AppLanguagePreferenceParser.NormalizeOrDefault("unknown"));
    }
}
