using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Threading;
using WindBoard.Core.Ink;
using WindBoard.Core.Input;
using StylusPoint = System.Windows.Input.StylusPoint;
using StylusPointCollection = System.Windows.Input.StylusPointCollection;

namespace WindBoard.Core.Modes
{
    public class InkMode : InteractionModeBase
    {
        private readonly InkCanvas _canvas;
        private readonly Func<double> _zoomProvider;
        private readonly Action? _onStrokeEndedOrCanceled;
        private readonly Dictionary<int, ActiveStroke> _activeStrokes = new();
        private DispatcherTimer? _flushTimer;
        private const int MaxStylusPointsPerSegment = 1800;
        private SimulatedPressureConfig _simulatedPressure = new SimulatedPressureConfig();

        public InkMode(InkCanvas canvas, Func<double> zoomProvider, Action? onStrokeEndedOrCanceled = null)
        {
            _canvas = canvas;
            _zoomProvider = zoomProvider;
            _onStrokeEndedOrCanceled = onStrokeEndedOrCanceled;
        }

        public override string Name => "Ink";

        public void SetSimulatedPressureConfig(SimulatedPressureConfig config)
        {
            _simulatedPressure = (config ?? new SimulatedPressureConfig()).Clone();
            _simulatedPressure.ClampInPlace();
        }

        public override void SwitchOn()
        {
            _canvas.EditingMode = InkCanvasEditingMode.None;
            _canvas.UseCustomCursor = false;
            _canvas.ClearValue(Control.CursorProperty);
        }

        public override void SwitchOff()
        {
            CancelAllStrokes();
            StopFlushTimerIfIdle();
        }

        public bool HasActiveStroke => _activeStrokes.Count > 0;

        public void CancelAllStrokes()
        {
            if (_activeStrokes.Count == 0) return;

            foreach (var kv in _activeStrokes)
            {
                try
                {
                    kv.Value.PendingPoints.Clear();
                    kv.Value.PendingStartIndex = 0;
                    foreach (var s in kv.Value.Segments)
                    {
                        _canvas.Strokes.Remove(s);
                    }
                }
                catch
                {
                }
            }
            _activeStrokes.Clear();
            _onStrokeEndedOrCanceled?.Invoke();
            StopFlushTimerIfIdle();
        }

        public override void OnPointerDown(InputEventArgs args)
        {
            if (args.IsInAir) return;
            if (args.DeviceType == InputDeviceType.Mouse && !args.LeftButton) return;

            int id = GetPointerKey(args);
            if (_activeStrokes.ContainsKey(id)) return;

            double zoom = _zoomProvider();
            var parameters = InkSmoothingDefaults.ForContact(args.ContactSize, zoom);
            var smoother = new RealtimeInkSmoother(parameters);

            var pointsMm = smoother.Process(args.CanvasPoint, args.TimestampTicks, zoom, isFinal: false);
            if (pointsMm.Count == 0)
            {
                return;
            }

            var cfg = _simulatedPressure;
            var pressureState = new SimulatedPressureState(cfg.Enabled, args.TimestampTicks);

            var stylusPoints = new StylusPointCollection();
            double dtPerSec = 0.016 / Math.Max(1, pointsMm.Count);
            dtPerSec = Math.Clamp(dtPerSec, 0.001, 0.05);
            for (int i = 0; i < pointsMm.Count; i++)
            {
                var pCanvas = smoother.ScreenMmToCanvasDip(pointsMm[i], zoom);
                float pressure = cfg.Enabled
                    ? NextPressure(cfg, ref pressureState, pointsMm[i], dtPerSec)
                    : 0.5f;
                stylusPoints.Add(new StylusPoint(pCanvas.X, pCanvas.Y, pressure));
            }

            var da = _canvas.DefaultDrawingAttributes.Clone();
            da.FitToCurve = false;
            da.IgnorePressure = !cfg.Enabled;

            double logicalThicknessDip = da.Width * zoom;

            var stroke = new Stroke(stylusPoints)
            {
                DrawingAttributes = da
            };
            StrokeThicknessMetadata.SetLogicalThicknessDip(stroke, logicalThicknessDip);

            _canvas.Strokes.Add(stroke);

            var active = new ActiveStroke(stroke, da, logicalThicknessDip, smoother, args.CanvasPoint, args.TimestampTicks, pressureState);
            active.Segments.Add(stroke);
            _activeStrokes[id] = active;
            EnsureFlushTimer();
        }

