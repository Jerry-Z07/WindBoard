using Windows.Graphics;
using WindBoard.Features.ScreenAnnotation.Models;
using Xunit;

namespace WindBoard.Tests.Features.ScreenAnnotation;

public sealed class ScreenAnnotationDisplayTargetTests
{
    [Fact]
    public void GetInitialToolbarBounds_PlacesToolbarAtBottomLeftWithMargin()
    {
        var target = new ScreenAnnotationDisplayTarget(
            MonitorHandle: nint.Zero,
            Bounds: new RectInt32(100, 200, 1600, 900),
            WorkArea: new RectInt32(100, 200, 1600, 900));

        RectInt32 toolbarBounds = target.GetInitialToolbarBounds(width: 280, height: 72);

        Assert.Equal(108, toolbarBounds.X);
        Assert.Equal(1020, toolbarBounds.Y);
        Assert.Equal(280, toolbarBounds.Width);
        Assert.Equal(72, toolbarBounds.Height);
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
        Assert.Equal(0, toolbarBounds.Y);
        Assert.Equal(200, toolbarBounds.Width);
        Assert.Equal(72, toolbarBounds.Height);
    }
}
