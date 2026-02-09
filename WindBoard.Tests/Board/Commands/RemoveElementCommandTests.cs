using System;
using System.Collections.Generic;
using WindBoard.Board;
using WindBoard.Board.Commands;
using WindBoard.Board.Elements;
using Xunit;

namespace WindBoard.Tests.Board.Commands;

public sealed class RemoveElementCommandTests
{
    [Fact]
    public void Do_Undo_RemovesFromBelowInkAndRestoresIndex()
    {
        AssertRemoveUndo(doc => doc.ElementsBelowInk);
    }

    [Fact]
    public void Do_Undo_RemovesFromAboveInkAndRestoresIndex()
    {
        AssertRemoveUndo(doc => doc.ElementsAboveInk);
    }

    private static void AssertRemoveUndo(Func<BoardDocument, List<BoardElement>> getLayer)
    {
        var doc = new BoardDocument();
        var a = new BoardTextElement { Text = "a" };
        var b = new BoardTextElement { Text = "b" };
        var c = new BoardTextElement { Text = "c" };

        List<BoardElement> layer = getLayer(doc);
        layer.Add(a);
        layer.Add(b);
        layer.Add(c);

        var cmd = new RemoveElementCommand(b);

        cmd.Do(doc);
        Assert.Equal(2, layer.Count);
        Assert.Same(a, layer[0]);
        Assert.Same(c, layer[1]);

        cmd.Undo(doc);
        Assert.Equal(3, layer.Count);
        Assert.Same(a, layer[0]);
        Assert.Same(b, layer[1]);
        Assert.Same(c, layer[2]);
    }
}
