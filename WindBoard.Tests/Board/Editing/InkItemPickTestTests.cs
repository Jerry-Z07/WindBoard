using System.Collections.Generic;
using System.Numerics;
using WindBoard.Board;
using WindBoard.Board.Editing;
using WindBoard.Board.Items;
using Xunit;

namespace WindBoard.Tests.Board.Editing;

public sealed class InkItemPickTestTests
{
    // 点击重叠区域时应优先选中“更靠上”的条目（列表末尾）
    [Fact]
    public void HitTestTopMostInkItem_PrefersLastItem()
    {
        Stroke a = StrokeTestFactory.CreateStroke(new Vector2(-10, 0), new Vector2(10, 0));
        Stroke b = StrokeTestFactory.CreateStroke(new Vector2(0, -10), new Vector2(0, 10));

        var items = new List<IBoardInkItem> { a, b };
        IBoardInkItem? hit = InkItemPickTest.HitTestTopMostInkItem(items, pointWorld: Vector2.Zero, toleranceWorld: 0.0f);

        Assert.Same(b, hit);
    }

    // 远离笔迹时不应命中（Stroke 分支）
    [Fact]
    public void IsStrokeHitByPoint_ReturnsFalse_WhenFarAway()
    {
        Stroke s = StrokeTestFactory.CreateStroke(new Vector2(0, 0), new Vector2(10, 0));

        bool hit = InkItemPickTest.IsStrokeHitByPoint(s, new Vector2(0, 100), toleranceWorld: 0.0f);

        Assert.False(hit);
    }

    // 单点笔迹应按“圆点”处理（Stroke 分支）
    [Fact]
    public void IsStrokeHitByPoint_HitsSinglePointStroke()
    {
        var stroke = new Stroke
        {
            BaseSize = 6.0f,
            EnablePressure = false,
        };

        stroke.Points.Add(new StrokePoint(new Vector2(0, 0), 1.0f));
        stroke.ExpandBounds(new Vector2(0, 0), 1.0f);

        Assert.True(InkItemPickTest.IsStrokeHitByPoint(stroke, new Vector2(0, 0), toleranceWorld: 0.0f));
        Assert.False(InkItemPickTest.IsStrokeHitByPoint(stroke, new Vector2(0, 10), toleranceWorld: 0.0f));
    }

    // 非 Stroke 条目走通用路径：点位于包围盒内（含容差）即命中
    [Fact]
    public void IsInkItemHitByPoint_HitsNonStrokeItem_WhenPointInsideBoundsWithTolerance()
    {
        var item = new TestInkItem(Vortice.Mathematics.Rect.FromLTRB(0.0f, 0.0f, 10.0f, 10.0f));

        Assert.True(InkItemPickTest.IsInkItemHitByPoint(item, new Vector2(5.0f, 5.0f), toleranceWorld: 0.0f));
        Assert.True(InkItemPickTest.IsInkItemHitByPoint(item, new Vector2(11.0f, 5.0f), toleranceWorld: 2.0f));
        Assert.False(InkItemPickTest.IsInkItemHitByPoint(item, new Vector2(11.0f, 5.0f), toleranceWorld: 0.0f));
    }

    // 非 Stroke 条目：无有效包围盒（空矩形）时不命中
    [Fact]
    public void IsInkItemHitByPoint_MissesNonStrokeItem_WhenBoundsEmpty()
    {
        var item = new TestInkItem(Vortice.Mathematics.Rect.Empty);

        Assert.False(InkItemPickTest.IsInkItemHitByPoint(item, Vector2.Zero, toleranceWorld: 5.0f));
    }

    // 混合列表（Stroke + 非 Stroke 替身）：点选按类型分发，返回最上层命中条目
    [Fact]
    public void HitTestTopMostInkItem_DispatchesByItemType_WhenMixed()
    {
        Stroke stroke = StrokeTestFactory.CreateStroke(new Vector2(-10, 0), new Vector2(10, 0));
        var fake = new TestInkItem(Vortice.Mathematics.Rect.FromLTRB(20.0f, 0.0f, 30.0f, 10.0f));

        var items = new List<IBoardInkItem> { stroke, fake };
        IBoardInkItem? hitOnFake = InkItemPickTest.HitTestTopMostInkItem(items, pointWorld: new Vector2(25.0f, 5.0f), toleranceWorld: 0.0f);
        IBoardInkItem? hitOnStroke = InkItemPickTest.HitTestTopMostInkItem(items, pointWorld: new Vector2(0.0f, 0.0f), toleranceWorld: 0.0f);

        Assert.Same(fake, hitOnFake);
        Assert.Same(stroke, hitOnStroke);
    }

