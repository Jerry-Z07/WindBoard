using System.Runtime.InteropServices;
using WindBoard.Features.ScreenAnnotation.Interop;
using Windows.Graphics;
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

    [Fact]
    public void ConvertDipSizeToPixels_ScalesToolbarSizeUsingWindowDpi()
    {
        Assert.Equal(345, ScreenAnnotationWindowInterop.ConvertDipSizeToPixels(276, 120));
        Assert.Equal(75, ScreenAnnotationWindowInterop.ConvertDipSizeToPixels(60, 120));
    }

    [Fact]
    public void ConvertDipCoordinateToPixels_ScalesToolbarMarginUsingWindowDpi()
    {
        Assert.Equal(10, ScreenAnnotationWindowInterop.ConvertDipCoordinateToPixels(8, 120));
    }

    [Fact]
    public void ConvertDpiToScaleRatio_UsesNinetySixDpiAsBase()
    {
        Assert.Equal(1.25, ScreenAnnotationWindowInterop.ConvertDpiToScaleRatio(120), precision: 2);
        Assert.Equal(1.50, ScreenAnnotationWindowInterop.ConvertDpiToScaleRatio(144), precision: 2);
    }

    [Fact]
    public void ConvertDpiToScalePercent_RoundsToNearestIntegerPercent()
    {
        Assert.Equal(125, ScreenAnnotationWindowInterop.ConvertDpiToScalePercent(120));
        Assert.Equal(150, ScreenAnnotationWindowInterop.ConvertDpiToScalePercent(144));
    }

    [Fact]
    public void TryReadDpiChangedSuggestedRect_ReadsSuggestedWindowBounds()
    {
        nint buffer = Marshal.AllocHGlobal(sizeof(int) * 4);
        try
        {
            Marshal.WriteInt32(buffer, 0, 120);
            Marshal.WriteInt32(buffer, sizeof(int), 220);
            Marshal.WriteInt32(buffer, sizeof(int) * 2, 420);
            Marshal.WriteInt32(buffer, sizeof(int) * 3, 295);

            bool ok = ScreenAnnotationWindowInterop.TryReadDpiChangedSuggestedRect(buffer, out RectInt32 rect);

            Assert.True(ok);
            Assert.Equal(new RectInt32(120, 220, 300, 75), rect);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
