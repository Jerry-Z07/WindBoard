using System.Collections.Generic;
using WindBoard.Board;
using WindBoard.Board.Commands;
using Xunit;

namespace WindBoard.Tests.Board.Commands;

public sealed class CompositeCommandTests
{
    // 复合命令应按顺序 Do，并按“反向顺序” Undo，保证可恢复到原始状态
    [Fact]
    public void Do_Undo_ExecutesInOrderAndUndoesInReverseOrder()
    {
        var doc = new BoardDocument();
        var a = new Stroke();
        var b = new Stroke();
        var c = new Stroke();
        doc.Strokes.Add(a);
        doc.Strokes.Add(b);
        doc.Strokes.Add(c);

        // 先把 b 置顶，再把 a 置顶：最终应为 [c, b, a]
        var command = new CompositeCommand(new List<IBoardCommand>
        {
            new BringStrokeToFrontCommand(b),
            new BringStrokeToFrontCommand(a),
        });

        command.Do(doc);
        Assert.Equal(3, doc.Strokes.Count);
        Assert.Same(c, doc.Strokes[0]);
        Assert.Same(b, doc.Strokes[1]);
        Assert.Same(a, doc.Strokes[2]);

        command.Undo(doc);
        Assert.Equal(3, doc.Strokes.Count);
        Assert.Same(a, doc.Strokes[0]);
        Assert.Same(b, doc.Strokes[1]);
        Assert.Same(c, doc.Strokes[2]);
    }
}