    // 形状（Line/Arrow）：按“点到线段距离 ≤ 容差 + 半宽”命中，不落入包围盒误命中区
    [Fact]
    public void IsInkItemHitByPoint_ShapeLine_HitsNearSegment_AndMissesBeyond()
    {
        // Width=4 → 半宽 2；点到线段距离 2 命中，2.5 不命中。
        var line = new BoardShape(BoardShapeKind.Line) { Width = 4.0f };
        line.SetGeometry(new Vector2(0, 0), new Vector2(10, 0));

        Assert.True(InkItemPickTest.IsInkItemHitByPoint(line, new Vector2(5.0f, 2.0f), toleranceWorld: 0.0f));
        Assert.False(InkItemPickTest.IsInkItemHitByPoint(line, new Vector2(5.0f, 2.5f), toleranceWorld: 0.0f));
    }

    [Fact]
    public void IsInkItemHitByPoint_ShapeLine_RespectsTolerance()
    {
        // Width=2 → 半宽 1；容差 1 时命中半径 2，容差 0 时为 1。
        var line = new BoardShape(BoardShapeKind.Line) { Width = 2.0f };
        line.SetGeometry(new Vector2(0, 0), new Vector2(10, 0));

        Assert.True(InkItemPickTest.IsInkItemHitByPoint(line, new Vector2(5.0f, 1.5f), toleranceWorld: 1.0f));
        Assert.False(InkItemPickTest.IsInkItemHitByPoint(line, new Vector2(5.0f, 1.5f), toleranceWorld: 0.0f));
    }

    [Fact]
    public void IsInkItemHitByPoint_ShapeLine_DiagonalMissesInsideAabbCorner()
    {
        // 斜线的 AABB 角部远离线段：即使位于 AABB 内也不命中（走线段距离而非包围盒）。
        var line = new BoardShape(BoardShapeKind.Line) { Width = 2.0f };
        line.SetGeometry(new Vector2(0, 0), new Vector2(10, 10));

        Assert.False(InkItemPickTest.IsInkItemHitByPoint(line, new Vector2(9.0f, 0.0f), toleranceWorld: 0.0f));
        Assert.True(InkItemPickTest.IsInkItemHitByPoint(line, new Vector2(5.0f, 5.0f), toleranceWorld: 0.0f));
    }

    [Fact]
    public void IsInkItemHitByPoint_ShapeArrow_FollowsSameSegmentRuleAsLine()
    {
        var arrow = new BoardShape(BoardShapeKind.Arrow) { Width = 4.0f };
        arrow.SetGeometry(new Vector2(0, 0), new Vector2(10, 0));

        Assert.True(InkItemPickTest.IsInkItemHitByPoint(arrow, new Vector2(5.0f, 2.0f), toleranceWorld: 0.0f));
        Assert.False(InkItemPickTest.IsInkItemHitByPoint(arrow, new Vector2(5.0f, 5.0f), toleranceWorld: 0.0f));
    }

    // 形状（Rectangle/Ellipse）：沿用 AABB 路径，内部点选命中
    [Fact]
    public void IsInkItemHitByPoint_ShapeRectangle_HitsInsideBounds()
    {
        var rect = new BoardShape(BoardShapeKind.Rectangle) { Width = 2.0f };
        rect.SetGeometry(new Vector2(0, 0), new Vector2(10, 10));

        Assert.True(InkItemPickTest.IsInkItemHitByPoint(rect, new Vector2(5.0f, 5.0f), toleranceWorld: 0.0f));
        Assert.False(InkItemPickTest.IsInkItemHitByPoint(rect, new Vector2(20.0f, 5.0f), toleranceWorld: 0.0f));
    }

    [Fact]
    public void IsInkItemHitByPoint_ShapeEllipse_HitsInsideBounds()
    {
        var ellipse = new BoardShape(BoardShapeKind.Ellipse) { Width = 2.0f };
        ellipse.SetGeometry(new Vector2(0, 0), new Vector2(10, 10));

        Assert.True(InkItemPickTest.IsInkItemHitByPoint(ellipse, new Vector2(5.0f, 5.0f), toleranceWorld: 0.0f));
        Assert.False(InkItemPickTest.IsInkItemHitByPoint(ellipse, new Vector2(50.0f, 5.0f), toleranceWorld: 0.0f));
    }
}
