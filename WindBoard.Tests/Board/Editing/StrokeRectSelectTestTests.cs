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

    // 与矩形无交集时不应命中
    [Fact]
    public void IsStrokeIntersectWorldRect_ReturnsFalse_WhenDisjoint()
    {
        Stroke s = StrokeTestFactory.CreateStroke(new Vector2(0, 0), new Vector2(10, 0));

        bool hit = StrokeRectSelectTest.IsStrokeIntersectWorldRect(s, minWorld: new Vector2(0, 100), maxWorld: new Vector2(10, 110));

        Assert.False(hit);
    }
}

