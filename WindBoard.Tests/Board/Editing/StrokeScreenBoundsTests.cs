using System.Collections.Generic;
using System.Numerics;
using WindBoard.Board;
using WindBoard.Board.Editing;
using Vortice.Mathematics;
using Xunit;

namespace WindBoard.Tests.Board.Editing;

public sealed class StrokeScreenBoundsTests
{
    [Fact]
    public void TryGetStrokesBoundsScreenDip_ReturnsFalse_WhenEmpty()
    {
        bool ok = StrokeScreenBounds.TryGetStrokesBoundsScreenDip(
            strokes: new List<Stroke>(),
            worldToScreen: Matrix3x2.Identity,
            out _);
        Assert.False(ok);
    }

    [Fact]
    public void TryGetStrokesBoundsScreenDip_ReturnsFalse_WhenNoPoints()
    {
        var strokes = new List<Stroke>
        {
            new(),
            new(),
        };

        bool ok = StrokeScreenBounds.TryGetStrokesBoundsScreenDip(strokes, Matrix3x2.Identity, out _);
        Assert.False(ok);
    }

    [Fact]
    public void TryGetStrokesBoundsScreenDip_UnionsBounds_WhenMultipleStrokes()
    {
        // BaseSize=0 => halfWidth 最小为 0.25
        var s0 = new Stroke { BaseSize = 0.0f, EnablePressure = false };
        s0.Points.Add(new StrokePoint(new Vector2(0.0f, 0.0f), 1.0f));
        s0.ExpandBounds(new Vector2(0.0f, 0.0f), 1.0f);

        var s1 = new Stroke { BaseSize = 0.0f, EnablePressure = false };
        s1.Points.Add(new StrokePoint(new Vector2(10.0f, 5.0f), 1.0f));
        s1.ExpandBounds(new Vector2(10.0f, 5.0f), 1.0f);

        var strokes = new List<Stroke> { s0, s1 };

        bool ok = StrokeScreenBounds.TryGetStrokesBoundsScreenDip(strokes, Matrix3x2.Identity, out Rect bounds);
        Assert.True(ok);

        AssertEx.Equal(-0.25f, bounds.Left);
        AssertEx.Equal(-0.25f, bounds.Top);
        AssertEx.Equal(10.25f, bounds.Right);
        AssertEx.Equal(5.25f, bounds.Bottom);
    }

    [Fact]
    public void TryGetStrokesBoundsScreenDip_RecalculatesBounds_WhenMissing()
    {
        // 不调用 ExpandBounds / RecalculateBoundsFromPoints，让 Bounds 处于“未计算”状态，验证兜底逻辑。
        var stroke = new Stroke { BaseSize = 0.0f, EnablePressure = false };
        stroke.Points.Add(new StrokePoint(new Vector2(1.0f, 2.0f), 1.0f));

        bool ok = StrokeScreenBounds.TryGetStrokesBoundsScreenDip(new[] { stroke }, Matrix3x2.Identity, out Rect bounds);
        Assert.True(ok);

        AssertEx.Equal(0.75f, bounds.Left);
        AssertEx.Equal(1.75f, bounds.Top);
        AssertEx.Equal(1.25f, bounds.Right);
        AssertEx.Equal(2.25f, bounds.Bottom);
    }
}

