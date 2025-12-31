using System;
using System.Collections.Generic;
using System.Windows;

namespace WindBoard.Core.Ink
{
    internal readonly struct DetailPreservingSmootherParameters
    {
        public DetailPreservingSmootherParameters(
            double cornerAngleDeg,
            double projectionFactor,
            double capMinMm,
            double capWidthRatio,
            double capMaxMm,
            double marginMm,
            double clearanceRatio,
            double gridCellMm,
            double searchRadiusMm,
            double excludeTailLengthMm)
        {
            CornerAngleDeg = cornerAngleDeg;
            ProjectionFactor = projectionFactor;
            CapMinMm = capMinMm;
            CapWidthRatio = capWidthRatio;
            CapMaxMm = capMaxMm;
            MarginMm = marginMm;
            ClearanceRatio = clearanceRatio;
            GridCellMm = gridCellMm;
            SearchRadiusMm = searchRadiusMm;
            ExcludeTailLengthMm = excludeTailLengthMm;
        }

        public double CornerAngleDeg { get; }
        public double ProjectionFactor { get; }
        public double CapMinMm { get; }
        public double CapWidthRatio { get; }
        public double CapMaxMm { get; }
        public double MarginMm { get; }
        public double ClearanceRatio { get; }
        public double GridCellMm { get; }
        public double SearchRadiusMm { get; }
        public double ExcludeTailLengthMm { get; }

        public static DetailPreservingSmootherParameters NoPressureDefaults =>
            new DetailPreservingSmootherParameters(
                cornerAngleDeg: 40.0,
                projectionFactor: 0.8,
                capMinMm: 0.25,
                capWidthRatio: 0.40,
                capMaxMm: 0.90,
                marginMm: 0.15,
                clearanceRatio: 0.35,
                gridCellMm: 2.0,
                searchRadiusMm: 8.0,
                excludeTailLengthMm: 12.0);
    }

    internal readonly struct DetailPreservingSample
    {
        public DetailPreservingSample(Point canvasDip, float pressure)
        {
            CanvasDip = canvasDip;
            Pressure = pressure;
        }

        public Point CanvasDip { get; }
        public float Pressure { get; }
    }

    internal sealed class DetailPreservingSmoother
    {
        private const double DipPerMm = 96.0 / 25.4;

        private readonly DetailPreservingSmootherParameters _parameters;
        private readonly double _mmToDip;
        private readonly double _dipToMm;
        private readonly double _strokeWidthMm;
        private readonly double _gridCellDip;
        private readonly double _searchRadiusDip;
        private readonly int _searchRadiusCells;

        private readonly Dictionary<long, List<int>> _segmentGrid = new();
        private readonly List<Segment> _segments = new(256);
        private readonly List<double> _segmentCumulativeLengthMm = new(256);
        private readonly List<int> _segmentQueryStamp = new(256);
        private int _queryStampCounter = 1;

        private Point _lastOutputCanvasDip;
        private bool _hasLastOutput;

        private DetailPreservingSample _prevRaw;
        private DetailPreservingSample _midRaw;
        private bool _hasMidRaw;

        public DetailPreservingSmoother(
            DetailPreservingSmootherParameters parameters,
            Point initialCanvasDip,
            double zoomAtStart,
            double logicalThicknessDip)
        {
            _parameters = parameters;

            if (zoomAtStart <= 0) zoomAtStart = 1;
            _mmToDip = DipPerMm / zoomAtStart;
            _dipToMm = zoomAtStart / DipPerMm;

            _strokeWidthMm = logicalThicknessDip > 0 ? logicalThicknessDip / DipPerMm : 0;

            _gridCellDip = Math.Max(0.01, _parameters.GridCellMm * _mmToDip);
            _searchRadiusDip = Math.Max(_gridCellDip, _parameters.SearchRadiusMm * _mmToDip);
            _searchRadiusCells = Math.Max(1, (int)Math.Ceiling(_searchRadiusDip / _gridCellDip));

            _prevRaw = new DetailPreservingSample(initialCanvasDip, pressure: 0);
            _midRaw = default;
            _hasMidRaw = false;

            _lastOutputCanvasDip = initialCanvasDip;
            _hasLastOutput = true;
        }

        public void Push(DetailPreservingSample rawSample, bool isFinal, List<DetailPreservingSample> outputs)
        {
            if (outputs == null) throw new ArgumentNullException(nameof(outputs));

            if (!_hasMidRaw)
            {
                _midRaw = rawSample;
                _hasMidRaw = true;
                if (isFinal)
                {
                    EmitRaw(_midRaw, outputs);
                    _hasMidRaw = false;
                }
                return;
            }

            var nextRaw = rawSample;

            EmitSmoothedMid(_prevRaw, _midRaw, nextRaw, outputs);

            _prevRaw = _midRaw;
            _midRaw = nextRaw;
            _hasMidRaw = true;

            if (isFinal)
            {
                EmitRaw(_midRaw, outputs);
                _hasMidRaw = false;
            }
        }

