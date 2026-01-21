using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using WindBoard;
using WindBoard.Core.Ink;
using WindBoard.Core.Input;
using WindBoard.Models.InkV2;
using WindBoard.Services.InkV2;

namespace WindBoard.Core.Modes
{
    public sealed class InkMode : InteractionModeBase
    {
        private const double DipPerMm = 96.0 / 25.4;
        private const float RealPressureBaseline = 0.5f;
        private const float RealPressureMeaningfulEpsilon = 0.06f;
        private const double MinDistanceSquaredDip = 0.25;

        private readonly FrameworkElement _inputSurface;
        private readonly Func<double> _zoomProvider;
        private readonly Func<BoardPage?> _currentPageProvider;
        private readonly Func<InkTool> _toolProvider;
        private readonly Action? _onStrokeEndedOrCanceled;
        private readonly Action? _invalidateSurface;

        private readonly Dictionary<int, ActiveStroke> _activeStrokes = new();
        private bool _simulatedPressureEnabled;

        public InkMode(
            FrameworkElement inputSurface,
            Func<double> zoomProvider,
            Func<BoardPage?> currentPageProvider,
            Func<InkTool> toolProvider,
            Action? onStrokeEndedOrCanceled = null,
            Action? invalidateSurface = null)
        {
            _inputSurface = inputSurface;
            _zoomProvider = zoomProvider;
            _currentPageProvider = currentPageProvider;
            _toolProvider = toolProvider;
            _onStrokeEndedOrCanceled = onStrokeEndedOrCanceled;
            _invalidateSurface = invalidateSurface;
        }

        public override string Name => "Ink";

        public bool HasActiveStroke => _activeStrokes.Count > 0;