        public override void OnPointerMove(InputEventArgs args)
        {
            if (args.IsInAir) return;
            if (args.DeviceType == InputDeviceType.Mouse && !args.LeftButton) return;

            int id = GetPointerKey(args);
            if (!_activeStrokes.TryGetValue(id, out var active)) return;

            AppendPoints(active, args, isFinal: false);
            EnsureFlushTimer();
        }

        public override void OnPointerUp(InputEventArgs args)
        {
            int id = GetPointerKey(args);
            if (!_activeStrokes.TryGetValue(id, out var active)) return;

            AppendPoints(active, args, isFinal: true);
            FlushPendingPoints(active);
            if (_simulatedPressure.Enabled)
            {
                ApplyEndTaper(active, _zoomProvider(), _simulatedPressure);
            }
            _activeStrokes.Remove(id);
            _onStrokeEndedOrCanceled?.Invoke();
            StopFlushTimerIfIdle();
        }

        private void AppendPoints(ActiveStroke active, InputEventArgs args, bool isFinal)
        {
            if (!isFinal)
            {
                // 输入频率过高时做轻量降采样：阈值过大会导致“跟手性”下降（卡/滞后）。
                const long MinIntervalTicks = 1 * TimeSpan.TicksPerMillisecond;
                const double MinDistanceDip = 0.25;

                long dtTicks = args.TimestampTicks - active.LastInputTicks;
                if (dtTicks >= 0 && dtTicks < MinIntervalTicks)
                {
                    var dv = args.CanvasPoint - active.LastInputCanvasDip;
                    if (dv.LengthSquared < (MinDistanceDip * MinDistanceDip))
                    {
                        return;
                    }
                }
            }

            active.LastInputCanvasDip = args.CanvasPoint;
            active.LastInputTicks = args.TimestampTicks;

            double zoom = _zoomProvider();
            var pointsMm = active.Smoother.Process(args.CanvasPoint, args.TimestampTicks, zoom, isFinal);
            if (pointsMm.Count == 0) return;

            var cfg = _simulatedPressure;
            double dtTotalSec = (args.TimestampTicks - active.PressureState.LastOutputTicks) / (double)TimeSpan.TicksPerSecond;
            dtTotalSec = Math.Clamp(dtTotalSec, 0.001, 0.05);
            double dtPerSec = dtTotalSec / pointsMm.Count;
            dtPerSec = Math.Clamp(dtPerSec, 0.001, 0.05);

            for (int i = 0; i < pointsMm.Count; i++)
            {
                var pCanvas = active.Smoother.ScreenMmToCanvasDip(pointsMm[i], zoom);
                float pressure = cfg.Enabled
                    ? NextPressure(cfg, ref active.PressureState, pointsMm[i], dtPerSec)
                    : 0.5f;
                active.PendingPoints.Add(new StylusPoint(pCanvas.X, pCanvas.Y, pressure));
            }

            active.PressureState.LastOutputTicks = args.TimestampTicks;
        }

        private static int GetPointerKey(InputEventArgs args)
        {
            if (args.PointerId.HasValue) return args.PointerId.Value;
            return args.DeviceType == InputDeviceType.Mouse ? -1 : -2;
        }

