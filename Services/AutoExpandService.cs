using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using WindBoard;

namespace WindBoard.Services
{
    public class AutoExpandService
    {
        private readonly InkCanvas _canvas;
        private readonly ScrollViewer _viewport;
        private readonly ZoomPanService _zoomPanService;
        private readonly Func<BoardPage?> _currentPageProvider;
        private readonly Func<bool>? _isInkingActiveProvider;

        private double _pendingShiftX;
        private double _pendingShiftY;

        public AutoExpandService(InkCanvas canvas, ScrollViewer viewport, ZoomPanService zoomPanService, Func<BoardPage?> currentPageProvider, Func<bool>? isInkingActiveProvider = null)
        {
            _canvas = canvas;
            _viewport = viewport;
            _zoomPanService = zoomPanService;
            _currentPageProvider = currentPageProvider;
            _isInkingActiveProvider = isInkingActiveProvider;
        }

        public void EnsureCanvasSpace(Point canvasPoint)
        {
            const double ExpansionThreshold = 1000.0;
            const double ExpansionStep = 2000.0;

            double expandLeft = 0, expandTop = 0, expandRight = 0, expandBottom = 0;

            if (canvasPoint.X < ExpansionThreshold) expandLeft = ExpansionStep;
            if (canvasPoint.Y < ExpansionThreshold) expandTop = ExpansionStep;

            if (canvasPoint.X > _canvas.Width - ExpansionThreshold) expandRight = ExpansionStep;
            if (canvasPoint.Y > _canvas.Height - ExpansionThreshold) expandBottom = ExpansionStep;

            if (expandLeft == 0 && expandTop == 0 && expandRight == 0 && expandBottom == 0)
                return;

            double newW = _canvas.Width + expandLeft + expandRight;
            double newH = _canvas.Height + expandTop + expandBottom;
            double newSize = Math.Max(newW, newH);

            if (newSize > _canvas.Width || newSize > _canvas.Height)
            {
                _canvas.Width = newSize;
                _canvas.Height = newSize;

                var currentPage = _currentPageProvider();
                if (currentPage != null)
                {
                    currentPage.CanvasWidth = _canvas.Width;
                    currentPage.CanvasHeight = _canvas.Height;
                }
            }

            if (expandLeft > 0 || expandTop > 0)
            {
                bool inkingActive = _isInkingActiveProvider?.Invoke()
                                    ?? ((_canvas.EditingMode == InkCanvasEditingMode.Ink) &&
                                        (Mouse.LeftButton == MouseButtonState.Pressed));

                if (inkingActive)
                {
                    _pendingShiftX += expandLeft;
                    _pendingShiftY += expandTop;
                }
                else
                {
                    ShiftCanvasContent(expandLeft, expandTop);
                }
            }
        }

        public void OnStrokeCollected(object? sender, InkCanvasStrokeCollectedEventArgs e)
        {
            FlushPendingShift();
        }

        public void FlushPendingShift()
        {
            if (_pendingShiftX == 0 && _pendingShiftY == 0) return;

            double dx = _pendingShiftX;
            double dy = _pendingShiftY;
            _pendingShiftX = _pendingShiftY = 0;

            ShiftCanvasContent(dx, dy);
        }

        private void ShiftCanvasContent(double dx, double dy)
        {
            if (dx == 0 && dy == 0) return;

            var m = Matrix.Identity;
            m.Translate(dx, dy);
            _canvas.Strokes.Transform(m, false);

            foreach (UIElement child in _canvas.Children)
            {
                double left = InkCanvas.GetLeft(child);
                double top = InkCanvas.GetTop(child);
                if (double.IsNaN(left)) left = 0;
                if (double.IsNaN(top)) top = 0;

                InkCanvas.SetLeft(child, left + dx);
                InkCanvas.SetTop(child, top + dy);
            }

            _viewport.UpdateLayout();
            _viewport.ScrollToHorizontalOffset(_viewport.HorizontalOffset + dx * _zoomPanService.Zoom);
            _viewport.ScrollToVerticalOffset(_viewport.VerticalOffset + dy * _zoomPanService.Zoom);
        }
    }
}
