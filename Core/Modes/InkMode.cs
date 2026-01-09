using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Threading;
using WindBoard;
using WindBoard.Core.Ink;
using WindBoard.Core.Input;
using WindBoard.Models.InkV2;
using WindBoard.Services.InkV2;
using StylusPoint = System.Windows.Input.StylusPoint;
using StylusPointCollection = System.Windows.Input.StylusPointCollection;

namespace WindBoard.Core.Modes
{
    public partial class InkMode : InteractionModeBase
    {
        private const double DipPerMm = 96.0 / 25.4;
        private readonly InkCanvas _canvas;
        private readonly Func<double> _zoomProvider;
        private readonly Func<BoardPage?> _currentPageProvider;
        private readonly Func<InkTool> _toolProvider;
        private readonly Action? _onStrokeEndedOrCanceled;
        private readonly Dictionary<int, ActiveStroke> _activeStrokes = new();
        private DispatcherTimer? _flushTimer;
        private const int MaxStylusPointsPerSegment = 1800;
        private bool _simulatedPressureEnabled;
        private bool _smoothingEnabled = true;

        public InkMode(
            InkCanvas canvas,
            Func<double> zoomProvider,
            Func<BoardPage?> currentPageProvider,
            Func<InkTool> toolProvider,
            Action? onStrokeEndedOrCanceled = null)
        {
            _canvas = canvas;
            _zoomProvider = zoomProvider;
            _currentPageProvider = currentPageProvider;
            _toolProvider = toolProvider;
            _onStrokeEndedOrCanceled = onStrokeEndedOrCanceled;
        }

        public override string Name => "Ink";

        private const float RealPressureBaseline = 0.5f;
        private const float RealPressureMeaningfulEpsilon = 0.06f;

        public void SetSimulatedPressureEnabled(bool enabled) => _simulatedPressureEnabled = enabled;

        public void SetSmoothingEnabled(bool enabled) => _smoothingEnabled = enabled;

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

            BoardPage? page = _currentPageProvider();
            if (page == null) return;

            double zoom = _zoomProvider();
            if (zoom <= 0) zoom = 1;

            InkTool baseTool = _toolProvider();

            bool hasRealPressureCandidate = args.DeviceType == InputDeviceType.Stylus && args.HasPressureHardware && args.Pressure.HasValue;
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

            bool usesPressure = usesRealPressure || usesSimulatedPressure;
            float pressureNominal = 1.0f;
            if (usesSimulatedPressure && TryGetSimulatedPressureNominal(simulatedPressureParameters, out float nominalPressure))
            {
                pressureNominal = nominalPressure;
            }

            float initialPressure = usesRealPressure
                ? initialRealPressure
                : usesSimulatedPressure ? (simulatedPressure?.Current ?? RealPressureBaseline) : RealPressureBaseline;
            stylusPoints.Add(new StylusPoint(args.CanvasPoint.X, args.CanvasPoint.Y, initialPressure));

            InkTool tool = baseTool with
            {
                UsesPressure = usesPressure,
                PressureNominal = pressureNominal
            };

            double logicalThicknessDip = InkToolThickness.ComputeLogicalThicknessDip(tool);
            var da = CreateDrawingAttributes(tool, zoom, logicalThicknessDip);

            DetailPreservingSmoother? detailSmoother = null;
            if (_smoothingEnabled)
            {
                detailSmoother = new DetailPreservingSmoother(
                    DetailPreservingSmootherParameters.NoPressureDefaults,
                    args.CanvasPoint,
                    zoom,
                    InkToolThickness.ComputeScreenThicknessDip(tool, zoom, logicalThicknessDip));
            }

            var stroke = new Stroke(stylusPoints)
            {
                DrawingAttributes = da
            };
            StrokeThicknessMetadata.SetLogicalThicknessDip(stroke, logicalThicknessDip);
            StrokeInkSemanticsMetadata.SetThicknessSemantics(stroke, tool.ThicknessSemantics);
            Guid strokeId = Guid.NewGuid();
            StrokeInkSemanticsMetadata.SetInkStrokeId(stroke, strokeId);

