using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WindBoard.Services
{
    public class ZoomPanService
    {
        private readonly ScrollViewer _viewport;
        private readonly ScaleTransform _zoomTransform;
        private readonly double _minZoom;
        private readonly double _maxZoom;
        private readonly Action<double>? _onZoomChanged;

        private bool _isMousePanning;
        private Point _lastMousePosition;

        private readonly Dictionary<int, Point> _activeTouches = new();
        private bool _gestureActive;
        private Point _lastGestureCenter;
        private double _lastGestureSpread;

        public double Zoom { get; private set; } = 1.0;

        public bool IsMousePanning => _isMousePanning;
        public bool IsGestureActive => _gestureActive;

        public ZoomPanService(ScrollViewer viewport, ScaleTransform zoomTransform, double minZoom = 0.5, double maxZoom = 5.0, Action<double>? onZoomChanged = null)
        {
            _viewport = viewport;
            _zoomTransform = zoomTransform;
            _minZoom = minZoom;
            _maxZoom = maxZoom;
            _onZoomChanged = onZoomChanged;
        }

        public void ZoomByWheel(Point viewportPoint, int delta)
        {
            double factor = delta > 0 ? 1.1 : 0.9;
            double newZoom = Zoom * factor;
            ZoomAt(viewportPoint, newZoom);
        }

        public void ZoomAt(Point viewportPoint, double newZoom)
        {
            double oldZoom = Zoom;
            newZoom = Clamp(newZoom);
            if (Math.Abs(newZoom - oldZoom) < 0.00001) return;

            double contentX = (_viewport.HorizontalOffset + viewportPoint.X) / oldZoom;
            double contentY = (_viewport.VerticalOffset + viewportPoint.Y) / oldZoom;

            Zoom = newZoom;
            _zoomTransform.ScaleX = Zoom;
            _zoomTransform.ScaleY = Zoom;

            _viewport.UpdateLayout();

            _viewport.ScrollToHorizontalOffset(contentX * Zoom - viewportPoint.X);
            _viewport.ScrollToVerticalOffset(contentY * Zoom - viewportPoint.Y);

            _onZoomChanged?.Invoke(Zoom);
        }

        public void SetZoomDirect(double newZoom)
        {
            Zoom = Clamp(newZoom);
            _zoomTransform.ScaleX = Zoom;
            _zoomTransform.ScaleY = Zoom;
            _viewport.UpdateLayout();
            _onZoomChanged?.Invoke(Zoom);
        }

        public void PanBy(Vector deltaViewport)
        {
            _viewport.ScrollToHorizontalOffset(_viewport.HorizontalOffset - deltaViewport.X);
            _viewport.ScrollToVerticalOffset(_viewport.VerticalOffset - deltaViewport.Y);
        }

        public void BeginMousePan(Point viewportPoint)
        {
            _isMousePanning = true;
            _lastMousePosition = viewportPoint;
        }

        public bool UpdateMousePan(Point viewportPoint)
        {
            if (!_isMousePanning) return false;

            Vector delta = viewportPoint - _lastMousePosition;
            PanBy(delta);
            _lastMousePosition = viewportPoint;
            return true;
        }

        public void EndMousePan()
        {
            _isMousePanning = false;
        }

        public bool TouchDown(int id, Point viewportPoint)
        {
            _activeTouches[id] = viewportPoint;

            if (_activeTouches.Count >= 2)
            {
                SnapshotGesture();
                _gestureActive = true;
                return true;
            }

            return false;
        }

        public bool TouchMove(int id, Point viewportPoint)
        {
            if (!_activeTouches.ContainsKey(id)) return false;

            _activeTouches[id] = viewportPoint;

            if (!_gestureActive || _activeTouches.Count < 2) return false;

            Point newCenter = GetCentroid();
            double newSpread = GetAverageSpread(newCenter);

            double oldZoom = Zoom;
            double scale = 1.0;
            if (_lastGestureSpread > 10 && newSpread > 0)
            {
                scale = newSpread / _lastGestureSpread;
            }
            double newZoom = Clamp(oldZoom * scale);

            double contentX = (_viewport.HorizontalOffset + _lastGestureCenter.X) / oldZoom;
            double contentY = (_viewport.VerticalOffset + _lastGestureCenter.Y) / oldZoom;

            Zoom = newZoom;
            _zoomTransform.ScaleX = Zoom;
            _zoomTransform.ScaleY = Zoom;

            _viewport.UpdateLayout();
            _viewport.ScrollToHorizontalOffset(contentX * Zoom - newCenter.X);
            _viewport.ScrollToVerticalOffset(contentY * Zoom - newCenter.Y);

            _lastGestureCenter = newCenter;
            _lastGestureSpread = newSpread;

            _onZoomChanged?.Invoke(Zoom);
            return true;
        }

        public bool TouchUp(int id)
        {
            bool wasActive = _gestureActive;
            _activeTouches.Remove(id);

            if (_activeTouches.Count < 2 && _gestureActive)
            {
                _gestureActive = false;
                return wasActive;
            }

            if (_gestureActive && _activeTouches.Count >= 2)
            {
                SnapshotGesture();
                return true;
            }

            return wasActive;
        }

        private void SnapshotGesture()
        {
            Point center = GetCentroid();
            double spread = GetAverageSpread(center);

            _lastGestureCenter = center;
            _lastGestureSpread = spread;
        }

        private Point GetCentroid()
        {
            double sumX = 0, sumY = 0;
            int n = _activeTouches.Count;
            foreach (var pt in _activeTouches.Values)
            {
                sumX += pt.X;
                sumY += pt.Y;
            }
            return n > 0 ? new Point(sumX / n, sumY / n) : new Point(0, 0);
        }

        private double GetAverageSpread(Point center)
        {
            double spread = 0;
            int n = _activeTouches.Count;
            foreach (var pt in _activeTouches.Values)
            {
                double dx = pt.X - center.X;
                double dy = pt.Y - center.Y;
                spread += Math.Sqrt(dx * dx + dy * dy);
            }
            return n > 0 ? spread / n : 0;
        }

        private double Clamp(double v)
        {
            if (v < _minZoom) return _minZoom;
            if (v > _maxZoom) return _maxZoom;
            return v;
        }
    }
}
