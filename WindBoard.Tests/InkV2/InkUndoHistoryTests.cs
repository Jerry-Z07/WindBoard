using WindBoard.Models.InkV2;
using WindBoard.Services.InkV2;
using Xunit;

namespace WindBoard.Tests.InkV2;

public sealed class InkUndoHistoryTests
{
    [Fact]
    public void Transaction_InsertStroke_CanUndoRedo()
    {
        var doc = new InkDocument();
        var stroke = new InkStroke(InkTool.CreateDefault());
        stroke.Fragments.Add(new InkFragment());
        doc.Strokes.Add(stroke);

        var history = new InkUndoHistory();
        history.Begin();
        history.Record(new InsertStrokeCommand(0, stroke));
        history.End();

        Assert.True(history.CanUndo);
        Assert.Contains(stroke, doc.Strokes);

        history.Undo(doc);
        Assert.DoesNotContain(stroke, doc.Strokes);
        Assert.True(history.CanRedo);

        history.Redo(doc);
        Assert.Contains(stroke, doc.Strokes);
    }

    [Fact]
    public void SuspendRecording_PreventsTransactionsFromBeingCaptured()
    {
        var doc = new InkDocument();
        var stroke = new InkStroke(InkTool.CreateDefault());
        stroke.Fragments.Add(new InkFragment());
        doc.Strokes.Add(stroke);

        var history = new InkUndoHistory();

        using (history.SuspendRecording())
        {
            history.Begin();
            history.Record(new InsertStrokeCommand(0, stroke));
            history.End();
        }

        Assert.False(history.CanUndo);
    }
}

