using System.Collections.Generic;
using WindBoard.Board;
using WindBoard.Board.Commands;
using Xunit;

namespace WindBoard.Tests.Board.Commands;

public sealed class ReplaceStrokesCommandTests
{
    // Do：会把笔迹替换为 after 快照；Undo：会恢复 before 快照
    [Fact]
    public void Do_ReplacesStrokesWithAfterSnapshot_UndoRestoresBeforeSnapshot()
    {
        var document = new BoardDocument();
        var a = new Stroke();
        var b = new Stroke();
        var c = new Stroke();

        document.Strokes.Add(a);
        document.Strokes.Add(b);
        document.Strokes.Add(c);

        var before = new List<Stroke>(document.Strokes);
        var after = new List<Stroke> { a, c };

        var command = new ReplaceStrokesCommand(before, after);

        command.Do(document);
        Assert.Equal(2, document.Strokes.Count);
        Assert.Same(a, document.Strokes[0]);
        Assert.Same(c, document.Strokes[1]);

        command.Undo(document);
        Assert.Equal(3, document.Strokes.Count);
        Assert.Same(a, document.Strokes[0]);
        Assert.Same(b, document.Strokes[1]);
        Assert.Same(c, document.Strokes[2]);
    }
}
