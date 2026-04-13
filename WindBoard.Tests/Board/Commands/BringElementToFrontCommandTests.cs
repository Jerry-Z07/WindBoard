using System.Numerics;
using WindBoard.Board;
using WindBoard.Board.Commands;
using WindBoard.Board.Elements;
using Xunit;

namespace WindBoard.Tests.Board.Commands;

public sealed class BringElementToFrontCommandTests
{
    [Fact]
    public void Do_Undo_MovesFromBelowInkToAboveInkAndRestores()
    {
        var doc = new BoardDocument();
        var a = new BoardTextElement { Text = "a", PositionWorld = Vector2.Zero, SizeWorld = new Vector2(10, 10) };
        var b = new BoardTextElement { Text = "b", PositionWorld = Vector2.Zero, SizeWorld = new Vector2(10, 10) };

        doc.ElementsBelowInk.Add(a);
        doc.ElementsBelowInk.Add(b);

        var cmd = new BringElementToFrontCommand(a);

        cmd.Do(doc);
        BoardElement remainingBelowInk = Assert.Single(doc.ElementsBelowInk);
        Assert.Same(b, remainingBelowInk);
        Assert.Single(doc.ElementsAboveInk);
        Assert.Same(a, doc.ElementsAboveInk[0]);

        cmd.Undo(doc);
        Assert.Equal(2, doc.ElementsBelowInk.Count);
        Assert.Same(a, doc.ElementsBelowInk[0]);
        Assert.Same(b, doc.ElementsBelowInk[1]);
        Assert.Empty(doc.ElementsAboveInk);
    }

    [Fact]
    public void Do_Undo_MovesWithinAboveInkToEndAndRestores()
    {
        var doc = new BoardDocument();
        var a = new BoardTextElement { Text = "a" };
        var b = new BoardTextElement { Text = "b" };
        var c = new BoardTextElement { Text = "c" };

        doc.ElementsAboveInk.Add(a);
        doc.ElementsAboveInk.Add(b);
        doc.ElementsAboveInk.Add(c);

        var cmd = new BringElementToFrontCommand(b);

        cmd.Do(doc);
        Assert.Equal(3, doc.ElementsAboveInk.Count);
        Assert.Same(a, doc.ElementsAboveInk[0]);
        Assert.Same(c, doc.ElementsAboveInk[1]);
        Assert.Same(b, doc.ElementsAboveInk[2]);

        cmd.Undo(doc);
        Assert.Equal(3, doc.ElementsAboveInk.Count);
        Assert.Same(a, doc.ElementsAboveInk[0]);
        Assert.Same(b, doc.ElementsAboveInk[1]);
        Assert.Same(c, doc.ElementsAboveInk[2]);
    }
}

