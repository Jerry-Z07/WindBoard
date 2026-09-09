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
}
