using System.Numerics;
using WindBoard.Board;
using WindBoard.Board.Viewport;
using WindBoard.Interaction;
using Vortice.Mathematics;
using Xunit;

namespace WindBoard.Tests.Interaction;

public sealed class BoardInputDirtyRectCalculatorTests
{
    [Fact]
    public void UpdatePendingStrokeDirtyRect_单点会按笔宽与额外Padding扩展()
    {
        var viewport = new BoardViewport();
        viewport.UpdateViewportSize(new Vector2(1.0f, 1.0f));

        var stroke = new Stroke
        {
            BaseSize = 10.0f,
            EnablePressure = true,
        };
        stroke.Points.Add(new StrokePoint(new Vector2(0.0f, 0.0f), 1.0f));

        Vector2 latestScreen = new(100.0f, 50.0f);
        Rect? pending = BoardInputDirtyRectCalculator.UpdatePendingStrokeDirtyRect(
            pendingStrokeDirtyRectDip: null,
            stroke,
            viewport,
            latestScreen,
            extraPaddingDip: 2.0f);

        Assert.NotNull(pending);
        Rect rect = pending!.Value;

        // padding = extra(2) + halfWidthScreen(10 * 1 / 2 * zoom=1) = 7
        AssertEx.Equal(93.0f, rect.Left);
        AssertEx.Equal(43.0f, rect.Top);
        AssertEx.Equal(107.0f, rect.Right);
        AssertEx.Equal(57.0f, rect.Bottom);
    }

    [Fact]
    public void UpdatePendingStrokeDirtyRect_两点会使用上一点屏幕坐标并合并已有Rect()
    {
        var viewport = new BoardViewport();
        viewport.UpdateViewportSize(new Vector2(1.0f, 1.0f));

        var stroke = new Stroke
        {
            BaseSize = 10.0f,
            EnablePressure = true,
        };

        Vector2 prevWorld = new(1.0f, 2.0f);
        Vector2 currWorld = new(3.0f, 4.0f);
        stroke.Points.Add(new StrokePoint(prevWorld, 0.5f));
        stroke.Points.Add(new StrokePoint(currWorld, 1.0f));

        Vector2 latestScreen = Vector2.Transform(currWorld, viewport.GetWorldToScreenTransform());
        Rect existing = Rect.FromLTRB(0, 0, 1, 1);

        Rect? pending = BoardInputDirtyRectCalculator.UpdatePendingStrokeDirtyRect(
            pendingStrokeDirtyRectDip: existing,
            stroke,
            viewport,
            latestScreen,
            extraPaddingDip: 2.0f);

        Assert.NotNull(pending);
        Rect rect = pending!.Value;

        // viewport size=1 => center=(0.5,0.5)，zoom=1、camera=0
        // prevScreen=(1.5,2.5)，latestScreen=(3.5,4.5)
        // widthFactor=clamp((0.5+1.0)/2=0.75)=0.75
        // halfWidthWorld=10*0.75/2=3.75 => padding=2+3.75=5.75
        // 线段包围盒扩展后：left=-4.25 top=-3.25 right=9.25 bottom=10.25
        AssertEx.Equal(-4.25f, rect.Left);
        AssertEx.Equal(-3.25f, rect.Top);
        AssertEx.Equal(9.25f, rect.Right);
        AssertEx.Equal(10.25f, rect.Bottom);
    }
}

