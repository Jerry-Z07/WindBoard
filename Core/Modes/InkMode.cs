using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Threading;
using WindBoard.Core.Ink;
using WindBoard.Core.Input;

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

        public InkMode(InkCanvas canvas, Func<double> zoomProvider, Action? onStrokeEndedOrCanceled = null)
        {
            _canvas = canvas;
            _zoomProvider = zoomProvider;
            _onStrokeEndedOrCanceled = onStrokeEndedOrCanceled;
        }

        public override string Name => "Ink";

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

            var stylusPoints = new System.Windows.Input.StylusPointCollection();
            for (int i = 0; i < pointsMm.Count; i++)
            {
                var pCanvas = smoother.ScreenMmToCanvasDip(pointsMm[i], zoom);
                stylusPoints.Add(new System.Windows.Input.StylusPoint(pCanvas.X, pCanvas.Y));
            }

            var da = _canvas.DefaultDrawingAttributes.Clone();
            da.FitToCurve = false;
            da.IgnorePressure = true;

            var stroke = new Stroke(stylusPoints)
            {
                DrawingAttributes = da
            };

            _canvas.Strokes.Add(stroke);

            var active = new ActiveStroke(stroke, da, smoother, args.CanvasPoint, args.TimestampTicks);
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

            for (int i = 0; i < pointsMm.Count; i++)
            {
                var pCanvas = active.Smoother.ScreenMmToCanvasDip(pointsMm[i], zoom);
                active.PendingPoints.Add(new System.Windows.Input.StylusPoint(pCanvas.X, pCanvas.Y));
            }
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

        private sealed class ActiveStroke
        {
            public Stroke Stroke { get; set; }
            public DrawingAttributes DrawingAttributes { get; }
            public RealtimeInkSmoother Smoother { get; }
            public Point LastInputCanvasDip { get; set; }
            public long LastInputTicks { get; set; }
            public List<Stroke> Segments { get; } = new List<Stroke>(4);

            public List<System.Windows.Input.StylusPoint> PendingPoints { get; } = new List<System.Windows.Input.StylusPoint>(256);
            public int PendingStartIndex { get; set; }
            public int PendingPointsCount => PendingPoints.Count - PendingStartIndex;
            public System.Windows.Input.StylusPointCollection ScratchPoints { get; } = new System.Windows.Input.StylusPointCollection(256);

            public ActiveStroke(Stroke stroke, DrawingAttributes drawingAttributes, RealtimeInkSmoother smoother, Point lastInputCanvasDip, long lastInputTicks)
            {
                Stroke = stroke;
                DrawingAttributes = drawingAttributes;
                Smoother = smoother;
                LastInputCanvasDip = lastInputCanvasDip;
                LastInputTicks = lastInputTicks;
            }
        }
    }
}
