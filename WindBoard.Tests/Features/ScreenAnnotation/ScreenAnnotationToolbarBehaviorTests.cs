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

}
