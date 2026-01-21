using System;
using System.Collections.Generic;
using WindBoard.Models.InkV2;

namespace WindBoard.Services.InkV2
{
    internal sealed class InkSpatialIndex
    {
        private const double MinSegmentLengthSquaredDip = 1e-10;
        private const double MinDistanceDenominatorSquaredDip = 1e-12;

        private readonly double _cellSizeDip;
        private readonly Dictionary<long, List<int>> _segmentGrid = new();
        private readonly List<Segment> _segments = new(1024);
        private readonly List<int> _segmentQueryStamp = new(1024);
        private int _queryStampCounter = 1;

        internal int SegmentCount => _segments.Count;
        internal int CellCount => _segmentGrid.Count;

        public InkSpatialIndex(double cellSizeDip = 72.0)
        {
            if (cellSizeDip <= 0) throw new ArgumentOutOfRangeException(nameof(cellSizeDip));
            _cellSizeDip = cellSizeDip;
        }

        public void Clear()
        {
            _segmentGrid.Clear();
            _segments.Clear();
            _segmentQueryStamp.Clear();
            _queryStampCounter = 1;
        }

        public void Rebuild(InkDocument document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));

            Clear();

            for (int si = 0; si < document.Strokes.Count; si++)
            {
                InkStroke stroke = document.Strokes[si];
                for (int fi = 0; fi < stroke.Fragments.Count; fi++)
                {
                    InkFragment fragment = stroke.Fragments[fi];
                    List<InkPoint> points = fragment.Points;
                    if (points.Count < 2) continue;

                    for (int pi = 0; pi < points.Count - 1; pi++)
                    {
                        InkPoint a = points[pi];
                        InkPoint b = points[pi + 1];
                        AddSegment(
                            stroke,
                            fragment,
                            startPointIndex: pi,
                            ax: a.XDip,
                            ay: a.YDip,
                            bx: b.XDip,
                            by: b.YDip);
                    }
                }
            }
        }

        public void AddStroke(InkStroke stroke)
        {
            if (stroke == null) throw new ArgumentNullException(nameof(stroke));

            for (int fi = 0; fi < stroke.Fragments.Count; fi++)
            {
                InkFragment fragment = stroke.Fragments[fi];
                List<InkPoint> points = fragment.Points;
                if (points.Count < 2) continue;

                for (int pi = 0; pi < points.Count - 1; pi++)
                {
                    InkPoint a = points[pi];
                    InkPoint b = points[pi + 1];
                    AddSegment(
                        stroke,
                        fragment,
                        startPointIndex: pi,
                        ax: a.XDip,
                        ay: a.YDip,
                        bx: b.XDip,
                        by: b.YDip);
                }
            }
        }

        public InkPointHitTestResult? HitTestPoint(double xDip, double yDip, double radiusDip)
        {
            if (radiusDip <= 0) return null;

            double radiusSquared = radiusDip * radiusDip;
            double bestDistanceSquaredDip = radiusSquared;
            int bestSegmentIndex = -1;
            double bestClosestX = 0;
            double bestClosestY = 0;

            foreach (int segmentIndex in EnumerateCandidateSegments(xDip, yDip, radiusDip))
            {
                Segment seg = _segments[segmentIndex];
                double closestX;
                double closestY;
                double d2 = DistancePointToSegmentSquared(
                    xDip,
                    yDip,
                    seg.Ax,
                    seg.Ay,
                    seg.Bx,
                    seg.By,
                    out closestX,
                    out closestY);
                if (d2 < bestDistanceSquaredDip)
                {
                    bestDistanceSquaredDip = d2;
                    bestSegmentIndex = segmentIndex;
                    bestClosestX = closestX;
                    bestClosestY = closestY;
                }
            }

            if (bestSegmentIndex < 0)
            {
                return null;
            }

            Segment best = _segments[bestSegmentIndex];
            return new InkPointHitTestResult(
                best.Stroke,
                best.Fragment,
                best.StartPointIndex,
                Math.Sqrt(bestDistanceSquaredDip),
                bestClosestX,
                bestClosestY);
        }

        public List<InkSegmentHit> QueryRect(InkRectDip rect)
        {
            var hits = new List<InkSegmentHit>(64);
            QueryRect(rect, hits);
            return hits;
        }

        public void QueryRect(InkRectDip rect, List<InkSegmentHit> hits)
        {
            if (hits == null) throw new ArgumentNullException(nameof(hits));

            hits.Clear();
            if (rect.Width <= 0 || rect.Height <= 0) return;

            int minCellX = (int)Math.Floor(rect.Left / _cellSizeDip);
            int maxCellX = (int)Math.Floor(rect.Right / _cellSizeDip);
            int minCellY = (int)Math.Floor(rect.Top / _cellSizeDip);
            int maxCellY = (int)Math.Floor(rect.Bottom / _cellSizeDip);

            int stamp = NextQueryStamp();
            for (int cx = minCellX; cx <= maxCellX; cx++)
            {
                for (int cy = minCellY; cy <= maxCellY; cy++)
                {
                    long key = PackCell(cx, cy);
                    if (!_segmentGrid.TryGetValue(key, out var indices))
                    {
                        continue;
                    }

                    for (int i = 0; i < indices.Count; i++)
                    {
                        int segmentIndex = indices[i];
                        EnsureStampCapacity(segmentIndex);
                        if (_segmentQueryStamp[segmentIndex] == stamp)
                        {
                            continue;
                        }
                        _segmentQueryStamp[segmentIndex] = stamp;

                        Segment seg = _segments[segmentIndex];
                        if (!rect.Intersects(seg.MinX, seg.MinY, seg.MaxX, seg.MaxY))
                        {
                            continue;
                        }

                        hits.Add(new InkSegmentHit(seg.Stroke, seg.Fragment, seg.StartPointIndex));
                    }
                }
            }
        }

        public List<InkSegmentHit> QueryCircle(double centerXDip, double centerYDip, double radiusDip)
        {
            var hits = new List<InkSegmentHit>(64);
            if (radiusDip <= 0) return hits;

            double radiusSquaredDip = radiusDip * radiusDip;
            int stamp = NextQueryStamp();

            foreach (int segmentIndex in EnumerateCandidateSegments(centerXDip, centerYDip, radiusDip))
            {
                EnsureStampCapacity(segmentIndex);
                if (_segmentQueryStamp[segmentIndex] == stamp)
                {
                    continue;
                }
                _segmentQueryStamp[segmentIndex] = stamp;

                Segment seg = _segments[segmentIndex];
                double d2 = DistancePointToSegmentSquared(
                    centerXDip,
                    centerYDip,
                    seg.Ax,
                    seg.Ay,
                    seg.Bx,
                    seg.By,
                    out _,
                    out _);
                if (d2 <= radiusSquaredDip)
                {
                    hits.Add(new InkSegmentHit(seg.Stroke, seg.Fragment, seg.StartPointIndex));
                }
            }

            return hits;
        }

        private IEnumerable<int> EnumerateCandidateSegments(double xDip, double yDip, double radiusDip)
        {
            if (_segments.Count == 0) yield break;

            int cellX = (int)Math.Floor(xDip / _cellSizeDip);
            int cellY = (int)Math.Floor(yDip / _cellSizeDip);
            int searchRadiusCells = Math.Max(1, (int)Math.Ceiling(radiusDip / _cellSizeDip));

            for (int dx = -searchRadiusCells; dx <= searchRadiusCells; dx++)
            {
                for (int dy = -searchRadiusCells; dy <= searchRadiusCells; dy++)
                {
                    long key = PackCell(cellX + dx, cellY + dy);
                    if (!_segmentGrid.TryGetValue(key, out var indices))
                    {
                        continue;
                    }

                    for (int i = 0; i < indices.Count; i++)
                    {
                        yield return indices[i];
                    }
                }
            }
        }

        private void AddSegment(InkStroke stroke, InkFragment fragment, int startPointIndex, double ax, double ay, double bx, double by)
        {
            double dx = bx - ax;
            double dy = by - ay;
            double lengthSquaredDip = (dx * dx) + (dy * dy);
            if (lengthSquaredDip <= MinSegmentLengthSquaredDip)
            {
                return;
            }

            int segmentIndex = _segments.Count;
            var seg = new Segment(stroke, fragment, startPointIndex, ax, ay, bx, by);
            _segments.Add(seg);
            EnsureStampCapacity(segmentIndex);

            int minCellX = (int)Math.Floor(seg.MinX / _cellSizeDip);
            int maxCellX = (int)Math.Floor(seg.MaxX / _cellSizeDip);
            int minCellY = (int)Math.Floor(seg.MinY / _cellSizeDip);
            int maxCellY = (int)Math.Floor(seg.MaxY / _cellSizeDip);

            for (int cx = minCellX; cx <= maxCellX; cx++)
            {
                for (int cy = minCellY; cy <= maxCellY; cy++)
                {
                    long key = PackCell(cx, cy);
                    if (!_segmentGrid.TryGetValue(key, out var list))
                    {
                        list = new List<int>(4);
                        _segmentGrid[key] = list;
                    }
                    list.Add(segmentIndex);
                }
            }
        }

        private int NextQueryStamp()
        {
            int stamp = _queryStampCounter++;
            if (_queryStampCounter == int.MaxValue)
            {
                _queryStampCounter = 1;
            }
            return stamp;
        }

        private void EnsureStampCapacity(int segmentIndex)
        {
            while (_segmentQueryStamp.Count <= segmentIndex)
            {
                _segmentQueryStamp.Add(0);
            }
        }

        private static long PackCell(int x, int y)
        {
            unchecked
            {
                return ((long)x << 32) | (uint)y;
            }
        }

        private static double DistancePointToSegmentSquared(
            double px,
            double py,
            double ax,
            double ay,
            double bx,
            double by,
            out double closestX,
            out double closestY)
        {
            double abx = bx - ax;
            double aby = by - ay;
            double abLen2 = (abx * abx) + (aby * aby);
            if (abLen2 <= MinDistanceDenominatorSquaredDip)
            {
                double apx0 = px - ax;
                double apy0 = py - ay;
                closestX = ax;
                closestY = ay;
                return (apx0 * apx0) + (apy0 * apy0);
            }

            double apx = px - ax;
            double apy = py - ay;
            double t = ((apx * abx) + (apy * aby)) / abLen2;
            t = Math.Clamp(t, 0.0, 1.0);

            closestX = ax + (abx * t);
            closestY = ay + (aby * t);

            double dx = px - closestX;
            double dy = py - closestY;
            return (dx * dx) + (dy * dy);
        }

        private readonly struct Segment
        {
            public Segment(InkStroke stroke, InkFragment fragment, int startPointIndex, double ax, double ay, double bx, double by)
            {
                Stroke = stroke;
                Fragment = fragment;
                StartPointIndex = startPointIndex;
                Ax = ax;
                Ay = ay;
                Bx = bx;
                By = by;
                MinX = Math.Min(ax, bx);
                MaxX = Math.Max(ax, bx);
                MinY = Math.Min(ay, by);
                MaxY = Math.Max(ay, by);
            }

            public InkStroke Stroke { get; }
            public InkFragment Fragment { get; }
            public int StartPointIndex { get; }
            public double Ax { get; }
            public double Ay { get; }
            public double Bx { get; }
            public double By { get; }
            public double MinX { get; }
            public double MinY { get; }
            public double MaxX { get; }
            public double MaxY { get; }
        }
    }

    internal readonly struct InkRectDip
    {
        public InkRectDip(double x, double y, double width, double height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public double X { get; }
        public double Y { get; }
        public double Width { get; }
        public double Height { get; }

        public double Left => X;
        public double Top => Y;
        public double Right => X + Width;
        public double Bottom => Y + Height;

        public bool Intersects(double minX, double minY, double maxX, double maxY)
        {
            if (Width <= 0 || Height <= 0) return false;
            if (maxX < Left) return false;
            if (minX > Right) return false;
            if (maxY < Top) return false;
            if (minY > Bottom) return false;
            return true;
        }
    }

    internal readonly struct InkSegmentHit
    {
        public InkSegmentHit(InkStroke stroke, InkFragment fragment, int segmentStartPointIndex)
        {
            Stroke = stroke;
            Fragment = fragment;
            SegmentStartPointIndex = segmentStartPointIndex;
        }

        public InkStroke Stroke { get; }
        public InkFragment Fragment { get; }
        public int SegmentStartPointIndex { get; }
    }

    internal readonly struct InkPointHitTestResult
    {
        public InkPointHitTestResult(
            InkStroke stroke,
            InkFragment fragment,
            int segmentStartPointIndex,
            double distanceDip,
            double closestXDip,
            double closestYDip)
        {
            Stroke = stroke;
            Fragment = fragment;
            SegmentStartPointIndex = segmentStartPointIndex;
            DistanceDip = distanceDip;
            ClosestXDip = closestXDip;
            ClosestYDip = closestYDip;
        }

        public InkStroke Stroke { get; }
        public InkFragment Fragment { get; }
        public int SegmentStartPointIndex { get; }
        public double DistanceDip { get; }
        public double ClosestXDip { get; }
        public double ClosestYDip { get; }
    }
}
