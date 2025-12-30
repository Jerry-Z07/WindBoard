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
    public partial class InkMode : InteractionModeBase
    {
        private const double DipPerMm = 96.0 / 25.4;
        private readonly InkCanvas _canvas;
        private readonly Func<double> _zoomProvider;
        private readonly Action? _onStrokeEndedOrCanceled;
        private readonly Dictionary<int, ActiveStroke> _activeStrokes = new();
        private DispatcherTimer? _flushTimer;
        private const int MaxStylusPointsPerSegment = 1800;
        private bool _simulatedPressureEnabled;

        public InkMode(InkCanvas canvas, Func<double> zoomProvider, Action? onStrokeEndedOrCanceled = null)
        {
            _canvas = canvas;
            _zoomProvider = zoomProvider;
            _onStrokeEndedOrCanceled = onStrokeEndedOrCanceled;
        }

        public override string Name => "Ink";

        private const float RealPressureBaseline = 0.5f;
        private const float RealPressureMeaningfulEpsilon = 0.06f;

        public void SetSimulatedPressureEnabled(bool enabled) => _simulatedPressureEnabled = enabled;

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

            bool hasRealPressureCandidate = args.DeviceType == InputDeviceType.Stylus && args.Pressure.HasValue;
            float initialRealPressure = hasRealPressureCandidate ? NormalizePressure(args.Pressure!.Value) : RealPressureBaseline;
            bool usesRealPressure = hasRealPressureCandidate && IsRealPressureLikely(initialRealPressure);

            bool usesSimulatedPressure = _simulatedPressureEnabled && !usesRealPressure && !hasRealPressureCandidate;
            SimulatedPressure? simulatedPressure = null;
            SimulatedPressureParameters? simulatedPressureParameters = null;
            if (usesSimulatedPressure)
            {
                simulatedPressureParameters = SimulatedPressureDefaults.ForContact(args.ContactSize, zoom);
                simulatedPressure = new SimulatedPressure(simulatedPressureParameters);
            }

            var stylusPoints = new StylusPointCollection();
            float initialPressure = usesRealPressure
                ? initialRealPressure
                : usesSimulatedPressure ? (simulatedPressure?.Current ?? RealPressureBaseline) : RealPressureBaseline;
            for (int i = 0; i < pointsMm.Count; i++)
            {
                var pCanvas = smoother.ScreenMmToCanvasDip(pointsMm[i], zoom);
                stylusPoints.Add(new StylusPoint(pCanvas.X, pCanvas.Y, initialPressure));
            }

            // 触摸书写：为减少实时“跟手”延迟，在 stroke 尾部维持一个可移动的 LiveTail 点（始终等于当前 raw 输入）。
            // 后续 Move 会更新该点的位置；Flush 时把新点插到 tail 之前，避免 tail 被“顶”到中间。
            bool liveTailEnabled = args.DeviceType == InputDeviceType.Touch;
            if (liveTailEnabled && stylusPoints.Count > 0)
            {
                stylusPoints.Add(new StylusPoint(args.CanvasPoint.X, args.CanvasPoint.Y, initialPressure));
            }

            var da = _canvas.DefaultDrawingAttributes.Clone();
            da.FitToCurve = true;
            da.IgnorePressure = !(usesRealPressure || usesSimulatedPressure);

            if (TryGetSimulatedPressureNominal(simulatedPressureParameters, out float nominalPressure))
            {
                da.Width /= nominalPressure;
                da.Height /= nominalPressure;
            }

            double logicalThicknessDip = da.Width * zoom;

            var stroke = new Stroke(stylusPoints)
            {
                DrawingAttributes = da
            };
            StrokeThicknessMetadata.SetLogicalThicknessDip(stroke, logicalThicknessDip);

            _canvas.Strokes.Add(stroke);

            var active = new ActiveStroke(stroke, da, logicalThicknessDip, smoother, args.CanvasPoint, args.TimestampTicks, usesRealPressure, initialRealPressure, hasRealPressureCandidate, simulatedPressure);
            active.LiveTailEnabled = liveTailEnabled;
            active.LiveTailPressure = initialPressure;
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
            UpdateLiveTail(active, args.CanvasPoint);
            EnsureFlushTimer();
        }

        public override void OnPointerUp(InputEventArgs args)
        {
            int id = GetPointerKey(args);
            if (!_activeStrokes.TryGetValue(id, out var active)) return;

            UpdateLiveTail(active, args.CanvasPoint);
            AppendPoints(active, args, isFinal: true);
            FlushPendingPoints(active);
            TryRemoveLiveTailIfDuplicate(active);
            _activeStrokes.Remove(id);
            _onStrokeEndedOrCanceled?.Invoke();
            StopFlushTimerIfIdle();
        }

        private void AppendPoints(ActiveStroke active, InputEventArgs args, bool isFinal)
        {
            Point prevInputCanvasDip = active.LastInputCanvasDip;
            long prevInputTicks = active.LastInputTicks;

            if (!isFinal)
            {
                // 输入频率过高时做轻量降采样：阈值过大会导致“跟手性”下降（卡/滞后）。
                const long MinIntervalTicks = 1 * TimeSpan.TicksPerMillisecond;
                const double MinDistanceDip = 0.25;

                long dtTicks = args.TimestampTicks - prevInputTicks;
                if (dtTicks >= 0 && dtTicks < MinIntervalTicks)
                {
                    var dv = args.CanvasPoint - prevInputCanvasDip;
                    if (dv.LengthSquared < (MinDistanceDip * MinDistanceDip))
                    {
                        return;
                    }
                }
            }

            active.LastInputCanvasDip = args.CanvasPoint;
            active.LastInputTicks = args.TimestampTicks;

            double zoom = _zoomProvider();
            if (zoom <= 0) zoom = 1;

            double dtSec = 0.016;
            double speedMmPerSec = 0;
            if (active.SimulatedPressure != null)
            {
                dtSec = (args.TimestampTicks - prevInputTicks) / (double)TimeSpan.TicksPerSecond;
                dtSec = Math.Clamp(dtSec, 0.001, 0.05);

                // CanvasPoint 是画布坐标（RenderTransform 下会被逆变换回“未缩放”的 DIP）；
                // 乘 zoom 可还原到屏幕空间长度，用于近似物理速度（与 RealtimeInkSmoother.CanvasToScreenMm 一致）。
                double distMm = (args.CanvasPoint - prevInputCanvasDip).Length * zoom / DipPerMm;
                speedMmPerSec = distMm <= 0 ? 0 : distMm / dtSec;
            }
            var pointsMm = active.Smoother.Process(args.CanvasPoint, args.TimestampTicks, zoom, isFinal);
            if (pointsMm.Count == 0) return;

            if (!active.UsesRealPressure && active.HasRealPressureCandidate && args.Pressure.HasValue && ShouldSwitchToRealPressure(active, NormalizePressure(args.Pressure.Value)))
            {
                active.UsesRealPressure = true;
                active.LastRealPressure = NormalizePressure(args.Pressure.Value);
                active.DrawingAttributes.IgnorePressure = false;
            }

            float simulatedStartPressure = 0;
            float simulatedEndPressure = 0;
            if (!active.UsesRealPressure && active.SimulatedPressure != null)
            {
                simulatedStartPressure = active.SimulatedPressure.Update(speedMmPerSec, dtSec);
                simulatedEndPressure = isFinal ? active.SimulatedPressure.Finish() : simulatedStartPressure;
            }

            float lastPressure = active.LiveTailPressure;
            for (int i = 0; i < pointsMm.Count; i++)
            {
                var pCanvas = active.Smoother.ScreenMmToCanvasDip(pointsMm[i], zoom);
                float pressure;
                if (active.UsesRealPressure)
                {
                    if (args.Pressure.HasValue)
                    {
                        active.LastRealPressure = NormalizePressure(args.Pressure.Value);
                    }
                    pressure = active.LastRealPressure;
                }
                else if (active.SimulatedPressure != null)
                {
                    if (isFinal)
                    {
                        float t = (i + 1) / (float)pointsMm.Count;
                        pressure = simulatedStartPressure + (simulatedEndPressure - simulatedStartPressure) * t;
                    }
                    else
                    {
                        pressure = simulatedStartPressure;
                    }
                }
                else
                {
                    pressure = RealPressureBaseline;
                }
                active.PendingPoints.Add(new StylusPoint(pCanvas.X, pCanvas.Y, pressure));
                lastPressure = pressure;
            }

            if (active.LiveTailEnabled)
            {
                active.LiveTailPressure = lastPressure;
            }
        }

        private static void UpdateLiveTail(ActiveStroke active, Point rawCanvasDip)
        {
            if (!active.LiveTailEnabled)
            {
                return;
            }

            var spc = active.Stroke.StylusPoints;
            if (spc.Count == 0)
            {
                var p = new StylusPoint(rawCanvasDip.X, rawCanvasDip.Y, active.LiveTailPressure);
                spc.Add(p);
                spc.Add(p);
                return;
            }

            if (spc.Count == 1)
            {
                spc.Add(spc[0]);
            }

            spc.RemoveAt(spc.Count - 1);
            spc.Add(new StylusPoint(rawCanvasDip.X, rawCanvasDip.Y, active.LiveTailPressure));
        }

        private static void TryRemoveLiveTailIfDuplicate(ActiveStroke active)
        {
            if (!active.LiveTailEnabled)
            {
                return;
            }

            var spc = active.Stroke.StylusPoints;
            if (spc.Count < 2)
            {
                return;
            }

            var a = spc[^2];
            var b = spc[^1];

            const double posEps = 0.0001;
            const double pressureEps = 0.0001;
            if (Math.Abs(a.X - b.X) <= posEps
                && Math.Abs(a.Y - b.Y) <= posEps
                && Math.Abs(a.PressureFactor - b.PressureFactor) <= pressureEps)
            {
                spc.RemoveAt(spc.Count - 1);
            }

            active.LiveTailEnabled = false;
        }

        private static int GetPointerKey(InputEventArgs args)
        {
            if (args.PointerId.HasValue) return args.PointerId.Value;
            return args.DeviceType == InputDeviceType.Mouse ? -1 : -2;
        }

        private static float NormalizePressure(double pressure)
        {
            return (float)Math.Clamp(pressure, 0.0, 1.0);
        }

        private static bool IsRealPressureLikely(float pressure)
        {
            return Math.Abs(pressure - RealPressureBaseline) >= RealPressureMeaningfulEpsilon;
        }

        private static bool ShouldSwitchToRealPressure(ActiveStroke active, float pressure)
        {
            active.RealPressureSamples++;
            active.RealPressureMin = Math.Min(active.RealPressureMin, pressure);
            active.RealPressureMax = Math.Max(active.RealPressureMax, pressure);

            if (IsRealPressureLikely(pressure))
            {
                return true;
            }

            return (active.RealPressureMax - active.RealPressureMin) >= RealPressureMeaningfulEpsilon;
        }

        private static bool TryGetSimulatedPressureNominal(SimulatedPressureParameters? parameters, out float nominalPressure)
        {
            nominalPressure = 0;
            if (parameters == null) return false;

            float nominal = parameters.PressureNominal;
            if (float.IsNaN(nominal) || float.IsInfinity(nominal)) return false;
            if (nominal <= 0.05f || nominal > 1.0f) return false;

            nominalPressure = nominal;
            return true;
        }
    }
}
