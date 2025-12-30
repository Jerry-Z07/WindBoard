using System;
using System.Windows;
using WindBoard.Services;

namespace WindBoard.Core.Ink
{
    public static class InkSmoothingDefaults
    {
        private const double DipPerMm = 96.0 / 25.4;

        // 固定的辅助参数（不提供用户调整）
        private const double VStopMmPerSec = 12;
        private const int StopHoldMs = 45;
        private const double FcSticky = 1.0;
        private const double CornerAngleDegPen = 100;
        private const double CornerAngleDegFinger = 105;
        private const int CornerHoldMsPen = 50;
        private const int CornerHoldMsFinger = 55;
        private const double FcCornerPen = 22;
        private const double FcCornerFinger = 20;
        private const double EpsilonCornerMmPen = 0.12;
        private const double EpsilonCornerMmFinger = 0.18;

        public static InkSmoothingParameters ForContact(Size? contactSizeCanvasDip, double zoom)
        {
            double sizeMm = 0;
            if (contactSizeCanvasDip.HasValue)
            {
                var s = contactSizeCanvasDip.Value;
                // contactSizeCanvasDip 是相对 InkCanvas 的尺寸（受 RenderTransform 逆变换影响）；
                // 乘 zoom 还原到屏幕空间，避免在不同缩放下"笔/手指"分类来回切换。
                sizeMm = Math.Max(s.Width, s.Height) * zoom / DipPerMm;
            }

            InkSmoothingParameters pen;
            InkSmoothingParameters finger;

            // 检查是否使用自定义参数
            var settings = SettingsService.Instance.Settings;
            if (settings.CustomSmoothingEnabled)
            {
                pen = new InkSmoothingParameters(
                    StepMm: settings.SmoothingPenStepMm,
                    EpsilonMm: settings.SmoothingPenEpsilonMm,
                    FcMin: settings.SmoothingPenFcMin,
                    Beta: settings.SmoothingPenBeta,
                    DCutoff: settings.SmoothingPenDCutoff,
                    VStopMmPerSec: VStopMmPerSec,
                    StopHoldMs: StopHoldMs,
                    FcSticky: FcSticky,
                    CornerAngleDeg: CornerAngleDegPen,
                    CornerHoldMs: CornerHoldMsPen,
                    FcCorner: FcCornerPen,
                    EpsilonCornerMm: EpsilonCornerMmPen);

                finger = new InkSmoothingParameters(
                    StepMm: settings.SmoothingFingerStepMm,
                    EpsilonMm: settings.SmoothingFingerEpsilonMm,
                    FcMin: settings.SmoothingFingerFcMin,
                    Beta: settings.SmoothingFingerBeta,
                    DCutoff: settings.SmoothingFingerDCutoff,
                    VStopMmPerSec: VStopMmPerSec,
                    StopHoldMs: StopHoldMs,
                    FcSticky: FcSticky,
                    CornerAngleDeg: CornerAngleDegFinger,
                    CornerHoldMs: CornerHoldMsFinger,
                    FcCorner: FcCornerFinger,
                    EpsilonCornerMm: EpsilonCornerMmFinger);
            }
            else
            {
                pen = new InkSmoothingParameters(
                    // 教学场景优化：降低步长与阈值以保留更多笔迹细节，提高拐角响应速度
                    StepMm: 0.9,
                    EpsilonMm: 0.15,
                    FcMin: 2.8,
                    Beta: 0.055,
                    DCutoff: 1.2,
                    VStopMmPerSec: VStopMmPerSec,
                    StopHoldMs: StopHoldMs,
                    FcSticky: FcSticky,
                    CornerAngleDeg: CornerAngleDegPen,
                    CornerHoldMs: CornerHoldMsPen,
                    FcCorner: FcCornerPen,
                    EpsilonCornerMm: EpsilonCornerMmPen);

                finger = new InkSmoothingParameters(
                    // 触摸书写优化：保持较强平滑（手指抖动大）但提高拐角响应
                    StepMm: 1.1,
                    EpsilonMm: 0.3,
                    FcMin: 1.8,
                    Beta: 0.035,
                    DCutoff: 1.2,
                    VStopMmPerSec: VStopMmPerSec,
                    StopHoldMs: StopHoldMs,
                    FcSticky: FcSticky,
                    CornerAngleDeg: CornerAngleDegFinger,
                    CornerHoldMs: CornerHoldMsFinger,
                    FcCorner: FcCornerFinger,
                    EpsilonCornerMm: EpsilonCornerMmFinger);
            }

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
