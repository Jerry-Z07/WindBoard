using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using WindBoard.Models.InkV2;
using WindBoard.Services.InkV2;

namespace WindBoard.Services
{
    public class AutoExpandService
    {
        private readonly FrameworkElement _canvas;
        private readonly ZoomPanService _zoomPanService;
        private readonly Func<BoardPage?> _currentPageProvider;
        private readonly Func<bool>? _isInkingActiveProvider;

        private double _pendingShiftX;
        private double _pendingShiftY;

        public AutoExpandService(
            FrameworkElement canvas,
            ZoomPanService zoomPanService,
            Func<BoardPage?> currentPageProvider,
            Func<bool>? isInkingActiveProvider = null)
        {
            _canvas = canvas;
            _zoomPanService = zoomPanService;
            _currentPageProvider = currentPageProvider;
            _isInkingActiveProvider = isInkingActiveProvider;
        }

        public void EnsureCanvasSpace(Point canvasPoint)
        {
            const double expansionThreshold = 1000.0;
            const double expansionStep = 2000.0;

            double expandLeft = 0;
            double expandTop = 0;
            double expandRight = 0;
            double expandBottom = 0;

            if (canvasPoint.X < expansionThreshold) expandLeft = expansionStep;
            if (canvasPoint.Y < expansionThreshold) expandTop = expansionStep;

            if (canvasPoint.X > _canvas.Width - expansionThreshold) expandRight = expansionStep;
            if (canvasPoint.Y > _canvas.Height - expansionThreshold) expandBottom = expansionStep;

            if (expandLeft == 0 && expandTop == 0 && expandRight == 0 && expandBottom == 0)
            {
                return;
            }

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
                bool inkingActive = _isInkingActiveProvider?.Invoke() ?? false;
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

        public void FlushPendingShift()
        {
            if (_pendingShiftX == 0 && _pendingShiftY == 0) return;

            double dx = _pendingShiftX;
            double dy = _pendingShiftY;
            _pendingShiftX = 0;
            _pendingShiftY = 0;

            ShiftCanvasContent(dx, dy);
        }

        private void ShiftCanvasContent(double dx, double dy)
        {
            if (dx == 0 && dy == 0) return;

            BoardPage? page = _currentPageProvider();
            if (page != null)
            {
                TranslateInk(page.Ink, dx, dy);
                TranslateAttachments(page.Attachments, dx, dy);
                page.InkSpatialIndex.Rebuild(page.Ink);
                page.ContentVersion++;
            }

            _zoomPanService.SetPanDirect(
                _zoomPanService.PanX - dx * _zoomPanService.Zoom,
                _zoomPanService.PanY - dy * _zoomPanService.Zoom);
        }

        private static void TranslateInk(InkDocument document, double dx, double dy)
        {
            if (document.Strokes.Count == 0) return;

            for (int si = 0; si < document.Strokes.Count; si++)
            {
                InkStroke stroke = document.Strokes[si];
                for (int fi = 0; fi < stroke.Fragments.Count; fi++)
                {
                    InkFragment fragment = stroke.Fragments[fi];
                    List<InkPoint> points = fragment.Points;
                    if (points.Count == 0) continue;

                    for (int pi = 0; pi < points.Count; pi++)
                    {
                        InkPoint p = points[pi];
                        points[pi] = new InkPoint(p.XDip + dx, p.YDip + dy, p.Pressure, p.TimestampTicks);
                    }

                    fragment.PointsVersion++;
                }
            }
        }

        private static void TranslateAttachments(System.Collections.ObjectModel.ObservableCollection<BoardAttachment> attachments, double dx, double dy)
        {
            if (attachments.Count == 0) return;

            for (int i = 0; i < attachments.Count; i++)
            {
                BoardAttachment att = attachments[i];
                att.X += dx;
                att.Y += dy;
            }
        }
    }
}
