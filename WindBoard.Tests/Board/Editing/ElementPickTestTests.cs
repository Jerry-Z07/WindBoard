using System.Collections.Generic;
using System.Numerics;
using WindBoard.Board.Editing;
using WindBoard.Board.Elements;
using Xunit;

namespace WindBoard.Tests.Board.Editing;

public sealed class ElementPickTestTests
{
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