        private void EmitSmoothedMid(DetailPreservingSample prev, DetailPreservingSample mid, DetailPreservingSample next, List<DetailPreservingSample> outputs)
        {
            Point p0 = prev.CanvasDip;
            Point p1 = mid.CanvasDip;
            Point p2 = next.CanvasDip;

            if (IsCorner(p0, p1, p2))
            {
                EmitRaw(mid, outputs);
                return;
            }

            var s = p2 - p0;
            double sLen2 = s.X * s.X + s.Y * s.Y;
            if (sLen2 <= 1e-8)
            {
                EmitRaw(mid, outputs);
                return;
            }

            var a = p1 - p0;
            double t = (a.X * s.X + a.Y * s.Y) / sLen2;
            t = Math.Clamp(t, 0.0, 1.0);
            var proj = new Point(p0.X + s.X * t, p0.Y + s.Y * t);

            var e = proj - p1;
            double eLen2 = e.X * e.X + e.Y * e.Y;
            if (eLen2 <= 1e-10)
            {
                EmitRaw(mid, outputs);
                return;
            }

            double capDip = ComputeCapDip(p1);
            if (capDip <= 1e-6)
            {
                EmitRaw(mid, outputs);
                return;
            }

            double eLen = Math.Sqrt(eLen2);
            double stepDip = Math.Min(_parameters.ProjectionFactor * eLen, capDip);
            if (stepDip <= 1e-6)
            {
                EmitRaw(mid, outputs);
                return;
            }

            double inv = stepDip / eLen;
            var outPoint = new Point(p1.X + e.X * inv, p1.Y + e.Y * inv);
            Emit(new DetailPreservingSample(outPoint, mid.Pressure), outputs);
        }

        private void EmitRaw(DetailPreservingSample raw, List<DetailPreservingSample> outputs)
        {
            Emit(raw, outputs);
        }

        private void Emit(DetailPreservingSample sample, List<DetailPreservingSample> outputs)
        {
            outputs.Add(sample);

            if (_hasLastOutput)
            {
                AddSegment(_lastOutputCanvasDip, sample.CanvasDip);
            }

            _lastOutputCanvasDip = sample.CanvasDip;
            _hasLastOutput = true;
        }

        private double ComputeCapDip(Point midCanvasDip)
        {
            double capMm = _parameters.CapMaxMm;
            if (_strokeWidthMm > 0)
            {
                capMm = Math.Min(capMm, Math.Max(_parameters.CapMinMm, _strokeWidthMm * _parameters.CapWidthRatio));
            }
            else
            {
                capMm = Math.Min(capMm, _parameters.CapMinMm);
            }

            double nearestMm = QueryNearestDistanceMm(midCanvasDip);
            if (!double.IsInfinity(nearestMm))
            {
                double safeMm = _strokeWidthMm + _parameters.MarginMm;
                double clearanceCapMm = Math.Max(0.0, nearestMm - safeMm) * _parameters.ClearanceRatio;
                capMm = Math.Min(capMm, clearanceCapMm);
            }

            if (capMm <= 0)
            {
                return 0;
            }

            return capMm * _mmToDip;
        }

        private bool IsCorner(Point p0, Point p1, Point p2)
        {
            var v1 = p1 - p0;
            var v2 = p2 - p1;

            double v1Len2 = v1.X * v1.X + v1.Y * v1.Y;
            double v2Len2 = v2.X * v2.X + v2.Y * v2.Y;
            if (v1Len2 <= 1e-8 || v2Len2 <= 1e-8)
            {
                return true;
            }

            double denom = Math.Sqrt(v1Len2 * v2Len2);
            if (denom <= 1e-12)
            {
                return true;
            }

            double cos = (v1.X * v2.X + v1.Y * v2.Y) / denom;
            cos = Math.Clamp(cos, -1.0, 1.0);
            double angleDeg = Math.Acos(cos) * (180.0 / Math.PI);
            return angleDeg >= _parameters.CornerAngleDeg;
        }

