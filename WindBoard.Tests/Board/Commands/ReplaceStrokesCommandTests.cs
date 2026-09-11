using System.Collections.Generic;
using WindBoard.Board;
using WindBoard.Board.Commands;
using WindBoard.Board.Items;
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

        document.InkItems.Add(a);
        document.InkItems.Add(b);
        document.InkItems.Add(c);

        var before = new List<IBoardInkItem>(document.InkItems);
        var after = new List<IBoardInkItem> { a, c };

        var command = new ReplaceStrokesCommand(before, after);

        command.Do(document);
        Assert.Equal(2, document.InkItems.Count);
        Assert.Same(a, document.InkItems[0]);
        Assert.Same(c, document.InkItems[1]);

        command.Undo(document);
        Assert.Equal(3, document.InkItems.Count);
        Assert.Same(a, document.InkItems[0]);
        Assert.Same(b, document.InkItems[1]);
        Assert.Same(c, document.InkItems[2]);
    }
}
