using System;
using System.Collections.Generic;
using System.Windows.Controls;
using System.Windows.Ink;
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
        }

        public bool HasActiveStroke => _activeStrokes.Count > 0;

        public void CancelAllStrokes()
        {
            if (_activeStrokes.Count == 0) return;

            foreach (var kv in _activeStrokes)
            {
                try
                {
                    _canvas.Strokes.Remove(kv.Value.Stroke);
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

            var active = new ActiveStroke(stroke, smoother);
            _activeStrokes[id] = active;
        }

        public override void OnPointerMove(InputEventArgs args)
        {
            if (args.IsInAir) return;
            if (args.DeviceType == InputDeviceType.Mouse && !args.LeftButton) return;

            int id = GetPointerKey(args);
            if (!_activeStrokes.TryGetValue(id, out var active)) return;

            AppendPoints(active, args, isFinal: false);
        }

        public override void OnPointerUp(InputEventArgs args)
        {
            int id = GetPointerKey(args);
            if (!_activeStrokes.TryGetValue(id, out var active)) return;

            AppendPoints(active, args, isFinal: true);
            _activeStrokes.Remove(id);
            _onStrokeEndedOrCanceled?.Invoke();
        }

        private void AppendPoints(ActiveStroke active, InputEventArgs args, bool isFinal)
        {
            double zoom = _zoomProvider();
            var pointsMm = active.Smoother.Process(args.CanvasPoint, args.TimestampTicks, zoom, isFinal);
            if (pointsMm.Count == 0) return;

            var pointsToAdd = new System.Windows.Input.StylusPointCollection(pointsMm.Count);
            for (int i = 0; i < pointsMm.Count; i++)
            {
                var pCanvas = active.Smoother.ScreenMmToCanvasDip(pointsMm[i], zoom);
                pointsToAdd.Add(new System.Windows.Input.StylusPoint(pCanvas.X, pCanvas.Y));
            }
            active.Stroke.StylusPoints.Add(pointsToAdd);
        }

        private static int GetPointerKey(InputEventArgs args)
        {
            if (args.PointerId.HasValue) return args.PointerId.Value;
            return args.DeviceType == InputDeviceType.Mouse ? -1 : -2;
        }

        private sealed record ActiveStroke(Stroke Stroke, RealtimeInkSmoother Smoother);
    }
}
