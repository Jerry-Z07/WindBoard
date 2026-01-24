using WindBoard.Board;
using WindBoard.Board.Commands;
using WindBoard.Board.Editing;
using Xunit;

namespace WindBoard.Tests.Board.Editing;

public sealed class BoardSessionTests
{
    [Fact]
    public void Execute_Undo_Redo_会正确更新状态并触发事件()
    {
        var session = new BoardSession();
        int stateChangedCount = 0;
        session.StateChanged += () => stateChangedCount++;

        var stroke = new Stroke();
        session.Execute(new AddStrokeCommand(stroke));

        Assert.True(session.CanUndo);
        Assert.False(session.CanRedo);
        Assert.True(session.HasStrokes);
        Assert.Single(session.Document.Strokes);
        Assert.Equal(1, stateChangedCount);

        session.Undo();
        Assert.False(session.HasStrokes);
        Assert.True(session.CanRedo);
        Assert.Equal(2, stateChangedCount);

        session.Redo();
        Assert.True(session.HasStrokes);
        Assert.False(session.CanRedo);
        Assert.Equal(3, stateChangedCount);
    }

    [Fact]
    public void Undo_Redo_在无历史时是安全的空操作()
    {
        var session = new BoardSession();
        int stateChangedCount = 0;
        session.StateChanged += () => stateChangedCount++;

        session.Undo();
        session.Redo();

        Assert.False(session.CanUndo);
        Assert.False(session.CanRedo);
        Assert.Equal(0, stateChangedCount);
    }

    [Fact]
    public void Execute_会清空_Redo_栈()
    {
        var session = new BoardSession();

        var a = new Stroke();
        session.Execute(new AddStrokeCommand(a));
        session.Undo();
        Assert.True(session.CanRedo);

        var b = new Stroke();
        session.Execute(new AddStrokeCommand(b));

        Assert.False(session.CanRedo);
        Assert.Single(session.Document.Strokes);
        Assert.Same(b, session.Document.Strokes[0]);
    }

    [Fact]
    public void ClearAll_有笔迹时会清空并可撤销()
    {
        var session = new BoardSession();
        var a = new Stroke();
        var b = new Stroke();
        session.Execute(new AddStrokeCommand(a));
        session.Execute(new AddStrokeCommand(b));

        session.ClearAll();
        Assert.Empty(session.Document.Strokes);
        Assert.True(session.CanUndo);

        session.Undo();
        Assert.Equal(2, session.Document.Strokes.Count);
        Assert.Same(a, session.Document.Strokes[0]);
        Assert.Same(b, session.Document.Strokes[1]);
    }

    [Fact]
    public void ClearAll_无笔迹时不会产生撤销记录()
    {
        var session = new BoardSession();

        session.ClearAll();

        Assert.False(session.CanUndo);
        Assert.False(session.HasStrokes);
    }
}

