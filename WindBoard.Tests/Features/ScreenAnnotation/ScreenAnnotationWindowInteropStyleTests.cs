using WindBoard.Features.ScreenAnnotation.Interop;
using Xunit;

namespace WindBoard.Tests.Features.ScreenAnnotation;

public sealed class ScreenAnnotationWindowInteropStyleTests
{
    private const uint WsCaption = 0x00C00000;
    private const uint WsThickFrame = 0x00040000;
    private const uint WsMinimizeBox = 0x00020000;
    private const uint WsMaximizeBox = 0x00010000;
    private const uint WsSysMenu = 0x00080000;
    private const uint WsVisible = 0x10000000;

    private const uint WsExTransparent = 0x00000020;
    private const uint WsExToolWindow = 0x00000080;
    private const uint WsExLayered = 0x00080000;

    [Fact]
    public void BuildBorderlessWindowStyle_RemovesNonClientFrameBitsAndPreservesOtherFlags()
    {
        uint currentStyle = WsCaption
            | WsThickFrame
            | WsMinimizeBox
            | WsMaximizeBox
            | WsSysMenu
            | WsVisible;

        uint result = ScreenAnnotationWindowInterop.BuildBorderlessWindowStyle(currentStyle);

        Assert.Equal(WsVisible, result & WsVisible);
        Assert.Equal(0u, result & WsCaption);
        Assert.Equal(0u, result & WsThickFrame);
        Assert.Equal(0u, result & WsMinimizeBox);
        Assert.Equal(0u, result & WsMaximizeBox);
        Assert.Equal(0u, result & WsSysMenu);
    }

    [Fact]
    public void BuildAnnotationWindowExtendedStyle_AddsToolWindowAndLayeredFlags()
    {
        uint result = ScreenAnnotationWindowInterop.BuildAnnotationWindowExtendedStyle(WsExTransparent);

        Assert.Equal(WsExTransparent, result & WsExTransparent);
        Assert.Equal(WsExToolWindow, result & WsExToolWindow);
        Assert.Equal(WsExLayered, result & WsExLayered);
    }

    [Fact]
    public void BuildToolbarWindowExtendedStyle_AddsToolWindowAndLayeredFlags()
    {
        uint result = ScreenAnnotationWindowInterop.BuildToolbarWindowExtendedStyle(WsExTransparent);

        Assert.Equal(WsExTransparent, result & WsExTransparent);
        Assert.Equal(WsExToolWindow, result & WsExToolWindow);
        Assert.Equal(WsExLayered, result & WsExLayered);
    }

    [Fact]
    public void DwmBorderSuppressionConstants_MatchExpectedValues()
    {
        Assert.Equal(34u, ScreenAnnotationWindowInterop.DwmBorderColorAttribute);
        Assert.Equal(0xFFFFFFFEu, ScreenAnnotationWindowInterop.DwmColorNone);
    }

    [Fact]
    public void DwmCornerSuppressionConstants_MatchExpectedValues()
    {
        Assert.Equal(33u, ScreenAnnotationWindowInterop.DwmWindowCornerPreferenceAttribute);
        Assert.Equal(1u, ScreenAnnotationWindowInterop.DwmWindowCornerPreferenceDoNotRound);
    }
}
