using WindBoard.Models.InkV2;
using WindBoard.Services.InkV2;
using Xunit;

namespace WindBoard.Tests.InkV2
{
    public sealed class InkSpatialIndexTests
    {
        [Fact]
        public void HitTestPoint_NearSegment_ReturnsHit()
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

            InkPointHitTestResult? hit = index.HitTestPoint(xDip: 5, yDip: 1, radiusDip: 2);

            Assert.True(hit.HasValue);
            Assert.Same(stroke, hit.Value.Stroke);
            Assert.Same(fragment, hit.Value.Fragment);
            Assert.InRange(hit.Value.DistanceDip, 0.999, 1.001);
        }

        [Fact]
        public void QueryRect_IntersectingSegment_ReturnsHits()
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

            var hits = index.QueryRect(new InkRectDip(x: 4, y: -1, width: 2, height: 2));

            Assert.NotEmpty(hits);
            Assert.Contains(hits, h => ReferenceEquals(h.Stroke, stroke) && ReferenceEquals(h.Fragment, fragment));
        }

        [Fact]
        public void AddStroke_QueryRect_IntersectingSegment_ReturnsHits()
        {
            var stroke = new InkStroke(InkTool.CreateDefault());
            var fragment = new InkFragment();
            fragment.Points.Add(new InkPoint(4000, 4000));
            fragment.Points.Add(new InkPoint(4100, 4000));
            stroke.Fragments.Add(fragment);

            var index = new InkSpatialIndex(cellSizeDip: 72);
            index.AddStroke(stroke);

            var hits = index.QueryRect(new InkRectDip(x: 3980, y: 3980, width: 200, height: 80));

            Assert.NotEmpty(hits);
            Assert.Contains(hits, h => ReferenceEquals(h.Stroke, stroke) && ReferenceEquals(h.Fragment, fragment));
        }
    }
}
