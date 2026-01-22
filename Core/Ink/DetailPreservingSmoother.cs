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
        private const double DegreesPerRadian = 180.0 / Math.PI;

        private const double MinVectorLengthSquaredDip = 1e-8;
        private const double MinChordLengthSquaredDip = 1e-8;
        private const double MinErrorLengthSquaredDip = 1e-10;
        private const double MinMeaningfulDip = 1e-6;
        private const double MinDenominator = 1e-12;
        private const double MinGridCellDip = 0.01;

        private readonly DetailPreservingSmootherParameters _parameters;
        private readonly double _mmToDip;
        private readonly double _strokeWidthMm;
        private readonly SegmentSpatialIndex _segmentIndex;

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
            double dipToMm = zoomAtStart / DipPerMm;

            _strokeWidthMm = logicalThicknessDip > 0 ? logicalThicknessDip / DipPerMm : 0;

            double gridCellDip = Math.Max(MinGridCellDip, _parameters.GridCellMm * _mmToDip);
            double searchRadiusDip = Math.Max(gridCellDip, _parameters.SearchRadiusMm * _mmToDip);
            _segmentIndex = new SegmentSpatialIndex(gridCellDip, searchRadiusDip, dipToMm, _parameters.ExcludeTailLengthMm);

            _prevRaw = new DetailPreservingSample(initialCanvasDip, pressure: 0);
            _midRaw = default;
            _hasMidRaw = false;

            _lastOutputCanvasDip = initialCanvasDip;
            _hasLastOutput = true;
        }

        public void Push(DetailPreservingSample rawSample, bool isFinal, List<DetailPreservingSample> outputs)
        {
            // 1-point delay: we need (prev, mid, next) to smooth `mid`, so the newest sample is buffered
            // until the next sample arrives; `isFinal` flushes the buffered last sample as-is.
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
            if (sLen2 <= MinChordLengthSquaredDip)
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
            if (eLen2 <= MinErrorLengthSquaredDip)
            {
                EmitRaw(mid, outputs);
                return;
            }

            double capDip = ComputeCapDip(p1);
            if (capDip <= MinMeaningfulDip)
            {
                EmitRaw(mid, outputs);
                return;
            }

            double eLen = Math.Sqrt(eLen2);
            double stepDip = Math.Min(_parameters.ProjectionFactor * eLen, capDip);
            if (stepDip <= MinMeaningfulDip)
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
                _segmentIndex.AddSegment(_lastOutputCanvasDip, sample.CanvasDip);
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

            double nearestMm = _segmentIndex.QueryNearestDistanceMm(midCanvasDip);
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
            if (v1Len2 <= MinVectorLengthSquaredDip || v2Len2 <= MinVectorLengthSquaredDip)
            {
                return true;
            }

            double denom = Math.Sqrt(v1Len2 * v2Len2);
            if (denom <= MinDenominator)
            {
                return true;
            }

            double cos = (v1.X * v2.X + v1.Y * v2.Y) / denom;
            cos = Math.Clamp(cos, -1.0, 1.0);
            double angleDeg = Math.Acos(cos) * DegreesPerRadian;
            return angleDeg >= _parameters.CornerAngleDeg;
        }
    }
}