        public void CollectActiveFragments(List<InkFragment> destination)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));

            foreach (var kv in _activeStrokes)
            {
                destination.Add(kv.Value.Fragment);
            }
        }

        public void SetSimulatedPressureEnabled(bool enabled) => _simulatedPressureEnabled = enabled;

        public override void SwitchOn()
        {
            _inputSurface.ClearValue(FrameworkElement.CursorProperty);
        }

        public override void SwitchOff()
        {
            CancelAllStrokes();
        }

        public void CancelAllStrokes()
        {
            if (_activeStrokes.Count == 0) return;

            foreach (var kv in _activeStrokes)
            {
                try
                {
                    RemoveUncommittedStroke(kv.Value);
                }
                catch
                {
                }
            }

            _activeStrokes.Clear();
            _onStrokeEndedOrCanceled?.Invoke();
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

            bool usesPressure = usesRealPressure || usesSimulatedPressure;
            float pressureNominal = 1.0f;
            if (usesSimulatedPressure && TryGetSimulatedPressureNominal(simulatedPressureParameters, out float nominalPressure))
            {
                pressureNominal = nominalPressure;
            }

            InkTool tool = baseTool with { UsesPressure = usesPressure, PressureNominal = pressureNominal };

            var fragment = new InkFragment();
            var stroke = new InkStroke(tool);
            stroke.Fragments.Add(fragment);

            int strokeIndex = page.Ink.Strokes.Count;
            page.Ink.Strokes.Add(stroke);

            var active = new ActiveStroke(
                page,
                stroke,
                fragment,
                strokeIndex,
                args.CanvasPoint,
                args.TimestampTicks,
                usesRealPressure,
                initialRealPressure,
                hasRealPressureCandidate,
                simulatedPressure);

            _activeStrokes.Add(id, active);

            float initialPressure = usesRealPressure
                ? initialRealPressure
                : usesSimulatedPressure && simulatedPressure != null ? simulatedPressure.Current : RealPressureBaseline;

            AppendPoint(active, args.CanvasPoint, initialPressure, args.TimestampTicks);
            _invalidateSurface?.Invoke();
        }

        public override void OnPointerMove(InputEventArgs args)
        {
            int id = GetPointerKey(args);
            if (!_activeStrokes.TryGetValue(id, out var active))
            {
                return;
            }

            if (args.IsInAir)
            {
                return;
            }

            AppendPointWithPressure(active, args, isFinal: false);
            _invalidateSurface?.Invoke();
        }

        public override void OnPointerUp(InputEventArgs args)
        {
            // Mouse 模式下仅以“左键抬起”作为一次笔迹的结束信号。
            // 右键用于平移视图，若右键抬起时左键仍按下，则不应中断当前笔迹。
            if (args.DeviceType == InputDeviceType.Mouse && args.LeftButton)
            {
                return;
            }

            int id = GetPointerKey(args);
            if (!_activeStrokes.TryGetValue(id, out var active))
            {
                return;
            }

            _activeStrokes.Remove(id);

            AppendPointWithPressure(active, args, isFinal: true);

            CommitOrDiscard(active);
            _onStrokeEndedOrCanceled?.Invoke();
        }

        private void AppendPointWithPressure(ActiveStroke active, InputEventArgs args, bool isFinal)
        {
            double dxDip = args.CanvasPoint.X - active.LastInputCanvasDip.X;
            double dyDip = args.CanvasPoint.Y - active.LastInputCanvasDip.Y;
            double dist2 = (dxDip * dxDip) + (dyDip * dyDip);
            if (dist2 < MinDistanceSquaredDip)
            {
                if (isFinal)
                {
                    active.LastInputCanvasDip = args.CanvasPoint;
                    active.LastInputTicks = args.TimestampTicks;
                }
                return;
            }

            long dtTicks = args.TimestampTicks - active.LastInputTicks;
            if (dtTicks <= 0) dtTicks = TimeSpan.TicksPerMillisecond;
            double dtSec = dtTicks / (double)TimeSpan.TicksPerSecond;

            double distDip = Math.Sqrt(dist2);
            double speedDipPerSec = distDip / Math.Max(0.0001, dtSec);
            double speedMmPerSec = speedDipPerSec / DipPerMm;

            if (active.HasRealPressureCandidate && !active.UsesRealPressure && args.Pressure.HasValue)
            {
                float normalized = NormalizePressure(args.Pressure.Value);
                if (ShouldSwitchToRealPressure(active, normalized))
                {
                    active.UsesRealPressure = true;
                }
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
                float start = active.SimulatedPressure.Update(speedMmPerSec, dtSec);
                pressure = isFinal ? active.SimulatedPressure.Finish() : start;
            }
            else
            {
                pressure = RealPressureBaseline;
            }

            AppendPoint(active, args.CanvasPoint, pressure, args.TimestampTicks);
        }

        private static void AppendPoint(ActiveStroke active, Point canvasDip, float pressure, long timestampTicks)
        {
            active.LastInputCanvasDip = canvasDip;
            active.LastInputTicks = timestampTicks;

            active.Fragment.Points.Add(new InkPoint(canvasDip.X, canvasDip.Y, pressure, timestampTicks));
        }

        private void CommitOrDiscard(ActiveStroke active)
        {
            if (active.Fragment.Points.Count < 2)
            {
                System.Diagnostics.Debug.WriteLine($"[InkMode] Discard stroke: points={active.Fragment.Points.Count}");
                RemoveUncommittedStroke(active);
                return;
            }

            try
            {
                var pts = active.Fragment.Points;
                var first = pts[0];
                var last = pts[^1];
                System.Diagnostics.Debug.WriteLine(
                    $"[InkMode] Commit stroke: points={pts.Count} index={active.StrokeIndex} " +
                    $"first=({first.XDip:F1},{first.YDip:F1}) last=({last.XDip:F1},{last.YDip:F1})");
            }
            catch
            {
            }

            BoardPage page = active.Page;
            page.InkUndoHistory.Record(new InsertStrokeCommand(active.StrokeIndex, active.Stroke));
            page.InkSpatialIndex.AddStroke(active.Stroke);
        }

        private static void RemoveUncommittedStroke(ActiveStroke active)
        {
            BoardPage page = active.Page;
            _ = page.Ink.Strokes.Remove(active.Stroke);
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

        private sealed class ActiveStroke
        {
            public ActiveStroke(
                BoardPage page,
                InkStroke stroke,
                InkFragment fragment,
                int strokeIndex,
                Point lastInputCanvasDip,
                long lastInputTicks,
                bool usesRealPressure,
                float initialRealPressure,
                bool hasRealPressureCandidate,
                SimulatedPressure? simulatedPressure)
            {
                Page = page;
                Stroke = stroke;
                Fragment = fragment;
                StrokeIndex = strokeIndex;
                LastInputCanvasDip = lastInputCanvasDip;
                LastInputTicks = lastInputTicks;
                UsesRealPressure = usesRealPressure;
                LastRealPressure = initialRealPressure;
                HasRealPressureCandidate = hasRealPressureCandidate;
                RealPressureMin = initialRealPressure;
                RealPressureMax = initialRealPressure;
                RealPressureSamples = hasRealPressureCandidate ? 1 : 0;
                SimulatedPressure = simulatedPressure;
            }

            public BoardPage Page { get; }
            public InkStroke Stroke { get; }
            public InkFragment Fragment { get; }
            public int StrokeIndex { get; }

            public Point LastInputCanvasDip { get; set; }
            public long LastInputTicks { get; set; }

            public bool UsesRealPressure { get; set; }
            public float LastRealPressure { get; set; }
            public bool HasRealPressureCandidate { get; }
            public float RealPressureMin { get; set; }
            public float RealPressureMax { get; set; }
            public int RealPressureSamples { get; set; }
            public SimulatedPressure? SimulatedPressure { get; }
        }
    }
}
