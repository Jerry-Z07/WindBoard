using System.Collections.Generic;
using System.Numerics;
using WindBoard.Board.Editing;
using WindBoard.Board.Elements;
using Xunit;

namespace WindBoard.Tests.Board.Editing;

public sealed class ElementRectSelectTestTests
{
    // 框选重叠区域时应优先命中“更靠上”的元素（列表末尾）
    [Fact]
    public void HitTestTopMostElementInWorldRect_PrefersLastElement()
    {
        var a = new BoardTextElement
        {
            PositionWorld = new Vector2(0, 0),
            SizeWorld = new Vector2(10, 10),
        };
        var b = new BoardTextElement
        {
            PositionWorld = new Vector2(5, 5),
            SizeWorld = new Vector2(10, 10),
        };

        var elements = new List<BoardElement> { a, b };
        BoardElement? hit = ElementRectSelectTest.HitTestTopMostElementInWorldRect(elements, minWorld: new Vector2(6, 6), maxWorld: new Vector2(7, 7));

        Assert.Same(b, hit);
    }

    // 框选区域与元素无交集时不应命中
    [Fact]
    public void HitTestTopMostElementInWorldRect_ReturnsNull_WhenNoIntersection()
    {
        var a = new BoardTextElement
        {
            PositionWorld = new Vector2(0, 0),
            SizeWorld = new Vector2(10, 10),
        };

        var elements = new List<BoardElement> { a };
        BoardElement? hit = ElementRectSelectTest.HitTestTopMostElementInWorldRect(elements, minWorld: new Vector2(100, 100), maxWorld: new Vector2(120, 120));

        Assert.Null(hit);
    }
}

