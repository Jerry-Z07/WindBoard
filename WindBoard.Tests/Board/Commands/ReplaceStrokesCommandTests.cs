using System.Collections.Generic;
using WindBoard.Board;
using WindBoard.Board.Commands;
using Xunit;

namespace WindBoard.Tests.Board.Commands;

public sealed class ReplaceStrokesCommandTests
{
    [Fact]
    public void Do_会把笔迹替换为_after_快照_Undo_会恢复_before_快照()
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

