using System;
using System.Windows;
using WindBoard.Core.Ink;
using StylusPoint = System.Windows.Input.StylusPoint;

namespace WindBoard.Core.Modes
{
    public partial class InkMode
    {
        private static float NextPressure(SimulatedPressureConfig cfg, ref SimulatedPressureState state, Point screenMm, double dtSec)
        {
            if (!state.Enabled)
            {
                return 0.5f;
            }

            dtSec = Math.Clamp(dtSec, 0.001, 0.05);

            double distMm = 0;
            if (state.HasLastMm)
            {
                distMm = (screenMm - state.LastMm).Length;
            }

            state.LengthFromStartMm += distMm;
            double startTaper = cfg.StartTaperMm <= 0 ? 1.0 : SmoothStep(0, cfg.StartTaperMm, state.LengthFromStartMm);

            double speed = distMm <= 0 ? 0 : distMm / dtSec;
            double tSpeed = SmoothStep(cfg.SpeedMinMmPerSec, cfg.SpeedMaxMmPerSec, speed);
            double speedFactor = Lerp(1.0, cfg.FastSpeedMinFactor, tSpeed);

            double floor = Math.Clamp(cfg.PressureFloor, 0.0, 1.0);
            double target = floor + (1.0 - floor) * (startTaper * speedFactor);

            float outP = (float)Math.Clamp(target, cfg.EndPressureFloor, 1.0);

            if (cfg.SmoothingTauMs > 0 && state.HasLastPressure)
            {
                double tau = cfg.SmoothingTauMs / 1000.0;
                double alpha = 1.0 - Math.Exp(-dtSec / tau);
                outP = (float)(state.LastPressure + (outP - state.LastPressure) * alpha);
                outP = (float)Math.Clamp(outP, cfg.EndPressureFloor, 1.0);
            }

            state.LastMm = screenMm;
            state.HasLastMm = true;
            state.LastPressure = outP;
            state.HasLastPressure = true;
            return outP;
        }

        private static double SmoothStep(double edge0, double edge1, double x)
        {
            if (edge1 <= edge0) return x < edge0 ? 0 : 1;
            double t = Math.Clamp((x - edge0) / (edge1 - edge0), 0, 1);
            return t * t * (3 - 2 * t);
        }

        private static double Lerp(double a, double b, double t) => a + (b - a) * Math.Clamp(t, 0, 1);

        private static void ApplyEndTaper(ActiveStroke active, double zoom, SimulatedPressureConfig cfg)
        {
            if (cfg.EndTaperMm <= 0) return;
            if (active.Segments.Count == 0) return;

            zoom = zoom <= 0 ? 1 : zoom;

            double coveredMm = 0;
            int edited = 0;
            StylusPoint? last = null;

            for (int s = active.Segments.Count - 1; s >= 0; s--)
            {
                var seg = active.Segments[s];
                var points = seg.StylusPoints;
                if (points == null || points.Count == 0) continue;

                for (int i = points.Count - 1; i >= 0; i--)
                {
                    var cur = points[i];
                    if (last.HasValue)
                    {
                        double distDip = (new Vector(cur.X - last.Value.X, cur.Y - last.Value.Y)).Length;
                        double distMm = distDip * zoom / (96.0 / 25.4);
                        coveredMm += distMm;
                    }

                    double u = cfg.EndTaperMm <= 0 ? 1.0 : Math.Clamp(coveredMm / cfg.EndTaperMm, 0, 1);
                    float envelope = (float)SmoothStep(0, 1, u);
                    float baseP = cur.PressureFactor;
                    float target = Math.Max(cfg.EndPressureFloor, baseP * envelope);
                    if (Math.Abs(target - baseP) > 0.0001f)
                    {
                        points[i] = new StylusPoint(cur.X, cur.Y, target);
                    }

                    edited++;
                    if (edited >= cfg.MaxEndTaperPoints || coveredMm >= cfg.EndTaperMm)
                    {
                        return;
                    }

                    last = cur;
                }
            }
        }

        private struct SimulatedPressureState
        {
            public bool Enabled;
            public long LastOutputTicks;
            public bool HasLastMm;
            public Point LastMm;
            public double LengthFromStartMm;
            public bool HasLastPressure;
            public float LastPressure;

            public SimulatedPressureState(bool enabled, long startTicks)
            {
                Enabled = enabled;
                LastOutputTicks = startTicks;
                HasLastMm = false;
                LastMm = default;
                LengthFromStartMm = 0;
                HasLastPressure = false;
                LastPressure = 0.5f;
            }
        }
    }
}

