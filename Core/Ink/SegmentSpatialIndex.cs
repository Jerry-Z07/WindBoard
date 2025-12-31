using System;
using System.Collections.Generic;
using System.Windows;

namespace WindBoard.Core.Ink
{
    internal sealed class SegmentSpatialIndex
    {
        private const double MinSegmentLengthSquaredDip = 1e-10;
        private const double MinDistanceDenominatorSquaredDip = 1e-12;

        private readonly double _gridCellDip;
        private readonly int _searchRadiusCells;
        private readonly double _dipToMm;
        private readonly double _excludeTailLengthMm;

        private readonly Dictionary<long, List<int>> _segmentGrid = new();
        private readonly List<Segment> _segments = new(256);
        private readonly List<double> _segmentCumulativeLengthMm = new(256);
        private readonly List<int> _segmentQueryStamp = new(256);
        private int _queryStampCounter = 1;

        public SegmentSpatialIndex(double gridCellDip, double searchRadiusDip, double dipToMm, double excludeTailLengthMm)
        {
            if (gridCellDip <= 0) throw new ArgumentOutOfRangeException(nameof(gridCellDip));
            if (searchRadiusDip <= 0) throw new ArgumentOutOfRangeException(nameof(searchRadiusDip));
            if (dipToMm <= 0) throw new ArgumentOutOfRangeException(nameof(dipToMm));

            _gridCellDip = gridCellDip;
            _searchRadiusCells = Math.Max(1, (int)Math.Ceiling(searchRadiusDip / gridCellDip));
            _dipToMm = dipToMm;
            _excludeTailLengthMm = excludeTailLengthMm;
        }

        public void AddSegment(Point a, Point b)
        {
            Vector segmentDelta = b - a;
            double lengthSquaredDip = segmentDelta.X * segmentDelta.X + segmentDelta.Y * segmentDelta.Y;
            if (lengthSquaredDip <= MinSegmentLengthSquaredDip)
            {
                return;
            }

            int segmentIndex = _segments.Count;
            _segments.Add(new Segment(a, b));
            EnsureStampCapacity(segmentIndex);

            double segmentLengthMm = Math.Sqrt(lengthSquaredDip) * _dipToMm;
            double totalMm = _segmentCumulativeLengthMm.Count > 0 ? _segmentCumulativeLengthMm[^1] : 0.0;
            _segmentCumulativeLengthMm.Add(totalMm + segmentLengthMm);

            double minX = Math.Min(a.X, b.X);
            double maxX = Math.Max(a.X, b.X);
            double minY = Math.Min(a.Y, b.Y);
            double maxY = Math.Max(a.Y, b.Y);

            int minCellX = (int)Math.Floor(minX / _gridCellDip);
            int maxCellX = (int)Math.Floor(maxX / _gridCellDip);
            int minCellY = (int)Math.Floor(minY / _gridCellDip);
            int maxCellY = (int)Math.Floor(maxY / _gridCellDip);

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

        public double QueryNearestDistanceMm(Point pCanvasDip)
        {
            if (_segments.Count == 0)
            {
                return double.PositiveInfinity;
            }

            var eligibility = CreateEligibility();
            if (eligibility.EligibleCount == 0)
            {
                return double.PositiveInfinity;
            }

            int cellX = (int)Math.Floor(pCanvasDip.X / _gridCellDip);
            int cellY = (int)Math.Floor(pCanvasDip.Y / _gridCellDip);

            int stamp = NextQueryStamp();
            double bestDistanceSquaredDip = double.PositiveInfinity;

            for (int dx = -_searchRadiusCells; dx <= _searchRadiusCells; dx++)
            {
                for (int dy = -_searchRadiusCells; dy <= _searchRadiusCells; dy++)
                {
                    long key = PackCell(cellX + dx, cellY + dy);
                    if (!_segmentGrid.TryGetValue(key, out var indices))
                    {
                        continue;
                    }

                    for (int i = 0; i < indices.Count; i++)
                    {
                        int segmentIndex = indices[i];
                        if (!eligibility.IsEligible(segmentIndex))
                        {
                            continue;
                        }

                        EnsureStampCapacity(segmentIndex);
                        if (_segmentQueryStamp[segmentIndex] == stamp)
                        {
                            continue;
                        }
                        _segmentQueryStamp[segmentIndex] = stamp;

                        var segment = _segments[segmentIndex];
                        double d2 = DistancePointToSegmentSquared(pCanvasDip, segment.A, segment.B);
                        if (d2 < bestDistanceSquaredDip)
                        {
                            bestDistanceSquaredDip = d2;
                        }
                    }
                }
            }

            if (double.IsInfinity(bestDistanceSquaredDip))
            {
                return double.PositiveInfinity;
            }

            return Math.Sqrt(bestDistanceSquaredDip) * _dipToMm;
        }

        private SegmentEligibility CreateEligibility()
        {
            int eligibleCount = GetEligibleSegmentCount();
            return new SegmentEligibility(eligibleCount);
        }

        private int GetEligibleSegmentCount()
        {
            if (_excludeTailLengthMm <= 0)
            {
                return _segments.Count;
            }

            if (_segmentCumulativeLengthMm.Count == 0)
            {
                return 0;
            }

            double cutoffMm = _segmentCumulativeLengthMm[^1] - _excludeTailLengthMm;
            if (cutoffMm <= 0)
            {
                return 0;
            }

            return UpperBound(_segmentCumulativeLengthMm, cutoffMm);
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

        private static double DistancePointToSegmentSquared(Point p, Point a, Point b)
        {
            Vector ab = b - a;
            double abLen2 = ab.X * ab.X + ab.Y * ab.Y;
            if (abLen2 <= MinDistanceDenominatorSquaredDip)
            {
                Vector ap0 = p - a;
                return ap0.X * ap0.X + ap0.Y * ap0.Y;
            }

            Vector ap = p - a;
            double t = (ap.X * ab.X + ap.Y * ab.Y) / abLen2;
            t = Math.Clamp(t, 0.0, 1.0);

            var q = new Point(a.X + ab.X * t, a.Y + ab.Y * t);
            Vector pq = p - q;
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

        private readonly struct SegmentEligibility
        {
            public SegmentEligibility(int eligibleCount)
            {
                EligibleCount = eligibleCount;
            }

            public int EligibleCount { get; }

            public bool IsEligible(int segmentIndex)
            {
                return segmentIndex >= 0 && segmentIndex < EligibleCount;
            }
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

