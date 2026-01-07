using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Threading;
using WindBoard.Core.Ink;
using WindBoard.Core.Ink.Backend;
using WindBoard.Core.Input;
using WindBoard.Models;
using WindBoard.Models.Ink;

namespace WindBoard.Core.Modes
{
    public partial class InkMode : InteractionModeBase
    {
        private const double DipPerMm = 96.0 / 25.4;
        private readonly InkCanvas _canvas;
        private readonly IInkBackend _backend;
        private readonly Func<double> _zoomProvider;
        private readonly Action? _onStrokeEndedOrCanceled;
        private readonly Dictionary<int, ActiveStroke> _activeStrokes = new();
        private DispatcherTimer? _flushTimer;
        private const int MaxStylusPointsPerSegment = 1800;
        private bool _simulatedPressureEnabled;
        private StrokeSmoothingMode _strokeSmoothingMode = StrokeSmoothingMode.RawInput;

        public InkMode(InkCanvas canvas, IInkBackend backend, Func<double> zoomProvider, Action? onStrokeEndedOrCanceled = null)
        {
            _canvas = canvas;
            _backend = backend;
            _zoomProvider = zoomProvider;
            _onStrokeEndedOrCanceled = onStrokeEndedOrCanceled;
        }

        public override string Name => "Ink";

        private const float RealPressureBaseline = 0.5f;
        private const float RealPressureMeaningfulEpsilon = 0.06f;

        public void SetSimulatedPressureEnabled(bool enabled) => _simulatedPressureEnabled = enabled;

        public void SetStrokeSmoothingMode(StrokeSmoothingMode mode) => _strokeSmoothingMode = mode;

        private bool ShouldEnableDetailSmoother(InputEventArgs args)
        {
            if (_strokeSmoothingMode == StrokeSmoothingMode.RawInput)
            {
                return false;
            }

            return args.DeviceType == InputDeviceType.Touch
                   || (args.DeviceType == InputDeviceType.Stylus && !args.HasPressureHardware);
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

            try { _backend.CancelAllStrokes(); } catch { }
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
            if (zoom <= 0) zoom = 1;

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

            float initialPressure = usesRealPressure
                ? initialRealPressure
                : usesSimulatedPressure ? (simulatedPressure?.Current ?? RealPressureBaseline) : RealPressureBaseline;

            var da = _canvas.DefaultDrawingAttributes.Clone();
            da.FitToCurve = false;
            bool usesPressure = usesRealPressure || usesSimulatedPressure;
            da.IgnorePressure = !usesPressure;

            if (TryGetSimulatedPressureNominal(simulatedPressureParameters, out float nominalPressure))
            {
                da.Width /= nominalPressure;
                da.Height /= nominalPressure;
            }

            double logicalThicknessDip = da.Width * zoom;

            DetailPreservingSmoother? detailSmoother = null;
            if (ShouldEnableDetailSmoother(args))
            {
                detailSmoother = new DetailPreservingSmoother(
                    DetailPreservingSmootherParameters.NoPressureDefaults,
                    args.CanvasPoint,
                    zoom,
                    logicalThicknessDip);
            }

            var style = new InkStrokeStyle(
                InkBrushKind.Pen,
                da.Color,
                logicalThicknessDip,
                usesPressure);

            var startPoint = new InkPoint(args.CanvasPoint.X, args.CanvasPoint.Y, initialPressure, args.TimestampTicks);
            _backend.BeginStroke(id, style, startPoint, zoom);

            var active = new ActiveStroke(
                id,
                style,
                zoom,
                detailSmoother,
                args.CanvasPoint,
                args.TimestampTicks,
                usesRealPressure,
                initialRealPressure,
                hasRealPressureCandidate,
                simulatedPressure,
                startPoint);
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
            _backend.EndStroke(id);
            _activeStrokes.Remove(id);
            _onStrokeEndedOrCanceled?.Invoke();
            StopFlushTimerIfIdle();
        }

        private void AppendPoints(ActiveStroke active, InputEventArgs args, bool isFinal)
        {
            Point prevInputCanvasDip = active.LastInputCanvasDip;
            long prevInputTicks = active.LastInputTicks;

            if (!isFinal && _strokeSmoothingMode != StrokeSmoothingMode.RawInput)
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
                // 乘 zoom 可还原到屏幕空间长度，用于近似物理速度。
                double distMm = (args.CanvasPoint - prevInputCanvasDip).Length * zoom / DipPerMm;
                speedMmPerSec = distMm <= 0 ? 0 : distMm / dtSec;
            }

            if (!active.UsesRealPressure && active.HasRealPressureCandidate && args.Pressure.HasValue && ShouldSwitchToRealPressure(active, NormalizePressure(args.Pressure.Value)))
            {
                active.UsesRealPressure = true;
                active.LastRealPressure = NormalizePressure(args.Pressure.Value);

                if (!active.Style.UsesPressure)
                {
                    active.Style = active.Style with { UsesPressure = true };
                    _backend.UpdateStrokeStyle(active.PointerId, active.Style, active.ZoomAtStart);
                }
            }

            float simulatedStartPressure = 0;
            float simulatedEndPressure = 0;
            if (!active.UsesRealPressure && active.SimulatedPressure != null)
            {
                simulatedStartPressure = active.SimulatedPressure.Update(speedMmPerSec, dtSec);
                simulatedEndPressure = isFinal ? active.SimulatedPressure.Finish() : simulatedStartPressure;
            }

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
                pressure = isFinal ? simulatedEndPressure : simulatedStartPressure;
            }
            else
            {
                pressure = RealPressureBaseline;
            }

            if (active.DetailSmoother == null)
            {
                active.PendingPoints.Add(new InkPoint(args.CanvasPoint.X, args.CanvasPoint.Y, pressure, args.TimestampTicks));
                return;
            }

            var outputs = active.SmoothingScratch;
            outputs.Clear();
            active.DetailSmoother.Push(new DetailPreservingSample(args.CanvasPoint, pressure), isFinal, outputs);
            for (int i = 0; i < outputs.Count; i++)
            {
                var s = outputs[i];
                active.PendingPoints.Add(new InkPoint(s.CanvasDip.X, s.CanvasDip.Y, s.Pressure, args.TimestampTicks));
            }
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
