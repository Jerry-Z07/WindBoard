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
    }
}
