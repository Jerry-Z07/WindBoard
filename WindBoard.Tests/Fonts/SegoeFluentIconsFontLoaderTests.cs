using WindBoard.Fonts;

namespace WindBoard.Tests.Fonts;

public sealed class SegoeFluentIconsFontLoaderTests
{
    [Theory]
    [InlineData(21999, false)]
    [InlineData(22000, true)]
    [InlineData(22621, true)]
    public void IsWin11OrLaterBuild_Should_Match_Threshold(int build, bool expected)
    {
        Assert.Equal(expected, SegoeFluentIconsFontLoader.IsWin11OrLaterBuild(build));
    }

    [Theory]
    // Win11+：即使未安装/未私有加载，也应选择 Fluent（系统自带）。
    [InlineData(22000, false, false, SegoeFluentIconsFontLoader.FluentFontFamilyName)]
    // Win10：系统已安装 Fluent。
    [InlineData(19045, true, false, SegoeFluentIconsFontLoader.FluentFontFamilyName)]
    // Win10：未安装，但私有加载成功。
    [InlineData(19045, false, true, SegoeFluentIconsFontLoader.FluentFontFamilyName)]
    // Win10：未安装且私有加载失败 -> 降级 MDL2。
    [InlineData(19045, false, false, SegoeFluentIconsFontLoader.FallbackFontFamilyName)]
    public void DecideEffectiveIconFontFamilyName_Should_Select_Correct_Font(
        int build,
        bool fluentInstalled,
        bool privateLoaded,
        string expectedFamilyName)
    {
        string chosen = SegoeFluentIconsFontLoader.DecideEffectiveIconFontFamilyName(build, fluentInstalled, privateLoaded);
        Assert.Equal(expectedFamilyName, chosen);
    }
}

