using WindBoard.Settings;

namespace WindBoard.Tests.Settings;

public sealed class StartupWindowModeParserTests
{
    [Fact]
    public void TryParse_Null_ReturnsFalseAndDefaultsToWindowed()
    {
        bool ok = StartupWindowModeParser.TryParse(null, out StartupWindowMode mode);
        Assert.False(ok);
        Assert.Equal(StartupWindowMode.Windowed, mode);
    }

    [Fact]
    public void TryParse_Windowed_ReturnsWindowed()
    {
        Assert.True(StartupWindowModeParser.TryParse("windowed", out StartupWindowMode mode));
        Assert.Equal(StartupWindowMode.Windowed, mode);

        Assert.True(StartupWindowModeParser.TryParse("WINDOWED", out mode));
        Assert.Equal(StartupWindowMode.Windowed, mode);
    }

    [Fact]
    public void TryParse_FullScreen_ReturnsFullScreen()
    {
        Assert.True(StartupWindowModeParser.TryParse("fullscreen", out StartupWindowMode mode));
        Assert.Equal(StartupWindowMode.FullScreen, mode);

        Assert.True(StartupWindowModeParser.TryParse("FULLSCREEN", out mode));
        Assert.Equal(StartupWindowMode.FullScreen, mode);
    }

    [Fact]
    public void TryParse_Unknown_ReturnsFalseAndDefaultsToWindowed()
    {
        bool ok = StartupWindowModeParser.TryParse("unknown", out StartupWindowMode mode);
        Assert.False(ok);
        Assert.Equal(StartupWindowMode.Windowed, mode);
    }

    [Fact]
    public void ToSettingValue_ReturnsLowercaseString()
    {
        Assert.Equal("windowed", StartupWindowModeParser.ToSettingValue(StartupWindowMode.Windowed));
        Assert.Equal("fullscreen", StartupWindowModeParser.ToSettingValue(StartupWindowMode.FullScreen));
    }
}

