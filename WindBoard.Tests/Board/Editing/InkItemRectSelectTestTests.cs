using System.Collections.Generic;
using System.Numerics;
using WindBoard.Board;
using WindBoard.Board.Editing;
using WindBoard.Board.Items;
using Xunit;

namespace WindBoard.Tests.Board.Editing;

public sealed class InkItemRectSelectTestTests
{
    // 框选重叠区域时应优先选中“更靠上”的条目（列表末尾）
    [Fact]
    public void HitTestTopMostInkItemInWorldRect_PrefersLastItem()
    {
        Stroke a = StrokeTestFactory.CreateStroke(new Vector2(-10, 0), new Vector2(10, 0));
        Stroke b = StrokeTestFactory.CreateStroke(new Vector2(-10, 1), new Vector2(10, 1));

        var items = new List<IBoardInkItem> { a, b };
        IBoardInkItem? hit = InkItemRectSelectTest.HitTestTopMostInkItemInWorldRect(items, minWorld: new Vector2(-5, -5), maxWorld: new Vector2(5, 5));

        Assert.Same(b, hit);
    }

    // 框选多个条目时应返回所有相交条目，并保持原列表顺序（便于作为整体操作时保持相对层级）
    [Fact]
    public void HitTestInkItemsInWorldRect_ReturnsAllHitsInOriginalOrder()
    {
        Stroke a = StrokeTestFactory.CreateStroke(new Vector2(-10, 0), new Vector2(10, 0));
        Stroke b = StrokeTestFactory.CreateStroke(new Vector2(-10, 10), new Vector2(10, 10));
        Stroke c = StrokeTestFactory.CreateStroke(new Vector2(-10, 20), new Vector2(10, 20));

        var items = new List<IBoardInkItem> { a, b, c };

        // 框选覆盖 b 与 c（不包含 a）
        var hits = InkItemRectSelectTest.HitTestInkItemsInWorldRect(items, minWorld: new Vector2(-5, 5), maxWorld: new Vector2(5, 25));

        Assert.Equal(2, hits.Count);
        Assert.Same(b, hits[0]);
        Assert.Same(c, hits[1]);
    }

    // 与矩形无交集时不应命中（Stroke 分支）
    [Fact]
    public void IsStrokeIntersectWorldRect_ReturnsFalse_WhenDisjoint()
    {
        Stroke s = StrokeTestFactory.CreateStroke(new Vector2(0, 0), new Vector2(10, 0));

        bool hit = InkItemRectSelectTest.IsStrokeIntersectWorldRect(s, minWorld: new Vector2(0, 100), maxWorld: new Vector2(10, 110));

        Assert.False(hit);
    }

    [Fact]
    public void IsStrokeIntersectWorldRect_ReturnsFalse_WhenOnlyBoundingBoxesOverlap()
    {
        Stroke stroke = StrokeTestFactory.CreateStroke(new Vector2(0, 0), new Vector2(10, 10));

        bool hit = InkItemRectSelectTest.IsStrokeIntersectWorldRect(
            stroke,
            minWorld: new Vector2(8.0f, -2.0f),
            maxWorld: new Vector2(12.0f, 2.0f));

        Assert.False(hit);
    }

    [Fact]
    public void IsStrokeIntersectWorldRect_ReturnsTrue_WhenRectTouchesStrokeThickness()
    {
        Stroke stroke = StrokeTestFactory.CreateStroke(new Vector2(0, 0), new Vector2(10, 10));

        bool hit = InkItemRectSelectTest.IsStrokeIntersectWorldRect(
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

        bool hit = InkItemRectSelectTest.IsStrokeIntersectWorldRect(
            stroke,
            minWorld: new Vector2(8.0f, -2.0f),
            maxWorld: new Vector2(12.0f, 2.0f));

        Assert.False(hit);
    }

    // 非 Stroke 条目走通用路径：条目包围盒与框选矩形相交即命中
    [Fact]
    public void IsInkItemIntersectWorldRect_HitsNonStrokeItem_WhenBoundsIntersectRect()
    {
        var item = new TestInkItem(Vortice.Mathematics.Rect.FromLTRB(0.0f, 0.0f, 10.0f, 10.0f));

        Assert.True(InkItemRectSelectTest.IsInkItemIntersectWorldRect(item, minWorld: new Vector2(5.0f, 5.0f), maxWorld: new Vector2(20.0f, 20.0f)));
        Assert.False(InkItemRectSelectTest.IsInkItemIntersectWorldRect(item, minWorld: new Vector2(20.0f, 20.0f), maxWorld: new Vector2(30.0f, 30.0f)));
    }

    // 非 Stroke 条目：无有效包围盒（空矩形）时不命中
    [Fact]
    public void IsInkItemIntersectWorldRect_MissesNonStrokeItem_WhenBoundsEmpty()
    {
        var item = new TestInkItem(Vortice.Mathematics.Rect.Empty);

        Assert.False(InkItemRectSelectTest.IsInkItemIntersectWorldRect(item, minWorld: Vector2.Zero, maxWorld: new Vector2(10.0f, 10.0f)));
    }
}
