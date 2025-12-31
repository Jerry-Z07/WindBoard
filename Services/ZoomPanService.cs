using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace WindBoard.Services
{
    public class ZoomPanService
    {
        private enum GesturePointSource
        {
            Raw,
            Smoothed
        }

        private readonly struct GestureSnapshot
        {
            public GestureSnapshot(Point center, double spread)
            {
                Center = center;
                Spread = spread;
            }

            public Point Center { get; }
            public double Spread { get; }
        }

        private readonly ScaleTransform _zoomTransform;
        private readonly double _minZoom;
        private readonly double _maxZoom;
        private readonly Action<double>? _onZoomChanged;
        private readonly TranslateTransform _panTransform;

        private bool _isMousePanning;
        private Point _lastMousePosition;

        private readonly Dictionary<int, Point> _activeTouches = new();
        private readonly Dictionary<int, Point> _smoothedTouches = new();  // 滤波后的触摸点
        private bool _gestureActive;
        private Point _lastGestureCenter;
        private double _lastGestureSpread;

        // 噪声过滤参数
        private const double TouchSmoothingFactor = 0.4;  // 低通滤波系数 (0-1, 越小越平滑)
        private const double MinSpreadThreshold = 30.0;   // 最小有效手指距离(px)
        private const double MaxScaleChangePerFrame = 0.08; // 单帧最大缩放变化率

        public double Zoom { get; private set; } = 1.0;
        public double PanX { get; private set; }
        public double PanY { get; private set; }

        public bool TwoFingerOnly { get; set; }

        public bool IsMousePanning => _isMousePanning;
        public bool IsGestureActive => _gestureActive;

        public ZoomPanService(ScaleTransform zoomTransform, TranslateTransform panTransform, double minZoom = 0.5, double maxZoom = 5.0, Action<double>? onZoomChanged = null)
        {
            _zoomTransform = zoomTransform;
            _panTransform = panTransform;
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

            double contentX = (viewportPoint.X - PanX) / oldZoom;
            double contentY = (viewportPoint.Y - PanY) / oldZoom;

            Zoom = newZoom;
            _zoomTransform.ScaleX = Zoom;
            _zoomTransform.ScaleY = Zoom;

            SetPanDirect(viewportPoint.X - contentX * Zoom, viewportPoint.Y - contentY * Zoom);

            _onZoomChanged?.Invoke(Zoom);
        }

        public void SetZoomDirect(double newZoom)
        {
            Zoom = Clamp(newZoom);
            _zoomTransform.ScaleX = Zoom;
            _zoomTransform.ScaleY = Zoom;
            _onZoomChanged?.Invoke(Zoom);
        }

        public void PanBy(Vector deltaViewport)
        {
            SetPanDirect(PanX + deltaViewport.X, PanY + deltaViewport.Y);
        }

        public void SetPanDirect(double panX, double panY)
        {
            PanX = panX;
            PanY = panY;
            _panTransform.X = PanX;
            _panTransform.Y = PanY;
        }

        public void SetViewDirect(double zoom, double panX, double panY)
        {
            SetZoomDirect(zoom);
            SetPanDirect(panX, panY);
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
            _smoothedTouches[id] = viewportPoint;  // 初始化时直接使用原始位置

            if (_activeTouches.Count >= 2)
            {
                // 每次手指数量变化都更新快照
                // 这样增量计算才能从正确的基准开始，避免重心/spread 跳跃
                SnapshotGesture(GesturePointSource.Smoothed);

                if (!_gestureActive)
                {
                    _gestureActive = true;
                }
                return true;
            }

            return false;
        }

        public bool TouchMove(int id, Point viewportPoint)
        {
            if (!_activeTouches.ContainsKey(id)) return false;

            _activeTouches[id] = viewportPoint;
            UpdateSmoothedTouch(id, viewportPoint);

            if (!_gestureActive || _activeTouches.Count < 2) return false;
            if (TwoFingerOnly && _activeTouches.Count > 2) return false;

            // 使用滤波后的触摸点计算重心和距离
            var snapshot = GetGestureSnapshot(GesturePointSource.Smoothed);
            Point newCenter = snapshot.Center;
            double newSpread = snapshot.Spread;

            // 计算重心增量（用于平移）
            Vector deltaCenter = newCenter - _lastGestureCenter;

            double oldZoom = Zoom;
            double scale = 1.0;

            // 只有当两指距离足够大时才计算缩放，避免小距离时的数值不稳定
            if (_lastGestureSpread > MinSpreadThreshold && newSpread > MinSpreadThreshold)
            {
                scale = newSpread / _lastGestureSpread;
                // 限制单帧缩放变化率，防止跳跃
                scale = Math.Clamp(scale, 1.0 - MaxScaleChangePerFrame, 1.0 + MaxScaleChangePerFrame);
            }

            double newZoom = Clamp(oldZoom * scale);

            // 使用增量模式处理平移和缩放：
            // 1. 先应用增量平移
            double panX = PanX + deltaCenter.X;
            double panY = PanY + deltaCenter.Y;

            // 2. 以 newCenter 为中心应用缩放（如果有缩放变化）
            if (Math.Abs(newZoom - oldZoom) > 0.00001)
            {
                // 计算 newCenter 在当前视图中指向的内容位置
                double contentX = (newCenter.X - panX) / oldZoom;
                double contentY = (newCenter.Y - panY) / oldZoom;

                // 更新缩放
                Zoom = newZoom;
                _zoomTransform.ScaleX = Zoom;
                _zoomTransform.ScaleY = Zoom;

                // 缩放后保持 newCenter 指向同一内容位置
                panX = newCenter.X - contentX * Zoom;
                panY = newCenter.Y - contentY * Zoom;

                _onZoomChanged?.Invoke(Zoom);
            }

            SetPanDirect(panX, panY);

            // 更新快照
            _lastGestureCenter = newCenter;
            _lastGestureSpread = newSpread;

            return true;
        }

        public bool TouchUp(int id)
        {
            bool wasActive = _gestureActive;
            _activeTouches.Remove(id);
            _smoothedTouches.Remove(id);

            if (_activeTouches.Count < 2 && _gestureActive)
            {
                _gestureActive = false;
                return wasActive;
            }

            if (_gestureActive && _activeTouches.Count >= 2)
            {
                // 手指数量变化时更新快照，这样增量计算才能从正确的基准开始
                SnapshotGesture(GesturePointSource.Smoothed);
                return true;
            }

            return wasActive;
        }

        private void UpdateSmoothedTouch(int id, Point viewportPoint)
        {
            // 对触摸点应用低通滤波以消除硬件噪声
            if (_smoothedTouches.TryGetValue(id, out var prevSmoothed))
            {
                _smoothedTouches[id] = new Point(
                    prevSmoothed.X + (viewportPoint.X - prevSmoothed.X) * TouchSmoothingFactor,
                    prevSmoothed.Y + (viewportPoint.Y - prevSmoothed.Y) * TouchSmoothingFactor
                );
                return;
            }

            _smoothedTouches[id] = viewportPoint;
        }

        private void SnapshotGesture(GesturePointSource source)
        {
            var snapshot = GetGestureSnapshot(source);
            _lastGestureCenter = snapshot.Center;
            _lastGestureSpread = snapshot.Spread;
        }

        private GestureSnapshot GetGestureSnapshot(GesturePointSource source)
        {
            double sumX = 0;
            double sumY = 0;
            int count = 0;

            if (source == GesturePointSource.Smoothed)
            {
                foreach (var id in _activeTouches.Keys)
                {
                    if (_smoothedTouches.TryGetValue(id, out var pt))
                    {
                        sumX += pt.X;
                        sumY += pt.Y;
                        count++;
                    }
                }
            }
            else
            {
                foreach (var pt in _activeTouches.Values)
                {
                    sumX += pt.X;
                    sumY += pt.Y;
                    count++;
                }
            }

            Point center = count > 0 ? new Point(sumX / count, sumY / count) : new Point(0, 0);

            double spread = 0;

            if (source == GesturePointSource.Smoothed)
            {
                foreach (var id in _activeTouches.Keys)
                {
                    if (_smoothedTouches.TryGetValue(id, out var pt))
                    {
                        double dx = pt.X - center.X;
                        double dy = pt.Y - center.Y;
                        spread += Math.Sqrt(dx * dx + dy * dy);
                    }
                }
            }
            else
            {
                foreach (var pt in _activeTouches.Values)
                {
                    double dx = pt.X - center.X;
                    double dy = pt.Y - center.Y;
                    spread += Math.Sqrt(dx * dx + dy * dy);
                }
            }

            return new GestureSnapshot(center, count > 0 ? spread / count : 0);
        }

        private double Clamp(double v)
        {
            if (v < _minZoom) return _minZoom;
            if (v > _maxZoom) return _maxZoom;
            return v;
        }
    }
}
