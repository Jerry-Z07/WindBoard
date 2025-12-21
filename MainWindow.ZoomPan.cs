using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Ink;
using System.Windows.Media;

namespace WindBoard
{
    public partial class MainWindow
    {
        // Zoom（视口缩放）
        private double _zoom = 1.0;

        private static double Clamp(double v, double min, double max) => v < min ? min : (v > max ? max : v);

        private void SetZoomAt(Point viewportPoint, double newZoom)
        {
            double oldZoom = _zoom;
            newZoom = Clamp(newZoom, MinZoom, MaxZoom);
            if (Math.Abs(newZoom - oldZoom) < 0.00001) return;

            // 将“鼠标/触点指向的屏幕点”映射到缩放前的内容坐标
            double contentX = (Viewport.HorizontalOffset + viewportPoint.X) / oldZoom;
            double contentY = (Viewport.VerticalOffset + viewportPoint.Y) / oldZoom;

            // 应用缩放
            _zoom = newZoom;
            ZoomTransform.ScaleX = _zoom;
            ZoomTransform.ScaleY = _zoom;

            // 让 ScrollViewer 更新 Extent，然后再设置 Offset（更稳）
            Viewport.UpdateLayout();

            // 调整 Offset：保证缩放后仍指向同一内容点
            Viewport.ScrollToHorizontalOffset(contentX * _zoom - viewportPoint.X);
            Viewport.ScrollToVerticalOffset(contentY * _zoom - viewportPoint.Y);

            UpdatePenThickness(_zoom);
            UpdateEraserVisual(null);
        }

        private void PanBy(Vector deltaViewport)
        {
            // 手往右拖，内容跟着往右 => ScrollOffset 减小
            Viewport.ScrollToHorizontalOffset(Viewport.HorizontalOffset - deltaViewport.X);
            Viewport.ScrollToVerticalOffset(Viewport.VerticalOffset - deltaViewport.Y);
        }

        private void UpdatePenThickness(double currentZoom)
        {
            if (currentZoom <= 0) currentZoom = 1;

            // “屏幕看起来粗细不变”：世界坐标粗细随 zoom 反比变化
            double newThickness = _baseThickness / currentZoom;

            var da = MyCanvas.DefaultDrawingAttributes;
            da.Width = newThickness;
            da.Height = newThickness;
        }
    }
}
