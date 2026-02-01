using Windows.UI;
using WindBoard.Settings;
using Xunit;

namespace WindBoard.Tests.Settings;

public sealed class ColorHexTests
{
    // 支持井号 RGB 格式
    [Fact]
    public void TryParse_SupportsRgbWithLeadingHash()
    {
        bool ok = ColorHex.TryParse("#2E2F33", out Color color);

        Assert.True(ok);
        Assert.Equal(0xFF, color.A);
        Assert.Equal(0x2E, color.R);
        Assert.Equal(0x2F, color.G);
        Assert.Equal(0x33, color.B);
    }

    // 支持无井号 RGB 格式
    [Fact]
    public void TryParse_SupportsRgbWithoutLeadingHash()
    {
        bool ok = ColorHex.TryParse("2E2F33", out Color color);

        Assert.True(ok);
        Assert.Equal(0xFF, color.A);
        Assert.Equal(0x2E, color.R);
        Assert.Equal(0x2F, color.G);
        Assert.Equal(0x33, color.B);
    }

    // 支持 ARGB 格式
    [Fact]
    public void TryParse_SupportsArgb()
    {
        bool ok = ColorHex.TryParse("#802E2F33", out Color color);

        Assert.True(ok);
        Assert.Equal(0x80, color.A);
        Assert.Equal(0x2E, color.R);
        Assert.Equal(0x2F, color.G);
        Assert.Equal(0x33, color.B);
    }

    // 统一输出大写 RGB
    [Fact]
    public void ToHexRgb_OutputsUppercaseRgb()
    {
        bool ok = ColorHex.TryParse("#802e2f33", out Color color);

        Assert.True(ok);
        Assert.Equal("#2E2F33", ColorHex.ToHexRgb(color));
    }

    // 非法输入回退默认值
    [Fact]
    public void NormalizeToHexRgbOrDefault_FallsBackToDefault_WhenInvalidInput()
    {
        string normalized = ColorHex.NormalizeToHexRgbOrDefault("not-a-color", ColorHex.DefaultCanvasBackgroundHex);

        Assert.Equal(ColorHex.DefaultCanvasBackgroundHex, normalized);
    }
}
