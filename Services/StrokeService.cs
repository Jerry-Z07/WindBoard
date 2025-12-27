using System;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Media;
using System.Windows.Threading;
using WindBoard.Core.Ink;

namespace WindBoard.Services
{
    public class StrokeService
    {
        private readonly InkCanvas _canvas;
        private double _baseThickness;
        private bool _strokeThicknessConsistencyEnabled;
        private DispatcherTimer? _applyAllStrokesTimer;
        private double _pendingZoom = 1.0;

        public StrokeService(InkCanvas canvas, double baseThickness = 2.0)
        {
            _canvas = canvas;
            _baseThickness = baseThickness;
        }

        public double BaseThickness => _baseThickness;
        public bool StrokeThicknessConsistencyEnabled => _strokeThicknessConsistencyEnabled;

        public void SetBaseThickness(double thickness, double currentZoom)
        {
            _baseThickness = thickness;
            UpdatePenThickness(currentZoom);
        }

        public void SetStrokeThicknessConsistencyEnabled(bool enabled, double currentZoom)
        {
            _strokeThicknessConsistencyEnabled = enabled;
            if (enabled)
            {
                ScheduleApplyThicknessToAllStrokes(currentZoom);
            }
        }

        public void UpdatePenThickness(double zoom)
        {
            if (zoom <= 0) zoom = 1;

            double newThickness = _baseThickness / zoom;

            var da = _canvas.DefaultDrawingAttributes;
            da.Width = newThickness;
            da.Height = newThickness;

            if (_strokeThicknessConsistencyEnabled)
            {
                ScheduleApplyThicknessToAllStrokes(zoom);
            }
        }

        public void SetColor(Color color)
        {
            _canvas.DefaultDrawingAttributes.Color = color;
        }

        private void ScheduleApplyThicknessToAllStrokes(double zoom)
        {
            if (zoom <= 0) zoom = 1;
            _pendingZoom = zoom;

            if (_applyAllStrokesTimer == null)
            {
                _applyAllStrokesTimer = new DispatcherTimer(DispatcherPriority.Render, _canvas.Dispatcher)
                {
                    Interval = TimeSpan.FromMilliseconds(16)
                };
                _applyAllStrokesTimer.Tick += (_, __) =>
                {
                    _applyAllStrokesTimer.Stop();
                    ApplyThicknessToAllStrokes(_pendingZoom);
                };
            }

            if (!_applyAllStrokesTimer.IsEnabled)
            {
                _applyAllStrokesTimer.Start();
            }
        }

        private void ApplyThicknessToAllStrokes(double zoom)
        {
            if (zoom <= 0) zoom = 1;

            var strokes = _canvas.Strokes;
            if (strokes == null || strokes.Count == 0) return;

            for (int i = 0; i < strokes.Count; i++)
            {
                var stroke = strokes[i];
                if (stroke == null) continue;

                double logicalDip = StrokeThicknessMetadata.GetOrCreateLogicalThicknessDip(stroke, zoom);
                if (logicalDip <= 0) continue;

                double renderDip = logicalDip / zoom;
                if (double.IsNaN(renderDip) || double.IsInfinity(renderDip) || renderDip <= 0) continue;

                var da = stroke.DrawingAttributes;
                da.Width = renderDip;
                da.Height = renderDip;
            }
        }
    }
}
