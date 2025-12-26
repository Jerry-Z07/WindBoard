using System;
using System.Windows;

namespace WindBoard.Core.Ink
{
    public static class InkSmoothingDefaults
    {
        private const double DipPerMm = 96.0 / 25.4;

        public static InkSmoothingParameters ForContact(Size? contactSizeCanvasDip, double zoom)
        {
            double sizeMm = 0;
            if (contactSizeCanvasDip.HasValue)
            {
                var s = contactSizeCanvasDip.Value;
                sizeMm = Math.Max(s.Width, s.Height) * zoom / DipPerMm;
            }

            var pen = new InkSmoothingParameters(
                // 性能/内存：步长与输出阈值略增，减少 StylusPoints 数量与实时处理频率。
                StepMm: 1.1,
                EpsilonMm: 0.22,
                FcMin: 2.2,
                Beta: 0.045,
                DCutoff: 1.2,
                VStopMmPerSec: 18,
                StopHoldMs: 60,
                FcSticky: 0.8,
                CornerAngleDeg: 120,
                CornerHoldMs: 80,
                FcCorner: 18,
                EpsilonCornerMm: 0.18);

            var finger = new InkSmoothingParameters(
                StepMm: 1.3,
                EpsilonMm: 0.4,
                FcMin: 1.6,
                Beta: 0.028,
                DCutoff: 1.2,
                VStopMmPerSec: 18,
                StopHoldMs: 60,
                FcSticky: 0.8,
                CornerAngleDeg: 120,
                CornerHoldMs: 80,
                FcCorner: 18,
                EpsilonCornerMm: 0.22);

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

        private static InkSmoothingParameters Lerp(InkSmoothingParameters a, InkSmoothingParameters b, double t)
        {
            t = Math.Clamp(t, 0, 1);
            double L(double x, double y) => x + (y - x) * t;
            int Li(int x, int y) => (int)Math.Round(x + (y - x) * t);

            return new InkSmoothingParameters(
                StepMm: L(a.StepMm, b.StepMm),
                EpsilonMm: L(a.EpsilonMm, b.EpsilonMm),
                FcMin: L(a.FcMin, b.FcMin),
                Beta: L(a.Beta, b.Beta),
                DCutoff: L(a.DCutoff, b.DCutoff),
                VStopMmPerSec: L(a.VStopMmPerSec, b.VStopMmPerSec),
                StopHoldMs: Li(a.StopHoldMs, b.StopHoldMs),
                FcSticky: L(a.FcSticky, b.FcSticky),
                CornerAngleDeg: L(a.CornerAngleDeg, b.CornerAngleDeg),
                CornerHoldMs: Li(a.CornerHoldMs, b.CornerHoldMs),
                FcCorner: L(a.FcCorner, b.FcCorner),
                EpsilonCornerMm: L(a.EpsilonCornerMm, b.EpsilonCornerMm));
        }
    }
}
