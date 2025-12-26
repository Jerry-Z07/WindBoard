using System;
using System.Collections.Generic;
using System.Windows;

namespace WindBoard.Core.Ink
{
    public sealed class RealtimeInkSmoother
    {
        private const double DipPerMm = 96.0 / 25.4;

        private readonly InkSmoothingParameters _p;
        private readonly OneEuroFilter2D _filter = new();
        private readonly List<Point> _scratchMm = new();
        private readonly List<Point> _resampledMm = new();
        private readonly double _cornerCosThreshold;

        private bool _initialized;
        private long _lastTicks;
        private Point _lastResampleMm;

        private bool _hasLastOutput;
        private Point _lastOutputMm;

        private Point? _aMm;
        private Point? _bMm;
        private long _cornerHoldUntilTicks;

        private double _lowSpeedSeconds;
        private bool _stickyActive;

        public RealtimeInkSmoother(InkSmoothingParameters parameters)
        {
            _p = parameters;
            _cornerCosThreshold = Math.Cos(_p.CornerAngleDeg * Math.PI / 180.0);
        }

        public void Reset()
        {
            _initialized = false;
            _hasLastOutput = false;
            _aMm = null;
            _bMm = null;
            _cornerHoldUntilTicks = 0;
            _lowSpeedSeconds = 0;
            _stickyActive = false;
        }

        public IReadOnlyList<Point> Process(Point rawCanvasDip, long timestampTicks, double zoom, bool isFinal)
        {
            _scratchMm.Clear();
            _resampledMm.Clear();
            Point rawMm = CanvasToScreenMm(rawCanvasDip, zoom);

            if (!_initialized)
            {
                _initialized = true;
                _lastTicks = timestampTicks;
                _lastResampleMm = rawMm;
                _filter.Reset(rawMm);
                _lastOutputMm = rawMm;
                _hasLastOutput = true;
                _scratchMm.Add(rawMm);
                return _scratchMm;
            }

            double dtEstimate = (timestampTicks - _lastTicks) / (double)TimeSpan.TicksPerSecond;
            dtEstimate = Math.Clamp(dtEstimate, 0.001, 0.05);
            double speedMmPerSec = (rawMm - _lastResampleMm).Length / dtEstimate;
            double stepScale = speedMmPerSec <= 0 ? 1.0 : Math.Clamp(speedMmPerSec / 220.0, 1.0, 2.2);
            double stepMm = _p.StepMm * stepScale;

            ResampleMm(rawMm, isFinal, stepMm, _resampledMm);
            if (_resampledMm.Count == 0)
            {
                _lastTicks = timestampTicks;
                return _scratchMm;
            }

            double dtTotal = (timestampTicks - _lastTicks) / (double)TimeSpan.TicksPerSecond;
            dtTotal = Math.Clamp(dtTotal, 0.001, 0.05);
            double dtPer = dtTotal / _resampledMm.Count;
            dtPer = Math.Clamp(dtPer, 0.001, 0.05);

            for (int i = 0; i < _resampledMm.Count; i++)
            {
                var candidateMm = _resampledMm[i];
                UpdateCornerHold(candidateMm, timestampTicks);

                bool cornerActive = timestampTicks <= _cornerHoldUntilTicks;
                double cutoffMin = cornerActive ? _p.FcCorner : 0;

                // cornerActive 与 stickyActive 可能在低速“拐角停顿”时同时为 true：
                // FcCorner(>=) 会大于 FcSticky(<=)，导致 Clamp(min,max) 反转并抛异常。
                // 此时优先保证拐角保持的响应（不启用 sticky 上限）。
                double cutoffMax = (cornerActive || !_stickyActive) ? double.PositiveInfinity : _p.FcSticky;

                Point outMm = _filter.Update(
                    candidateMm,
                    dtPer,
                    minCutoffHz: _p.FcMin,
                    beta: _p.Beta,
                    dCutoffHz: _p.DCutoff,
                    cutoffMinClampHz: cutoffMin,
                    cutoffMaxClampHz: cutoffMax);

                UpdateSticky(_filter.DerivativeMagnitudeMmPerSec, dtPer);
                double epsilon = cornerActive ? _p.EpsilonCornerMm : _p.EpsilonMm;
                if (!_hasLastOutput || (outMm - _lastOutputMm).Length >= epsilon)
                {
                    _scratchMm.Add(outMm);
                    _lastOutputMm = outMm;
                    _hasLastOutput = true;
                }
            }

            _lastTicks = timestampTicks;
            return _scratchMm;
        }

        public Point ScreenMmToCanvasDip(Point mm, double zoom)
        {
            zoom = zoom <= 0 ? 1 : zoom;
            return new Point(mm.X * DipPerMm / zoom, mm.Y * DipPerMm / zoom);
        }

        private void ResampleMm(Point rawMm, bool isFinal, double stepMm, List<Point> output)
        {
            stepMm = Math.Max(0.05, stepMm);
            Vector d = rawMm - _lastResampleMm;
            double dist = d.Length;
            if (dist <= 0)
            {
                if (isFinal)
                {
                    output.Add(rawMm);
                }
                return;
            }

            while (dist >= stepMm)
            {
                double t = stepMm / dist;
                _lastResampleMm = new Point(
                    _lastResampleMm.X + d.X * t,
                    _lastResampleMm.Y + d.Y * t);

                output.Add(_lastResampleMm);
                d = rawMm - _lastResampleMm;
                dist = d.Length;
            }

            if (isFinal && (rawMm - _lastResampleMm).Length > 0)
            {
                _lastResampleMm = rawMm;
                output.Add(rawMm);
            }
        }

        private void UpdateSticky(double speedMmPerSec, double dtSec)
        {
            if (speedMmPerSec < _p.VStopMmPerSec)
            {
                _lowSpeedSeconds += dtSec;
                if (_lowSpeedSeconds * 1000.0 >= _p.StopHoldMs)
                {
                    _stickyActive = true;
                }
            }
            else
            {
                _lowSpeedSeconds = 0;
                if (speedMmPerSec > _p.VStopMmPerSec * 1.5)
                {
                    _stickyActive = false;
                }
            }
        }

        private void UpdateCornerHold(Point newMm, long nowTicks)
        {
            if (_aMm == null)
            {
                _aMm = newMm;
                return;
            }

            if (_bMm == null)
            {
                _bMm = newMm;
                return;
            }

            var a = _aMm.Value;
            var b = _bMm.Value;
            var c = newMm;

            Vector v1 = a - b;
            Vector v2 = c - b;
            double len1Sq = v1.LengthSquared;
            double len2Sq = v2.LengthSquared;
            if (len1Sq > 0.0001 && len2Sq > 0.0001)
            {
                double dot = Vector.Multiply(v1, v2);
                double cos = dot / Math.Sqrt(len1Sq * len2Sq);
                cos = Math.Clamp(cos, -1.0, 1.0);
                if (cos >= _cornerCosThreshold)
                {
                    _cornerHoldUntilTicks = nowTicks + _p.CornerHoldMs * TimeSpan.TicksPerMillisecond;
                }
            }

            _aMm = _bMm;
            _bMm = newMm;
        }

        private static Point CanvasToScreenMm(Point canvasDip, double zoom)
        {
            zoom = zoom <= 0 ? 1 : zoom;
            return new Point(canvasDip.X * zoom / DipPerMm, canvasDip.Y * zoom / DipPerMm);
        }
    }
}
