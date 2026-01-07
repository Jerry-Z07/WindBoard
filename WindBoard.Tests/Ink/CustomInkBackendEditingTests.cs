using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using WindBoard.Controls;
using WindBoard.Core.Ink.Backend;
using WindBoard.Models.Ink;
using WindBoard.Services.Ink;
using Xunit;

namespace WindBoard.Tests.Ink;

public sealed class CustomInkBackendEditingTests
{
    [StaFact]
    public void Erase_SplitsStroke_ProducesFragments()
    {
        var surface = new InkSurface();
        using var backend = new CustomInkBackend(surface);

        var doc = new List<InkStrokeModel>();
        var originalId = Guid.NewGuid();
        var stroke = new InkStrokeModel
        {
            Id = originalId,
            ZoomAtCreation = 1.0,
            Style = new InkStrokeStyle(InkBrushKind.Pen, Colors.White, LogicalThicknessDip: 4.0, UsesPressure: false)
        };
        for (int x = 0; x <= 100; x++)
        {
            stroke.Points.Add(new InkPoint(x, 0, 0.5f, TimestampTicks: 0));
        }
        doc.Add(stroke);

        InkStrokeCollectionChangedEventArgs? last = null;
        backend.StrokesChanged += (_, e) => last = e;

        backend.BindDocument(doc);

        bool changed = backend.Erase(new Rect(45, -10, 10, 20));

        Assert.True(changed);
        Assert.NotNull(last);
        Assert.Single(last!.Removed);
        Assert.Equal(2, last.Added.Count);
        Assert.Equal(2, doc.Count);
        Assert.DoesNotContain(doc, s => s.Id == originalId);
    }

    [StaFact]
    public void Erase_UndoRedo_RestoresOriginalStroke()
    {
        var surface = new InkSurface();
        using var backend = new CustomInkBackend(surface);

        var doc = new List<InkStrokeModel>();
        var originalId = Guid.NewGuid();
        var stroke = new InkStrokeModel
        {
            Id = originalId,
            ZoomAtCreation = 1.0,
            Style = new InkStrokeStyle(InkBrushKind.Pen, Colors.White, LogicalThicknessDip: 4.0, UsesPressure: false)
        };
        for (int x = 0; x <= 100; x++)
        {
            stroke.Points.Add(new InkPoint(x, 0, 0.5f, TimestampTicks: 0));
        }
        doc.Add(stroke);

        var history = new InkStrokeUndoHistory();
        backend.StrokesChanged += (_, e) => history.Record(e.Added, e.Removed);

        backend.BindDocument(doc);

        history.Begin();
        backend.Erase(new Rect(45, -10, 10, 20));
        history.End();

        Assert.NotEqual(1, doc.Count);

        history.Undo(doc);
        Assert.Single(doc);
        Assert.Equal(originalId, doc[0].Id);

        history.Redo(doc);
        Assert.NotEqual(1, doc.Count);
    }

    [StaFact]
    public void SelectAtPoint_DeletesTopmostStroke()
    {
        var surface = new InkSurface();
        using var backend = new CustomInkBackend(surface);

        var bottom = CreateLineStroke(Colors.Red, id: Guid.NewGuid());
        var top = CreateLineStroke(Colors.Blue, id: Guid.NewGuid());

        var doc = new List<InkStrokeModel> { bottom, top };
        backend.BindDocument(doc);

        backend.SelectAtPoint(new Point(10, 0), toggle: false);
        Assert.True(backend.HasSelection);

        backend.DeleteSelection();

        Assert.Single(doc);
        Assert.Equal(bottom.Id, doc[0].Id);
    }

    [StaFact]
    public void CopySelection_ReplacesSelectionAndCanDeleteCopiedStroke()
    {
        var surface = new InkSurface();
        using var backend = new CustomInkBackend(surface);

        var original = CreateLineStroke(Colors.White, id: Guid.NewGuid());
        var doc = new List<InkStrokeModel> { original };
        backend.BindDocument(doc);

        backend.SelectAtPoint(new Point(10, 0), toggle: false);
        Assert.True(backend.CopySelection(20, 20, replaceSelection: true));

        Assert.Equal(2, doc.Count);

        backend.DeleteSelection();

        Assert.Single(doc);
        Assert.Equal(original.Id, doc[0].Id);
    }

    [StaFact]
    public void MoveSelection_TranslatesPoints()
    {
        var surface = new InkSurface();
        using var backend = new CustomInkBackend(surface);

        var original = CreateLineStroke(Colors.White, id: Guid.NewGuid());
        var doc = new List<InkStrokeModel> { original };
        backend.BindDocument(doc);

        backend.SelectAtPoint(new Point(10, 0), toggle: false);
        Assert.True(backend.MoveSelection(10, 5));

        Assert.Single(doc);
        var moved = doc.Single();
        Assert.Equal(original.Id, moved.Id);
        Assert.Equal(10, moved.Points[0].X, precision: 6);
        Assert.Equal(5, moved.Points[0].Y, precision: 6);
    }

    private static InkStrokeModel CreateLineStroke(Color color, Guid id)
    {
        var stroke = new InkStrokeModel
        {
            Id = id,
            ZoomAtCreation = 1.0,
            Style = new InkStrokeStyle(InkBrushKind.Pen, color, LogicalThicknessDip: 3.0, UsesPressure: false)
        };

        for (int x = 0; x <= 100; x += 10)
        {
            stroke.Points.Add(new InkPoint(x, 0, 0.5f, TimestampTicks: 0));
        }

        return stroke;
    }
}
