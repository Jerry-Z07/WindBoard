using System.Collections.Generic;
using WindBoard.Board;
using WindBoard.Board.Commands;
using WindBoard.Board.Items;
using Xunit;

namespace WindBoard.Tests.Board.Commands;

public sealed class ClearCommandTests
{
    // Do：会清空笔迹；Undo：会恢复快照
    [Fact]
    public void Do_ClearsStrokes_Undo_RestoresSnapshot()
    {
        var document = new BoardDocument();
        var a = new Stroke();
        var b = new Stroke();
        document.InkItems.Add(a);
        document.InkItems.Add(b);

        var snapshot = new List<IBoardInkItem>(document.InkItems);
        var command = new ClearCommand(snapshot);

        command.Do(document);
        Assert.Empty(document.InkItems);

        command.Undo(document);
        Assert.Equal(2, document.InkItems.Count);
        Assert.Same(a, document.InkItems[0]);
        Assert.Same(b, document.InkItems[1]);
    }
}
