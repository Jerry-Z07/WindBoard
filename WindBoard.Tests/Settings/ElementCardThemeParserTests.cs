using WindBoard.Settings;

namespace WindBoard.Tests.Settings;

public sealed class ElementCardThemeParserTests
{
    [Fact]
    public void TryParse_Null_ReturnsFalseAndDefaultsToDark()
    {
        bool ok = ElementCardThemeParser.TryParse(null, out ElementCardTheme theme);
        Assert.False(ok);
        Assert.Equal(ElementCardTheme.Dark, theme);
    }

    [Fact]
    public void TryParse_Dark_ReturnsDark()
    {
        Assert.True(ElementCardThemeParser.TryParse("dark", out ElementCardTheme theme));
        Assert.Equal(ElementCardTheme.Dark, theme);

        Assert.True(ElementCardThemeParser.TryParse("DARK", out theme));
        Assert.Equal(ElementCardTheme.Dark, theme);
    }

    [Fact]
    public void TryParse_Light_ReturnsLight()
    {
        Assert.True(ElementCardThemeParser.TryParse("light", out ElementCardTheme theme));
        Assert.Equal(ElementCardTheme.Light, theme);
    }

    [Fact]
    public void TryParse_Unknown_ReturnsFalseAndDefaultsToDark()
    {
        bool ok = ElementCardThemeParser.TryParse("unknown", out ElementCardTheme theme);
        Assert.False(ok);
        Assert.Equal(ElementCardTheme.Dark, theme);
    }

    [Fact]
    public void ToSettingValue_ReturnsLowercaseString()
    {
        Assert.Equal("dark", ElementCardThemeParser.ToSettingValue(ElementCardTheme.Dark));
        Assert.Equal("light", ElementCardThemeParser.ToSettingValue(ElementCardTheme.Light));
    }
}