        private double QueryNearestDistanceMm(Point pCanvasDip)
        {
            if (_segments.Count == 0)
            {
                return double.PositiveInfinity;
            }

            int cx = (int)Math.Floor(pCanvasDip.X / _gridCellDip);
            int cy = (int)Math.Floor(pCanvasDip.Y / _gridCellDip);

            int tailStart = 0;
            if (_segmentCumulativeLengthMm.Count > 0 && _parameters.ExcludeTailLengthMm > 0)
            {
                double totalMm = _segmentCumulativeLengthMm[^1];
                double cutoffMm = totalMm - _parameters.ExcludeTailLengthMm;
                if (cutoffMm > 0)
                {
                    tailStart = UpperBound(_segmentCumulativeLengthMm, cutoffMm);
                }
            }

            int stamp = _queryStampCounter++;
            if (_queryStampCounter == int.MaxValue)
            {
                _queryStampCounter = 1;
            }

            double bestD2 = double.PositiveInfinity;

            for (int dx = -_searchRadiusCells; dx <= _searchRadiusCells; dx++)
            {
                for (int dy = -_searchRadiusCells; dy <= _searchRadiusCells; dy++)
                {
                    long key = PackCell(cx + dx, cy + dy);
                    if (!_segmentGrid.TryGetValue(key, out var indices))
                    {
                        continue;
                    }

                    for (int i = 0; i < indices.Count; i++)
                    {
                        int segIndex = indices[i];
                        if (segIndex < tailStart)
                        {
                            // ok
                        }
                        else
                        {
                            continue;
                        }

                        if (segIndex < 0 || segIndex >= _segments.Count)
                        {
                            continue;
                        }

                        EnsureStampCapacity(segIndex);
                        if (_segmentQueryStamp[segIndex] == stamp)
                        {
                            continue;
                        }

                        _segmentQueryStamp[segIndex] = stamp;

                        var seg = _segments[segIndex];
                        double d2 = DistancePointToSegmentSquared(pCanvasDip, seg.A, seg.B);
                        if (d2 < bestD2)
                        {
                            bestD2 = d2;
                        }
                    }
                }
            }

            if (double.IsInfinity(bestD2))
            {
                return double.PositiveInfinity;
            }

            return Math.Sqrt(bestD2) * _dipToMm;
        }

        private void AddSegment(Point a, Point b)
        {
            var d = b - a;
            double len2 = d.X * d.X + d.Y * d.Y;
            if (len2 <= 1e-10)
            {
                return;
            }

            int index = _segments.Count;
            _segments.Add(new Segment(a, b));
            EnsureStampCapacity(index);

            double segMm = Math.Sqrt(len2) * _dipToMm;
            double totalMm = _segmentCumulativeLengthMm.Count > 0 ? _segmentCumulativeLengthMm[^1] : 0.0;
            _segmentCumulativeLengthMm.Add(totalMm + segMm);

            double minX = Math.Min(a.X, b.X);
            double maxX = Math.Max(a.X, b.X);
            double minY = Math.Min(a.Y, b.Y);
            double maxY = Math.Max(a.Y, b.Y);

            int minCx = (int)Math.Floor(minX / _gridCellDip);
            int maxCx = (int)Math.Floor(maxX / _gridCellDip);
            int minCy = (int)Math.Floor(minY / _gridCellDip);
            int maxCy = (int)Math.Floor(maxY / _gridCellDip);

            for (int cx = minCx; cx <= maxCx; cx++)
            {
                for (int cy = minCy; cy <= maxCy; cy++)
                {
                    long key = PackCell(cx, cy);
                    if (!_segmentGrid.TryGetValue(key, out var list))
                    {
                        list = new List<int>(4);
                        _segmentGrid[key] = list;
                    }
                    list.Add(index);
                }
            }
        }

        private void EnsureStampCapacity(int segIndex)
        {
            while (_segmentQueryStamp.Count <= segIndex)
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

        private static double DistancePointToSegmentSquared(Point p, Point a, Point b)
        {
            var ab = b - a;
            double abLen2 = ab.X * ab.X + ab.Y * ab.Y;
            if (abLen2 <= 1e-12)
            {
                var ap0 = p - a;
                return ap0.X * ap0.X + ap0.Y * ap0.Y;
            }

            var ap = p - a;
            double t = (ap.X * ab.X + ap.Y * ab.Y) / abLen2;
            t = Math.Clamp(t, 0.0, 1.0);
            var q = new Point(a.X + ab.X * t, a.Y + ab.Y * t);
            var pq = p - q;
            return pq.X * pq.X + pq.Y * pq.Y;
        }

        private static int UpperBound(List<double> sortedAscending, double value)
        {
            int lo = 0;
            int hi = sortedAscending.Count;
            while (lo < hi)
            {
                int mid = lo + ((hi - lo) / 2);
                if (sortedAscending[mid] <= value)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid;
                }
            }

            return lo;
        }

        private readonly struct Segment
        {
            public Segment(Point a, Point b)
            {
                A = a;
                B = b;
            }

            public Point A { get; }
            public Point B { get; }
        }
    }
}
