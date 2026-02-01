using System.Collections.Generic;
using WindBoard.Board;
using WindBoard.Board.Commands;
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
        document.Strokes.Add(a);
        document.Strokes.Add(b);

        var snapshot = new List<Stroke>(document.Strokes);
        var command = new ClearCommand(snapshot);

        command.Do(document);
        Assert.Empty(document.Strokes);

        command.Undo(document);
        Assert.Equal(2, document.Strokes.Count);
        Assert.Same(a, document.Strokes[0]);
        Assert.Same(b, document.Strokes[1]);
    }
}
