using System.Windows.Ink;
using System.Windows.Input;
using WindBoard.Services;
using Xunit;

namespace WindBoard.Tests.Services;

public sealed class StrokeUndoHistoryTests
{
    [StaFact]
    public void Transaction_AddStroke_CanUndoUndoRedo()
    {
        var strokes = new StrokeCollection();
        var history = new StrokeUndoHistory();
        strokes.StrokesChanged += (_, e) => history.Record(e);

        history.Begin();
        var s1 = CreateStroke();
        strokes.Add(s1);
        history.End();

        Assert.True(history.CanUndo);
        Assert.Contains(s1, strokes);

        history.Undo(strokes);
        Assert.DoesNotContain(s1, strokes);
        Assert.True(history.CanRedo);

        history.Redo(strokes);
        Assert.Contains(s1, strokes);
    }

    [StaFact]
    public void Transaction_AddThenRemoveSameStroke_ProducesNoUndo()
    {
        var strokes = new StrokeCollection();
        var history = new StrokeUndoHistory();
        strokes.StrokesChanged += (_, e) => history.Record(e);

        history.Begin();
        var s1 = CreateStroke();
        strokes.Add(s1);
        strokes.Remove(s1);
        history.End();

        Assert.False(history.CanUndo);
        Assert.DoesNotContain(s1, strokes);
    }

    [StaFact]
    public void SuspendRecording_PreventsTransactionsFromBeingCaptured()
    {
        var strokes = new StrokeCollection();
        var history = new StrokeUndoHistory();
        strokes.StrokesChanged += (_, e) => history.Record(e);

        using (history.SuspendRecording())
        {
            history.Begin();
            strokes.Add(CreateStroke());
            history.End();
        }

        Assert.False(history.CanUndo);
    }

    private static Stroke CreateStroke()
    {
        var points = new StylusPointCollection
        {
            new StylusPoint(0, 0),
            new StylusPoint(1, 1)
        };
        return new Stroke(points);
    }
}
