using System.Collections.Generic;
using System.Numerics;
using WindBoard.Board.Editing;
using WindBoard.Board.Elements;
using Xunit;

namespace WindBoard.Tests.Board.Editing;

public sealed class ElementPickTestTests
{
    // 点选重叠区域时应优先命中“更靠上”的元素（列表末尾）
    [Fact]
    public void HitTestTopMostElement_PrefersLastElement()
    {
        var a = new BoardTextElement
        {
            PositionWorld = new Vector2(0, 0),
            SizeWorld = new Vector2(10, 10),
        };
        var b = new BoardTextElement
        {
            PositionWorld = new Vector2(0, 0),
            SizeWorld = new Vector2(10, 10),
        };

        var elements = new List<BoardElement> { a, b };
        BoardElement? hit = ElementPickTest.HitTestTopMostElement(elements, pointWorld: new Vector2(5, 5), toleranceWorld: 0.0f);

        Assert.Same(b, hit);
    }

    // 远离元素时不应命中
    [Fact]
    public void HitTestTopMostElement_ReturnsNull_WhenOutside()
    {
        var a = new BoardTextElement
        {
            PositionWorld = new Vector2(0, 0),
            SizeWorld = new Vector2(10, 10),
        };

        var elements = new List<BoardElement> { a };
        BoardElement? hit = ElementPickTest.HitTestTopMostElement(elements, pointWorld: new Vector2(100, 100), toleranceWorld: 0.0f);

        Assert.Null(hit);
    }
}

