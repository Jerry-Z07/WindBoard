using System;
using System.Windows;

namespace WindBoard.Core.Ink
{
    public static class SimulatedPressureDefaults
    {
        private const double DipPerMm = 96.0 / 25.4;

        public static SimulatedPressureParameters ForContact(Size? contactSizeCanvasDip, double zoom)
        {
            double sizeMm = 0;
            if (contactSizeCanvasDip.HasValue)
            {
                var s = contactSizeCanvasDip.Value;
                sizeMm = Math.Max(s.Width, s.Height) * zoom / DipPerMm;
            }

            var pen = new SimulatedPressureParameters(
                // 签字笔：变化幅度克制（主要体现在起收笔与低速略粗，高速略细）。
                PressureMin: 0.78f,
                PressureMax: 0.95f,
                PressureNominal: 0.86f,
                PressureEnd: 0.60f,
                VSlowMmPerSec: 35,
                VFastMmPerSec: 260,
                VStopMmPerSec: 18,
                StopHoldMs: 60,
                AttackMs: 25,
                ReleaseMs: 15);

            var finger = new SimulatedPressureParameters(
                PressureMin: 0.82f,
                PressureMax: 0.94f,
                PressureNominal: 0.88f,
                PressureEnd: 0.65f,
                VSlowMmPerSec: 45,
                VFastMmPerSec: 320,
                VStopMmPerSec: 18,
                StopHoldMs: 60,
                AttackMs: 30,
                ReleaseMs: 18);

            const double penMm = 5.0;
            const double fingerMm = 8.0;

            double t;
            if (sizeMm <= 0)
            {
                t = 0;
            }
            else if (sizeMm <= penMm)
            {
                t = 0;
            }
            else if (sizeMm >= fingerMm)
            {
                t = 1;
            }
            else
            {
                t = (sizeMm - penMm) / (fingerMm - penMm);
            }

            return Lerp(pen, finger, t);
        }

        private static SimulatedPressureParameters Lerp(SimulatedPressureParameters a, SimulatedPressureParameters b, double t)
        {
            t = Math.Clamp(t, 0, 1);
            float Lf(float x, float y) => x + (y - x) * (float)t;
            double L(double x, double y) => x + (y - x) * t;
            int Li(int x, int y) => (int)Math.Round(x + (y - x) * t);

            return new SimulatedPressureParameters(
                PressureMin: Lf(a.PressureMin, b.PressureMin),
                PressureMax: Lf(a.PressureMax, b.PressureMax),
                PressureNominal: Lf(a.PressureNominal, b.PressureNominal),
                PressureEnd: Lf(a.PressureEnd, b.PressureEnd),
                VSlowMmPerSec: L(a.VSlowMmPerSec, b.VSlowMmPerSec),
                VFastMmPerSec: L(a.VFastMmPerSec, b.VFastMmPerSec),
                VStopMmPerSec: L(a.VStopMmPerSec, b.VStopMmPerSec),
                StopHoldMs: Li(a.StopHoldMs, b.StopHoldMs),
                AttackMs: Li(a.AttackMs, b.AttackMs),
                ReleaseMs: Li(a.ReleaseMs, b.ReleaseMs));
        }
    }
}

