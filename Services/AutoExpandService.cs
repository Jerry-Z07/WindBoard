using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using WindBoard;
using WindBoard.Models.Ink;

namespace WindBoard.Services
{
    public class AutoExpandService
    {
        private readonly InkCanvas _canvas;
        private readonly ZoomPanService _zoomPanService;
        private readonly Func<BoardPage?> _currentPageProvider;
        private readonly Func<bool>? _isInkingActiveProvider;
        private readonly Action<double, double>? _shiftContent;

        private double _pendingShiftX;
        private double _pendingShiftY;

        public AutoExpandService(
            InkCanvas canvas,
            ZoomPanService zoomPanService,
            Func<BoardPage?> currentPageProvider,
            Func<bool>? isInkingActiveProvider = null,
            Action<double, double>? shiftContent = null)
        {
            _canvas = canvas;
            _zoomPanService = zoomPanService;
            _currentPageProvider = currentPageProvider;
            _isInkingActiveProvider = isInkingActiveProvider;
            _shiftContent = shiftContent;
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

            if (_shiftContent != null)
            {
                _shiftContent(dx, dy);
            }
            else
            {
                var m = Matrix.Identity;
                m.Translate(dx, dy);
                _canvas.Strokes.Transform(m, false);

                // Attachments are hosted outside InkCanvas.Children; shift them via the page model.
                var page = _currentPageProvider();
                if (page != null)
                {
                    foreach (var att in page.Attachments)
                    {
                        att.X += dx;
                        att.Y += dy;
                    }

                    ShiftInkModelPoints(page.InkStrokes, dx, dy);
                }

                foreach (UIElement child in _canvas.Children)
                {
                    double left = InkCanvas.GetLeft(child);
                    double top = InkCanvas.GetTop(child);
                    if (double.IsNaN(left)) left = 0;
                    if (double.IsNaN(top)) top = 0;

                    InkCanvas.SetLeft(child, left + dx);
                    InkCanvas.SetTop(child, top + dy);
                }
            }

            // 内容整体右/下平移后，为了保持用户视野不跳动，需要将相机做反向补偿。
            _zoomPanService.SetPanDirect(
                _zoomPanService.PanX - dx * _zoomPanService.Zoom,
                _zoomPanService.PanY - dy * _zoomPanService.Zoom);
        }

        private static void ShiftInkModelPoints(List<InkStrokeModel> strokes, double dx, double dy)
        {
            if (strokes == null || strokes.Count == 0) return;

            for (int i = 0; i < strokes.Count; i++)
            {
                var stroke = strokes[i];
                if (stroke == null) continue;
                var pts = stroke.Points;
                for (int j = 0; j < pts.Count; j++)
                {
                    var p = pts[j];
                    pts[j] = p with { X = p.X + dx, Y = p.Y + dy };
                }
            }
        }
    }
}
