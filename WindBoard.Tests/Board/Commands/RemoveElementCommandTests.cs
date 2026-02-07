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
        var doc = new BoardDocument();
        var a = new BoardTextElement { Text = "a" };
        var b = new BoardTextElement { Text = "b" };
        var c = new BoardTextElement { Text = "c" };

        doc.ElementsBelowInk.Add(a);
        doc.ElementsBelowInk.Add(b);
        doc.ElementsBelowInk.Add(c);

        var cmd = new RemoveElementCommand(b);

        cmd.Do(doc);
        Assert.Equal(2, doc.ElementsBelowInk.Count);
        Assert.Same(a, doc.ElementsBelowInk[0]);
        Assert.Same(c, doc.ElementsBelowInk[1]);

        cmd.Undo(doc);
        Assert.Equal(3, doc.ElementsBelowInk.Count);
        Assert.Same(a, doc.ElementsBelowInk[0]);
        Assert.Same(b, doc.ElementsBelowInk[1]);
        Assert.Same(c, doc.ElementsBelowInk[2]);
    }

    [Fact]
    public void Do_Undo_RemovesFromAboveInkAndRestoresIndex()
    {
        var doc = new BoardDocument();
        var a = new BoardTextElement { Text = "a" };
        var b = new BoardTextElement { Text = "b" };
        var c = new BoardTextElement { Text = "c" };

        doc.ElementsAboveInk.Add(a);
        doc.ElementsAboveInk.Add(b);
        doc.ElementsAboveInk.Add(c);

        var cmd = new RemoveElementCommand(b);

        cmd.Do(doc);
        Assert.Equal(2, doc.ElementsAboveInk.Count);
        Assert.Same(a, doc.ElementsAboveInk[0]);
        Assert.Same(c, doc.ElementsAboveInk[1]);

        cmd.Undo(doc);
        Assert.Equal(3, doc.ElementsAboveInk.Count);
        Assert.Same(a, doc.ElementsAboveInk[0]);
        Assert.Same(b, doc.ElementsAboveInk[1]);
        Assert.Same(c, doc.ElementsAboveInk[2]);
    }
}

