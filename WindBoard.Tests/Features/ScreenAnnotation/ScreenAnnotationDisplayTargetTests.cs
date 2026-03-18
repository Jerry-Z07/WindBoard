using Windows.Graphics;
using WindBoard.Features.ScreenAnnotation.Models;
using Xunit;

namespace WindBoard.Tests.Features.ScreenAnnotation;

public sealed class ScreenAnnotationDisplayTargetTests
{
    [Fact]
    public void GetInitialToolbarBounds_PlacesToolbarInsideDisplay()
    {
        var target = new ScreenAnnotationDisplayTarget(
            MonitorHandle: nint.Zero,
            Bounds: new RectInt32(100, 200, 1600, 900),
            WorkArea: new RectInt32(100, 200, 1600, 900));

        RectInt32 toolbarBounds = target.GetInitialToolbarBounds(width: 280, height: 72);

        Assert.True(toolbarBounds.X >= target.Bounds.X);
        Assert.True(toolbarBounds.Y >= target.Bounds.Y);
        Assert.True(toolbarBounds.X + toolbarBounds.Width <= target.Bounds.X + target.Bounds.Width);
        Assert.True(toolbarBounds.Y + toolbarBounds.Height <= target.Bounds.Y + target.Bounds.Height);
    }

    [Fact]
    public void GetInitialToolbarBounds_ClampsWhenDisplayIsVerySmall()
    {
        var target = new ScreenAnnotationDisplayTarget(
            MonitorHandle: nint.Zero,
            Bounds: new RectInt32(0, 0, 200, 80),
            WorkArea: new RectInt32(0, 0, 200, 80));

        RectInt32 toolbarBounds = target.GetInitialToolbarBounds(width: 280, height: 72);

        Assert.Equal(0, toolbarBounds.X);
        Assert.Equal(8, toolbarBounds.Y);
        Assert.Equal(200, toolbarBounds.Width);
        Assert.Equal(72, toolbarBounds.Height);
    }
}