            _canvas.Strokes.Add(stroke);

            var fragment = new InkFragment();
            fragment.Points.Add(new InkPoint(args.CanvasPoint.X, args.CanvasPoint.Y, initialPressure, args.TimestampTicks));

            var active = new ActiveStroke(
                page,
                strokeId,
                tool,
                fragment,
                stroke,
                da,
                logicalThicknessDip,
                detailSmoother,
                args.CanvasPoint,
                args.TimestampTicks,
                usesRealPressure,
                initialRealPressure,
                hasRealPressureCandidate,
                simulatedPressure);
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
            _activeStrokes.Remove(id);

            CommitStroke(active);
            _onStrokeEndedOrCanceled?.Invoke();
            StopFlushTimerIfIdle();
        }

        private void AppendPoints(ActiveStroke active, InputEventArgs args, bool isFinal)
        {
            Point prevInputCanvasDip = active.LastInputCanvasDip;
            long prevInputTicks = active.LastInputTicks;

            if (!isFinal && _smoothingEnabled)
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
                active.DrawingAttributes.IgnorePressure = false;
                active.Tool = active.Tool with { UsesPressure = true, PressureNominal = 1.0f };
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
                AppendOutputPoint(active, args.CanvasPoint, pressure, args.TimestampTicks);
                return;
            }

            var outputs = active.SmoothingScratch;
            outputs.Clear();
            active.DetailSmoother.Push(new DetailPreservingSample(args.CanvasPoint, pressure), isFinal, outputs);
            for (int i = 0; i < outputs.Count; i++)
            {
                var s = outputs[i];
                AppendOutputPoint(active, s.CanvasDip, s.Pressure, args.TimestampTicks);
            }
        }

        private static void AppendOutputPoint(ActiveStroke active, Point canvasDip, float pressure, long timestampTicks)
        {
            active.PendingPoints.Add(new StylusPoint(canvasDip.X, canvasDip.Y, pressure));
            active.Fragment.Points.Add(new InkPoint(canvasDip.X, canvasDip.Y, pressure, timestampTicks));
        }

        private void CommitStroke(ActiveStroke active)
        {
            if (active.Fragment.Points.Count < 2)
            {
                try
                {
                    for (int i = 0; i < active.Segments.Count; i++)
                    {
                        _canvas.Strokes.Remove(active.Segments[i]);
                    }
                }
                catch
                {
                }
                return;
            }

            InkTool finalTool = active.Tool;
            var stroke = new InkStroke(active.StrokeId, finalTool);
            stroke.Fragments.Add(active.Fragment);

            BoardPage page = active.Page;
            int index = page.Ink.Strokes.Count;
            page.Ink.Strokes.Add(stroke);
            page.InkUndoHistory.Record(new InsertStrokeCommand(index, stroke));

            page.InkSpatialIndex.Rebuild(page.Ink);
        }

        private static DrawingAttributes CreateDrawingAttributes(InkTool tool, double zoom, double logicalThicknessDip)
        {
            double renderThicknessDip = InkToolThickness.ComputeRenderThicknessDip(tool, zoom, logicalThicknessDip);

            var da = new DrawingAttributes
            {
                FitToCurve = false,
                IgnorePressure = !tool.UsesPressure,
                Width = renderThicknessDip,
                Height = renderThicknessDip
            };

            da.Color = ColorFromArgb(tool.ColorArgb);
            return da;
        }

        private static System.Windows.Media.Color ColorFromArgb(uint argb)
        {
            byte a = (byte)((argb >> 24) & 0xFF);
            byte r = (byte)((argb >> 16) & 0xFF);
            byte g = (byte)((argb >> 8) & 0xFF);
            byte b = (byte)(argb & 0xFF);
            return System.Windows.Media.Color.FromArgb(a, r, g, b);
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