        private void EnsureFlushTimer()
        {
            if (_flushTimer == null)
            {
                _flushTimer = new DispatcherTimer(DispatcherPriority.Render, _canvas.Dispatcher)
                {
                    Interval = TimeSpan.FromMilliseconds(16)
                };
                _flushTimer.Tick += (_, __) =>
                {
                    foreach (var active in _activeStrokes.Values)
                    {
                        FlushPendingPoints(active);
                    }
                    StopFlushTimerIfIdle();
                };
            }

            if (!_flushTimer.IsEnabled)
            {
                _flushTimer.Start();
            }
        }

        private void StopFlushTimerIfIdle()
        {
            if (_flushTimer == null) return;
            if (_activeStrokes.Count != 0) return;
            _flushTimer.Stop();
        }

        private void FlushPendingPoints(ActiveStroke active)
        {
            if (active.PendingPointsCount == 0) return;

            while (active.PendingPointsCount > 0)
            {
                EnsureSegmentCapacity(active, active.PendingPointsCount);

                int curCount = active.Stroke.StylusPoints.Count;
                int remain = MaxStylusPointsPerSegment - curCount;
                if (remain <= 0) continue;

                int take = Math.Min(remain, active.PendingPointsCount);
                AppendPendingBatch(active, take);
            }
        }

        private void EnsureSegmentCapacity(ActiveStroke active, int pointsToAppend)
        {
            int curCount = active.Stroke.StylusPoints.Count;
            if (curCount + pointsToAppend <= MaxStylusPointsPerSegment) return;

            // 分段：避免单个 Stroke 无限增长导致增量更新越来越慢（单笔越画越卡）。
            var last = curCount > 0
                ? active.Stroke.StylusPoints[^1]
                : new System.Windows.Input.StylusPoint(active.LastInputCanvasDip.X, active.LastInputCanvasDip.Y);

            var next = new Stroke(new System.Windows.Input.StylusPointCollection { last })
            {
                DrawingAttributes = active.DrawingAttributes
            };
            StrokeThicknessMetadata.SetLogicalThicknessDip(next, active.LogicalThicknessDip);
            _canvas.Strokes.Add(next);
            active.Segments.Add(next);
            active.Stroke = next;
        }

        private void AppendPendingBatch(ActiveStroke active, int take)
        {
            var scratch = active.ScratchPoints;
            scratch.Clear();

            int start = active.PendingStartIndex;
            for (int i = 0; i < take; i++)
            {
                scratch.Add(active.PendingPoints[start + i]);
            }

            active.Stroke.StylusPoints.Add(scratch);
            active.PendingStartIndex += take;

            if (active.PendingStartIndex >= 2048 && active.PendingStartIndex >= active.PendingPoints.Count / 2)
            {
                active.PendingPoints.RemoveRange(0, active.PendingStartIndex);
                active.PendingStartIndex = 0;
            }
        }

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

        private sealed class ActiveStroke
        {
            public Stroke Stroke { get; set; }
            public DrawingAttributes DrawingAttributes { get; }
            public double LogicalThicknessDip { get; }
            public RealtimeInkSmoother Smoother { get; }
            public Point LastInputCanvasDip { get; set; }
            public long LastInputTicks { get; set; }
            public List<Stroke> Segments { get; } = new List<Stroke>(4);
            public SimulatedPressureState PressureState;

            public List<StylusPoint> PendingPoints { get; } = new List<StylusPoint>(256);
            public int PendingStartIndex { get; set; }
            public int PendingPointsCount => PendingPoints.Count - PendingStartIndex;
            public StylusPointCollection ScratchPoints { get; }

            public ActiveStroke(Stroke stroke, DrawingAttributes drawingAttributes, double logicalThicknessDip, RealtimeInkSmoother smoother, Point lastInputCanvasDip, long lastInputTicks, SimulatedPressureState pressureState)
            {
                Stroke = stroke;
                DrawingAttributes = drawingAttributes;
                LogicalThicknessDip = logicalThicknessDip;
                Smoother = smoother;
                LastInputCanvasDip = lastInputCanvasDip;
                LastInputTicks = lastInputTicks;
                PressureState = pressureState;
                ScratchPoints = new StylusPointCollection(stroke.StylusPoints.Description, 256);
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
