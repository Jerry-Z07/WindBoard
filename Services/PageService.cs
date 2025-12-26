using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WindBoard;

namespace WindBoard.Services
{
    public class PageService
    {
        private readonly InkCanvas _canvas;
        private readonly ScrollViewer _viewport;
        private readonly ZoomPanService _zoomPanService;
        private readonly Action? _onPageStateChanged;

        private StrokeCollection? _attachedStrokes;
        private bool _suppressStrokeEvents;
        private int _currentPageIndex;

        public ObservableCollection<BoardPage> Pages { get; } = new ObservableCollection<BoardPage>();

        public PageService(InkCanvas canvas, ScrollViewer viewport, ZoomPanService zoomPanService, Action? onPageStateChanged = null)
        {
            _canvas = canvas;
            _viewport = viewport;
            _zoomPanService = zoomPanService;
            _onPageStateChanged = onPageStateChanged;

            Pages.CollectionChanged += (_, __) => _onPageStateChanged?.Invoke();
        }

        public bool IsMultiPage => Pages.Count > 1;
        public string PageIndicatorText => $"{_currentPageIndex + 1} / {Pages.Count}";
        public BoardPage? CurrentPage => (_currentPageIndex >= 0 && _currentPageIndex < Pages.Count) ? Pages[_currentPageIndex] : null;
        public int CurrentPageIndex => _currentPageIndex;

        public void InitializePagesIfNeeded()
        {
            if (Pages.Count > 0) return;

            var p = new BoardPage
            {
                Number = 1,
                CanvasWidth = _canvas.Width,
                CanvasHeight = _canvas.Height,
                Zoom = _zoomPanService.Zoom,
                HorizontalOffset = _viewport.HorizontalOffset,
                VerticalOffset = _viewport.VerticalOffset,
                Strokes = _canvas.Strokes.Clone()
            };

            Pages.Add(p);
            _currentPageIndex = 0;
            MarkCurrentPage();
            UpdatePagePreview(p);
            _onPageStateChanged?.Invoke();
            AttachStrokeEvents();
        }

        public void SaveCurrentPage()
        {
            if (Pages.Count == 0) return;
            var cur = Pages[_currentPageIndex];

            _suppressStrokeEvents = true;
            try
            {
                cur.CanvasWidth = _canvas.Width;
                cur.CanvasHeight = _canvas.Height;
                cur.Zoom = _zoomPanService.Zoom;
                cur.HorizontalOffset = _viewport.HorizontalOffset;
                cur.VerticalOffset = _viewport.VerticalOffset;
                cur.Strokes = _canvas.Strokes.Clone();
                UpdatePagePreview(cur);
            }
            finally
            {
                _suppressStrokeEvents = false;
            }
        }

        public void SwitchToPage(int newIndex)
        {
            if (newIndex < 0 || newIndex >= Pages.Count) return;
            if (newIndex == _currentPageIndex) return;

            SaveCurrentPage();

            _currentPageIndex = newIndex;
            LoadPageIntoCanvas(Pages[_currentPageIndex]);

            MarkCurrentPage();
            _onPageStateChanged?.Invoke();
        }

        public void AddPage()
        {
            SaveCurrentPage();

            var newPage = new BoardPage
            {
                Number = Pages.Count + 1,
                CanvasWidth = 8000,
                CanvasHeight = 8000,
                Zoom = _zoomPanService.Zoom,
                HorizontalOffset = 0,
                VerticalOffset = 0,
                Strokes = new StrokeCollection()
            };

            Pages.Add(newPage);
            RenumberPages();
            SwitchToPage(Pages.Count - 1);
        }

        public void DeletePage(BoardPage page)
        {
            if (Pages.Count <= 1) return;

            int deleteIndex = Pages.IndexOf(page);
            if (deleteIndex < 0) return;

            SaveCurrentPage();

            Pages.RemoveAt(deleteIndex);
            RenumberPages();

            if (_currentPageIndex >= Pages.Count) _currentPageIndex = Pages.Count - 1;
            if (_currentPageIndex < 0) _currentPageIndex = 0;

            LoadPageIntoCanvas(Pages[_currentPageIndex]);
            MarkCurrentPage();
            _onPageStateChanged?.Invoke();
        }

