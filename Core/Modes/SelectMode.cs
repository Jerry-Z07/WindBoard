using System;
using System.Collections.Generic;
using System.Windows;
using WindBoard;
using WindBoard.Core.Input;
using WindBoard.Models.InkV2;
using WindBoard.Services.InkV2;

namespace WindBoard.Core.Modes
{
    public sealed class SelectMode : InteractionModeBase
    {
        private const double ClickHitRadiusScreenDip = 8.0;
        private const double MinMarqueeSizeDip = 2.0;

        private readonly Func<BoardPage?> _currentPageProvider;
        private readonly Func<double> _zoomProvider;
        private readonly Action<IReadOnlyList<InkStroke>> _setSelectedStrokes;
        private readonly Action<Rect?> _setMarqueeRect;

        private bool _marqueeActive;
        private Point _marqueeStart;

        public SelectMode(
            Func<BoardPage?> currentPageProvider,
            Func<double> zoomProvider,
            Action<IReadOnlyList<InkStroke>> setSelectedStrokes,
            Action<Rect?> setMarqueeRect)
        {
            _currentPageProvider = currentPageProvider;
            _zoomProvider = zoomProvider;
            _setSelectedStrokes = setSelectedStrokes;
            _setMarqueeRect = setMarqueeRect;
        }

        public override string Name => "Select";

        public override void SwitchOff()
        {
            _marqueeActive = false;
            _setMarqueeRect(null);
        }

        public override void OnPointerDown(InputEventArgs args)
        {
            if (args.DeviceType == InputDeviceType.Mouse && !args.LeftButton) return;

            BoardPage? page = _currentPageProvider();
            if (page == null) return;

            double zoom = _zoomProvider();
            if (zoom <= 0) zoom = 1.0;

            double radiusWorldDip = ClickHitRadiusScreenDip / zoom;
            InkPointHitTestResult? hit = page.InkSpatialIndex.HitTestPoint(args.CanvasPoint.X, args.CanvasPoint.Y, radiusWorldDip);
            if (hit.HasValue)
            {
                _marqueeActive = false;
                _setMarqueeRect(null);
                _setSelectedStrokes(new[] { hit.Value.Stroke });
                return;
            }

            _setSelectedStrokes(Array.Empty<InkStroke>());
            _marqueeStart = args.CanvasPoint;
            _marqueeActive = true;
            _setMarqueeRect(new Rect(_marqueeStart, _marqueeStart));
        }

        public override void OnPointerMove(InputEventArgs args)
        {
            if (!_marqueeActive) return;

            Rect rect = NormalizeRect(_marqueeStart, args.CanvasPoint);
            _setMarqueeRect(rect);
        }

        public override void OnPointerUp(InputEventArgs args)
        {
            if (!_marqueeActive) return;
            _marqueeActive = false;

            BoardPage? page = _currentPageProvider();
            if (page == null)
            {
                _setMarqueeRect(null);
                return;
            }

            Rect rect = NormalizeRect(_marqueeStart, args.CanvasPoint);
            _setMarqueeRect(null);

            if (rect.Width < MinMarqueeSizeDip && rect.Height < MinMarqueeSizeDip)
            {
                return;
            }

            var hits = page.InkSpatialIndex.QueryRect(new InkRectDip(rect.X, rect.Y, rect.Width, rect.Height));
            if (hits.Count == 0)
            {
                _setSelectedStrokes(Array.Empty<InkStroke>());
                return;
            }

            var selected = new HashSet<InkStroke>();
            for (int i = 0; i < hits.Count; i++)
            {
                selected.Add(hits[i].Stroke);
            }

            var ordered = new List<InkStroke>(selected.Count);
            for (int i = 0; i < page.Ink.Strokes.Count; i++)
            {
                InkStroke stroke = page.Ink.Strokes[i];
                if (selected.Contains(stroke))
                {
                    ordered.Add(stroke);
                }
            }

            _setSelectedStrokes(ordered);
        }

        private static Rect NormalizeRect(Point a, Point b)
        {
            double x = Math.Min(a.X, b.X);
            double y = Math.Min(a.Y, b.Y);
            double w = Math.Abs(b.X - a.X);
            double h = Math.Abs(b.Y - a.Y);
            return new Rect(x, y, w, h);
        }
    }
}
