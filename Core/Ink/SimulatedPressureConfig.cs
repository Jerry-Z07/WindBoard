using System;

namespace WindBoard.Core.Ink
{
    public sealed class SimulatedPressureConfig
    {
        public bool Enabled { get; set; } = true;

        // 起笔/收笔渐变长度（屏幕毫米）
        public double StartTaperMm { get; set; } = 5.5;
        public double EndTaperMm { get; set; } = 8.0;

        // 速度调制（屏幕毫米/秒）：越快越细（压力系数越小）
        public double SpeedMinMmPerSec { get; set; } = 30.0;
        public double SpeedMaxMmPerSec { get; set; } = 750.0;
        public double FastSpeedMinFactor { get; set; } = 0.62;

        // 压力下限（0..1）：避免起笔“断墨”
        public float PressureFloor { get; set; } = 0.26f;

        // 收笔阶段允许更细的下限（0..1）
        public float EndPressureFloor { get; set; } = 0.06f;

        // 压力平滑时间常数（毫秒）：越大越稳但越“糊”
        public double SmoothingTauMs { get; set; } = 18.0;

        // 收笔回写最多处理的点数（性能上限）
        public int MaxEndTaperPoints { get; set; } = 160;

        public static SimulatedPressureConfig CreateDefault()
        {
            var cfg = new SimulatedPressureConfig();
            cfg.ClampInPlace();
            return cfg;
        }

        public SimulatedPressureConfig Clone()
        {
            return new SimulatedPressureConfig
            {
                Enabled = Enabled,
                StartTaperMm = StartTaperMm,
                EndTaperMm = EndTaperMm,
                SpeedMinMmPerSec = SpeedMinMmPerSec,
                SpeedMaxMmPerSec = SpeedMaxMmPerSec,
                FastSpeedMinFactor = FastSpeedMinFactor,
                PressureFloor = PressureFloor,
                EndPressureFloor = EndPressureFloor,
                SmoothingTauMs = SmoothingTauMs,
                MaxEndTaperPoints = MaxEndTaperPoints
            };
        }

        public void ClampInPlace()
        {
            StartTaperMm = ClampFinite(StartTaperMm, 0, 40);
            EndTaperMm = ClampFinite(EndTaperMm, 0, 60);
            SpeedMinMmPerSec = ClampFinite(SpeedMinMmPerSec, 0, 5000);
            SpeedMaxMmPerSec = ClampFinite(SpeedMaxMmPerSec, 1, 8000);
            if (SpeedMaxMmPerSec < SpeedMinMmPerSec) (SpeedMinMmPerSec, SpeedMaxMmPerSec) = (SpeedMaxMmPerSec, SpeedMinMmPerSec);
            FastSpeedMinFactor = ClampFinite(FastSpeedMinFactor, 0.05, 1.0);
            PressureFloor = (float)ClampFinite(PressureFloor, 0.02, 1.0);
            EndPressureFloor = (float)ClampFinite(EndPressureFloor, 0.0, 1.0);
            if (EndPressureFloor > PressureFloor) EndPressureFloor = PressureFloor;
            SmoothingTauMs = ClampFinite(SmoothingTauMs, 0, 200);
            MaxEndTaperPoints = Math.Clamp(MaxEndTaperPoints, 0, 2000);
        }

        private static double ClampFinite(double v, double min, double max)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return min;
            return Math.Clamp(v, min, max);
        }
    }
}
