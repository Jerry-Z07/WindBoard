using System.Collections.Generic;
using System.Numerics;
using WindBoard.Board;
using WindBoard.Board.Editing;
using Xunit;

namespace WindBoard.Tests.Board.Editing;

public sealed class StrokePickTestTests
{
    // 点击重叠区域时应优先选中“更靠上”的笔迹（列表末尾）
    [Fact]
    public void HitTestTopMostStroke_PrefersLastStroke()
    {
        Stroke a = StrokeTestFactory.CreateStroke(new Vector2(-10, 0), new Vector2(10, 0));
        Stroke b = StrokeTestFactory.CreateStroke(new Vector2(0, -10), new Vector2(0, 10));

        var strokes = new List<Stroke> { a, b };
        Stroke? hit = StrokePickTest.HitTestTopMostStroke(strokes, pointWorld: Vector2.Zero, toleranceWorld: 0.0f);

        Assert.Same(b, hit);
    }

    // 远离笔迹时不应命中
    [Fact]
    public void IsStrokeHitByPoint_ReturnsFalse_WhenFarAway()
    {
        Stroke s = StrokeTestFactory.CreateStroke(new Vector2(0, 0), new Vector2(10, 0));

        bool hit = StrokePickTest.IsStrokeHitByPoint(s, new Vector2(0, 100), toleranceWorld: 0.0f);

        Assert.False(hit);
    }

    // 单点笔迹应按“圆点”处理
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

        Assert.True(StrokePickTest.IsStrokeHitByPoint(stroke, new Vector2(0, 0), toleranceWorld: 0.0f));
        Assert.False(StrokePickTest.IsStrokeHitByPoint(stroke, new Vector2(0, 10), toleranceWorld: 0.0f));
    }
}

