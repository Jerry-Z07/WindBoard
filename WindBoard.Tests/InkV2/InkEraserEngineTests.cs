using WindBoard.Models.InkV2;
using WindBoard.Services.InkV2;
using Xunit;

namespace WindBoard.Tests.InkV2
{
    public sealed class InkEraserEngineTests
    {
        [Fact]
        public void EraseCircle_SegmentCrossingCircle_SplitsIntoTwoFragments()
        {
            var doc = new InkDocument();
            var stroke = new InkStroke(InkTool.CreateDefault());
            var fragment = new InkFragment();
            fragment.Points.Add(new InkPoint(0, 0));
            fragment.Points.Add(new InkPoint(10, 0));
            stroke.Fragments.Add(fragment);
            doc.Strokes.Add(stroke);

            var index = new InkSpatialIndex(cellSizeDip: 4);
            index.Rebuild(doc);

            var undo = new InkUndoHistory();
            undo.Begin();
            bool changed = InkEraserEngine.EraseCircle(doc, index, undo, centerXDip: 5, centerYDip: 0, radiusDip: 1);
            undo.End();

            Assert.True(changed);
            Assert.Single(doc.Strokes);
            Assert.Equal(2, stroke.Fragments.Count);

            InkFragment left = stroke.Fragments[0];
            InkFragment right = stroke.Fragments[1];

            Assert.Equal(2, left.Points.Count);
            Assert.Equal(2, right.Points.Count);

            Assert.InRange(left.Points[0].XDip, -0.001, 0.001);
            Assert.InRange(left.Points[1].XDip, 3.999, 4.001);
            Assert.InRange(right.Points[0].XDip, 5.999, 6.001);
            Assert.InRange(right.Points[1].XDip, 9.999, 10.001);
        }

        [Fact]
        public void EraseCircle_UndoRedo_RestoresOriginalFragment()
        {
            var doc = new InkDocument();
            var stroke = new InkStroke(InkTool.CreateDefault());
            var original = new InkFragment();
            original.Points.Add(new InkPoint(0, 0));
            original.Points.Add(new InkPoint(10, 0));
            stroke.Fragments.Add(original);
            doc.Strokes.Add(stroke);

            var index = new InkSpatialIndex(cellSizeDip: 4);
            index.Rebuild(doc);

            var undo = new InkUndoHistory();
            undo.Begin();
            _ = InkEraserEngine.EraseCircle(doc, index, undo, centerXDip: 5, centerYDip: 0, radiusDip: 1);
            undo.End();

            Assert.True(undo.CanUndo);
            Assert.Equal(2, stroke.Fragments.Count);
            Assert.DoesNotContain(original, stroke.Fragments);

            undo.Undo(doc);

            Assert.True(undo.CanRedo);
            Assert.Single(stroke.Fragments);
            Assert.Same(original, stroke.Fragments[0]);
            Assert.Equal(2, stroke.Fragments[0].Points.Count);

            undo.Redo(doc);

            Assert.Equal(2, stroke.Fragments.Count);
            Assert.DoesNotContain(original, stroke.Fragments);
        }

        [Fact]
        public void EraseCircle_EntireStroke_RemovesStroke_UndoRestores()
        {
            var doc = new InkDocument();
            var stroke = new InkStroke(InkTool.CreateDefault());
            var fragment = new InkFragment();
            fragment.Points.Add(new InkPoint(0, 0));
            fragment.Points.Add(new InkPoint(10, 0));
            stroke.Fragments.Add(fragment);
            doc.Strokes.Add(stroke);

            var index = new InkSpatialIndex(cellSizeDip: 4);
            index.Rebuild(doc);

            var undo = new InkUndoHistory();
            undo.Begin();
            bool changed = InkEraserEngine.EraseCircle(doc, index, undo, centerXDip: 5, centerYDip: 0, radiusDip: 100);
            undo.End();

            Assert.True(changed);
            Assert.Empty(doc.Strokes);
            Assert.True(undo.CanUndo);

            undo.Undo(doc);

            Assert.Single(doc.Strokes);
            Assert.Same(stroke, doc.Strokes[0]);
        }

        [Fact]
        public void EraseRect_SegmentCrossingRect_SplitsIntoTwoFragments()
        {
            var doc = new InkDocument();
            var stroke = new InkStroke(InkTool.CreateDefault());
            var fragment = new InkFragment();
            fragment.Points.Add(new InkPoint(0, 0));
            fragment.Points.Add(new InkPoint(10, 0));
            stroke.Fragments.Add(fragment);
            doc.Strokes.Add(stroke);

            var index = new InkSpatialIndex(cellSizeDip: 4);
            index.Rebuild(doc);

            var undo = new InkUndoHistory();
            undo.Begin();
            bool changed = InkEraserEngine.EraseRect(doc, index, undo, new InkRectDip(x: 4, y: -1, width: 2, height: 2));
            undo.End();

            Assert.True(changed);
            Assert.Single(doc.Strokes);
            Assert.Equal(2, stroke.Fragments.Count);

            InkFragment left = stroke.Fragments[0];
            InkFragment right = stroke.Fragments[1];

            Assert.Equal(2, left.Points.Count);
            Assert.Equal(2, right.Points.Count);

            Assert.InRange(left.Points[1].XDip, 3.999, 4.001);
            Assert.InRange(right.Points[0].XDip, 5.999, 6.001);
        }
    }
}