        public void RefreshAllPreviews()
        {
            SaveCurrentPage();
            foreach (var p in Pages)
            {
                UpdatePagePreview(p);
            }
        }

        public void AttachStrokeEvents()
        {
            if (_attachedStrokes != null)
            {
                _attachedStrokes.StrokesChanged -= CurrentStrokes_StrokesChanged;
            }

            _attachedStrokes = _canvas?.Strokes;
            if (_attachedStrokes != null)
            {
                _attachedStrokes.StrokesChanged += CurrentStrokes_StrokesChanged;
            }
        }

        private void CurrentStrokes_StrokesChanged(object sender, StrokeCollectionChangedEventArgs e)
        {
            if (_suppressStrokeEvents) return;
            if (Pages.Count == 0) return;

            int added = 0, removed = 0, total = 0;
            try
            {
                added = e.Added?.Count ?? 0;
                removed = e.Removed?.Count ?? 0;
                total = _canvas.Strokes?.Count ?? 0;
            }
            catch { }

            var sw = Stopwatch.StartNew();

            UpdatePagePreview(Pages[_currentPageIndex]);
            sw.Stop();
        }

        private void LoadPageIntoCanvas(BoardPage page)
        {
            _suppressStrokeEvents = true;
            try
            {
                _canvas.Width = page.CanvasWidth;
                _canvas.Height = page.CanvasHeight;

                _canvas.Strokes = page.Strokes?.Clone() ?? new StrokeCollection();
                AttachStrokeEvents();

                _zoomPanService.SetZoomDirect(page.Zoom);

                _viewport.UpdateLayout();
                _viewport.ScrollToHorizontalOffset(page.HorizontalOffset);
                _viewport.ScrollToVerticalOffset(page.VerticalOffset);
            }
            finally
            {
                _suppressStrokeEvents = false;
            }
        }

        private void MarkCurrentPage()
        {
            for (int i = 0; i < Pages.Count; i++)
            {
                Pages[i].IsCurrent = i == _currentPageIndex;
            }
        }

        private void RenumberPages()
        {
            for (int i = 0; i < Pages.Count; i++)
            {
                Pages[i].Number = i + 1;
            }
            _onPageStateChanged?.Invoke();
        }

        private void UpdatePagePreview(BoardPage page)
        {
            const int w = 220;
            const int h = 120;
            const double padding = 10;
            const double MaxZoomInFactor = 30.0;

            var strokes = (Pages.Count > 0 && page == Pages[_currentPageIndex]) ? _canvas.Strokes : page.Strokes;

            var dv = new DrawingVisual();

            using (var dc = dv.RenderOpen())
            {
                dc.DrawRoundedRectangle(
                    new SolidColorBrush(Color.FromRgb(0x0F, 0x12, 0x16)),
                    null,
                    new Rect(0, 0, w, h),
                    10, 10);

                if (strokes != null && strokes.Count > 0)
                {
                    Rect bounds = strokes.GetBounds();
                    if (!bounds.IsEmpty)
                    {
                        double innerW = w - 2 * padding;
                        double innerH = h - 2 * padding;

                        double bw = Math.Max(bounds.Width, 1);
                        double bh = Math.Max(bounds.Height, 1);

                        double scaleFitBounds = Math.Min(innerW / bw, innerH / bh);

                        double canvasW = Math.Max(page.CanvasWidth, 1);
                        double canvasH = Math.Max(page.CanvasHeight, 1);
                        double scaleFitCanvas = Math.Min(innerW / canvasW, innerH / canvasH);

                        double scale = Math.Min(scaleFitBounds, scaleFitCanvas * MaxZoomInFactor);

                        double targetW = bw * scale;
                        double targetH = bh * scale;

                        double tx = padding + (innerW - targetW) / 2.0;
                        double ty = padding + (innerH - targetH) / 2.0;

                        dc.PushTransform(new TranslateTransform(tx, ty));
                        dc.PushTransform(new ScaleTransform(scale, scale));
                        dc.PushTransform(new TranslateTransform(-bounds.X, -bounds.Y));

                        strokes.Draw(dc);

                        dc.Pop(); dc.Pop(); dc.Pop();
                    }
                }
            }

            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            page.Preview = rtb;
        }
    }
}
