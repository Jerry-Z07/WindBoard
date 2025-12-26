using System;
using System.Windows;

namespace WindBoard.Core.Ink
{
    public sealed class OneEuroFilter2D
    {
        private Point _prevRaw;
        private Point _prevFiltered;
        private Vector _prevDerivFiltered;
        private bool _hasPrev;

        public double DerivativeMagnitudeMmPerSec { get; private set; }

        public void Reset(Point rawMm)
        {
            _prevRaw = rawMm;
            _prevFiltered = rawMm;
            _prevDerivFiltered = new Vector(0, 0);
            DerivativeMagnitudeMmPerSec = 0;
            _hasPrev = true;
        }

        public Point Update(
            Point rawMm,
            double dtSec,
            double minCutoffHz,
            double beta,
            double dCutoffHz,
            double cutoffMinClampHz = 0,
            double cutoffMaxClampHz = double.PositiveInfinity)
        {
            if (!_hasPrev)
            {
                Reset(rawMm);
                return rawMm;
            }

            dtSec = Math.Clamp(dtSec, 0.001, 0.05);

            Vector deriv = (rawMm - _prevRaw) / dtSec;
            double aDeriv = Alpha(dCutoffHz, dtSec);
            _prevDerivFiltered = LowPass(deriv, _prevDerivFiltered, aDeriv);
            DerivativeMagnitudeMmPerSec = _prevDerivFiltered.Length;

            double cutoff = minCutoffHz + beta * DerivativeMagnitudeMmPerSec;
            cutoff = Math.Clamp(cutoff, Math.Max(0.0001, cutoffMinClampHz), cutoffMaxClampHz);
            double a = Alpha(cutoff, dtSec);
            _prevFiltered = LowPass(rawMm, _prevFiltered, a);

            _prevRaw = rawMm;
            return _prevFiltered;
        }

        private static double Alpha(double cutoffHz, double dtSec)
        {
            cutoffHz = Math.Max(0.0001, cutoffHz);
            double tau = 1.0 / (2.0 * Math.PI * cutoffHz);
            return dtSec / (dtSec + tau);
        }

        private static Point LowPass(Point value, Point prev, double alpha)
        {
            alpha = Math.Clamp(alpha, 0, 1);
            return new Point(
                prev.X + (value.X - prev.X) * alpha,
                prev.Y + (value.Y - prev.Y) * alpha);
        }

        private static Vector LowPass(Vector value, Vector prev, double alpha)
        {
            alpha = Math.Clamp(alpha, 0, 1);
            return new Vector(
                prev.X + (value.X - prev.X) * alpha,
                prev.Y + (value.Y - prev.Y) * alpha);
        }
    }
}
