using WindBoard.Board;
using WindBoard.Board.Commands;
using Xunit;

namespace WindBoard.Tests.Board.Commands;

public sealed class RemoveStrokeCommandTests
{
    // Do/Undo 应能删除并按原索引插回
    [Fact]
    public void Do_Undo_RemovesAndRestoresAtOriginalIndex()
    {
        var doc = new BoardDocument();
        var a = new Stroke();
        var b = new Stroke();
        var c = new Stroke();
        doc.InkItems.Add(a);
        doc.InkItems.Add(b);
        doc.InkItems.Add(c);

        var command = new RemoveStrokeCommand(b);

        command.Do(doc);
        Assert.Equal(2, doc.InkItems.Count);
        Assert.Same(a, doc.InkItems[0]);
        Assert.Same(c, doc.InkItems[1]);

        command.Undo(doc);
        Assert.Equal(3, doc.InkItems.Count);
        Assert.Same(a, doc.InkItems[0]);
        Assert.Same(b, doc.InkItems[1]);
        Assert.Same(c, doc.InkItems[2]);
    }
}

