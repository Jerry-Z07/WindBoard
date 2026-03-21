using Windows.Graphics;
using WindBoard.Features.ScreenAnnotation.Models;
using WindBoard.Features.ScreenAnnotation.UI;
using Xunit;

namespace WindBoard.Tests.Features.ScreenAnnotation;

public sealed class ScreenAnnotationToolbarBehaviorTests
{
    [Fact]
    public void IsSecondaryClick_WhenCurrentModeDiffers_ReturnsFalse()
    {
        bool actual = ScreenAnnotationToolbarBehavior.IsSecondaryClick(
            ScreenAnnotationMode.PassThrough,
            ScreenAnnotationMode.Pen);

        Assert.False(actual);
    }

    [Fact]
    public void IsSecondaryClick_WhenPenIsClickedAgain_ReturnsTrue()
    {
        bool actual = ScreenAnnotationToolbarBehavior.IsSecondaryClick(
            ScreenAnnotationMode.Pen,
            ScreenAnnotationMode.Pen);

        Assert.True(actual);
    }

    [Fact]
    public void IsSecondaryClick_WhenEraserIsClickedAgain_ReturnsTrue()
    {
        bool actual = ScreenAnnotationToolbarBehavior.IsSecondaryClick(
            ScreenAnnotationMode.Eraser,
            ScreenAnnotationMode.Eraser);

        Assert.True(actual);
    }

    [Fact]
    public void IsSecondaryClick_WhenSwitchingFromEraserToPen_ReturnsFalse()
    {
        bool actual = ScreenAnnotationToolbarBehavior.IsSecondaryClick(
            ScreenAnnotationMode.Eraser,
            ScreenAnnotationMode.Pen);

        Assert.False(actual);
    }

    [Fact]
    public void BuildFlyoutHostBounds_ExpandsUpwardAndKeepsBottomAnchor()
    {
        var target = new ScreenAnnotationDisplayTarget(
            MonitorHandle: nint.Zero,
            Bounds: new RectInt32(0, 0, 1920, 1080),
            WorkArea: new RectInt32(0, 0, 1920, 1080));
        RectInt32 currentBounds = new(100, 1000, 276, 60);

        RectInt32 bounds = ScreenAnnotationToolbarBehavior.BuildFlyoutHostBounds(
            target,
            currentBounds,
            flyoutWidth: 232,
            flyoutHeight: 180,
            toolbarHeight: 60);

        Assert.Equal(100, bounds.X);
        Assert.Equal(820, bounds.Y);
        Assert.Equal(276, bounds.Width);
        Assert.Equal(240, bounds.Height);
    }

    [Fact]
    public void BuildCompactToolbarBounds_RestoresToolbarHeightAndKeepsBottomAnchor()
    {
        var target = new ScreenAnnotationDisplayTarget(
            MonitorHandle: nint.Zero,
            Bounds: new RectInt32(0, 0, 1920, 1080),
            WorkArea: new RectInt32(0, 0, 1920, 1080));
        RectInt32 expandedBounds = new(100, 820, 276, 240);

        RectInt32 bounds = ScreenAnnotationToolbarBehavior.BuildCompactToolbarBounds(
            target,
            expandedBounds,
            compactWidth: 276,
            compactHeight: 60);

        Assert.Equal(100, bounds.X);
        Assert.Equal(1000, bounds.Y);
        Assert.Equal(276, bounds.Width);
        Assert.Equal(60, bounds.Height);
    }
}
