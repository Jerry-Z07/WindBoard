using WindBoard.Features.ScreenAnnotation.Models;
using WindBoard.Features.ScreenAnnotation.Services;
using WindBoard.Interaction;
using Xunit;

namespace WindBoard.Tests.Features.ScreenAnnotation;

public sealed class ScreenAnnotationWindowStateTests
{
    [Fact]
    public void Constructor_DefaultsToPassThroughAndPen()
    {
        var state = new ScreenAnnotationWindowState();

        Assert.Equal(ScreenAnnotationMode.PassThrough, state.Mode);
        Assert.True(state.IsPassThrough);
        Assert.Equal(BoardTool.Pen, state.ActiveCanvasTool);
    }

    [Fact]
    public void SetMode_Eraser_UpdatesCanvasToolAndDisablesPassThrough()
    {
        var state = new ScreenAnnotationWindowState();

        state.SetMode(ScreenAnnotationMode.Eraser);

        Assert.Equal(ScreenAnnotationMode.Eraser, state.Mode);
        Assert.False(state.IsPassThrough);
        Assert.Equal(BoardTool.Eraser, state.ActiveCanvasTool);
    }

    [Fact]
    public void SetMode_PassThrough_KeepsLastDrawingTool()
    {
        var state = new ScreenAnnotationWindowState();
        state.SetMode(ScreenAnnotationMode.Eraser);

        state.SetMode(ScreenAnnotationMode.PassThrough);

        Assert.Equal(ScreenAnnotationMode.PassThrough, state.Mode);
        Assert.True(state.IsPassThrough);
        Assert.Equal(BoardTool.Eraser, state.ActiveCanvasTool);
    }
}
