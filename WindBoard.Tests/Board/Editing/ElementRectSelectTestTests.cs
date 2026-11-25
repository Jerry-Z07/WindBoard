using System.Collections.Generic;
using System.Numerics;
using WindBoard.Board.Editing;
using WindBoard.Board.Elements;
using Xunit;

namespace WindBoard.Tests.Board.Editing;

public sealed class ElementRectSelectTestTests
{
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

