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
        doc.Strokes.Add(a);
        doc.Strokes.Add(b);
        doc.Strokes.Add(c);

        var command = new RemoveStrokeCommand(b);

        command.Do(doc);
        Assert.Equal(2, doc.Strokes.Count);
        Assert.Same(a, doc.Strokes[0]);
        Assert.Same(c, doc.Strokes[1]);

        command.Undo(doc);
        Assert.Equal(3, doc.Strokes.Count);
        Assert.Same(a, doc.Strokes[0]);
        Assert.Same(b, doc.Strokes[1]);
        Assert.Same(c, doc.Strokes[2]);
    }
}

