using System.Collections.Generic;
using System.Numerics;
using WindBoard.Board;
using WindBoard.Board.Editing;
using Xunit;

namespace WindBoard.Tests.Board.Editing;

public sealed class StrokeRectSelectTestTests
{
    // 框选重叠区域时应优先选中“更靠上”的笔迹（列表末尾）
    [Fact]
    public void HitTestTopMostStrokeInWorldRect_PrefersLastStroke()
    {
        Stroke a = StrokeTestFactory.CreateStroke(new Vector2(-10, 0), new Vector2(10, 0));
        Stroke b = StrokeTestFactory.CreateStroke(new Vector2(-10, 1), new Vector2(10, 1));

        var strokes = new List<Stroke> { a, b };
        Stroke? hit = StrokeRectSelectTest.HitTestTopMostStrokeInWorldRect(strokes, minWorld: new Vector2(-5, -5), maxWorld: new Vector2(5, 5));

        Assert.Same(b, hit);
    }

    // 框选多个笔迹时应返回所有相交笔迹，并保持原列表顺序（便于作为整体操作时保持相对层级）
    [Fact]
    public void HitTestStrokesInWorldRect_ReturnsAllHitsInOriginalOrder()
    {
        Stroke a = StrokeTestFactory.CreateStroke(new Vector2(-10, 0), new Vector2(10, 0));
        Stroke b = StrokeTestFactory.CreateStroke(new Vector2(-10, 10), new Vector2(10, 10));
        Stroke c = StrokeTestFactory.CreateStroke(new Vector2(-10, 20), new Vector2(10, 20));

        var strokes = new List<Stroke> { a, b, c };

        // 框选覆盖 b 与 c（不包含 a）
        var hits = StrokeRectSelectTest.HitTestStrokesInWorldRect(strokes, minWorld: new Vector2(-5, 5), maxWorld: new Vector2(5, 25));

        Assert.Equal(2, hits.Count);
        Assert.Same(b, hits[0]);
        Assert.Same(c, hits[1]);
    }

    // 与矩形无交集时不应命中
    [Fact]
    public void IsStrokeIntersectWorldRect_ReturnsFalse_WhenDisjoint()
    {
        Stroke s = StrokeTestFactory.CreateStroke(new Vector2(0, 0), new Vector2(10, 0));

        bool hit = StrokeRectSelectTest.IsStrokeIntersectWorldRect(s, minWorld: new Vector2(0, 100), maxWorld: new Vector2(10, 110));

        Assert.False(hit);
    }

    [Fact]
    public void IsStrokeIntersectWorldRect_ReturnsFalse_WhenOnlyBoundingBoxesOverlap()
    {
        Stroke stroke = StrokeTestFactory.CreateStroke(new Vector2(0, 0), new Vector2(10, 10));

        bool hit = StrokeRectSelectTest.IsStrokeIntersectWorldRect(
            stroke,
            minWorld: new Vector2(8.0f, -2.0f),
            maxWorld: new Vector2(12.0f, 2.0f));

        Assert.False(hit);
    }

    [Fact]
    public void IsStrokeIntersectWorldRect_ReturnsTrue_WhenRectTouchesStrokeThickness()
    {
        Stroke stroke = StrokeTestFactory.CreateStroke(new Vector2(0, 0), new Vector2(10, 10));

        bool hit = StrokeRectSelectTest.IsStrokeIntersectWorldRect(
            stroke,
            minWorld: new Vector2(7.0f, 3.0f),
            maxWorld: new Vector2(8.0f, 4.0f));

        Assert.True(hit);
    }

    [Fact]
    public void IsStrokeIntersectWorldRect_DoesNotUseMaxEndpointWidth_ForPressureStrokeSegment()
    {
        var stroke = new Stroke
        {
            BaseSize = 10.0f,
            EnablePressure = true,
        };

        stroke.Points.Add(new StrokePoint(new Vector2(0.0f, 0.0f), 1.0f));
        stroke.ExpandBounds(new Vector2(0.0f, 0.0f), 1.0f);
        stroke.Points.Add(new StrokePoint(new Vector2(10.0f, 10.0f), 0.1f));
        stroke.ExpandBounds(new Vector2(10.0f, 10.0f), 0.1f);

        bool hit = StrokeRectSelectTest.IsStrokeIntersectWorldRect(
            stroke,
            minWorld: new Vector2(8.0f, -2.0f),
            maxWorld: new Vector2(12.0f, 2.0f));

        Assert.False(hit);
    }
}
