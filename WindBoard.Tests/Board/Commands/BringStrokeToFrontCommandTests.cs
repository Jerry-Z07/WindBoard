using WindBoard.Board;
using WindBoard.Board.Commands;
using Xunit;

namespace WindBoard.Tests.Board.Commands;

public sealed class BringStrokeToFrontCommandTests
{
    // Do/Undo 应能把指定笔迹移动到末尾，并可撤销恢复
    [Fact]
    public void Do_Undo_MovesStrokeToEndAndRestores()
    {
        var doc = new BoardDocument();
        var a = new Stroke();
        var b = new Stroke();
        var c = new Stroke();
        doc.InkItems.Add(a);
        doc.InkItems.Add(b);
        doc.InkItems.Add(c);

        var command = new BringStrokeToFrontCommand(b);

        command.Do(doc);
        Assert.Equal(3, doc.InkItems.Count);
        Assert.Same(a, doc.InkItems[0]);
        Assert.Same(c, doc.InkItems[1]);
        Assert.Same(b, doc.InkItems[2]);

        command.Undo(doc);
        Assert.Equal(3, doc.InkItems.Count);
        Assert.Same(a, doc.InkItems[0]);
        Assert.Same(b, doc.InkItems[1]);
        Assert.Same(c, doc.InkItems[2]);
    }
}

