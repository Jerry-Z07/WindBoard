using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using WindBoard;

namespace WindBoard.Services
{
    public class PageService
    {
        private readonly InkCanvas _canvas;
        private readonly ZoomPanService _zoomPanService;
        private readonly Action? _onPageStateChanged;

        private StrokeCollection? _observedStrokes;
        private bool _suppressStrokeEvents;
        private int _currentPageIndex;
        private readonly PagePreviewRenderer _previewRenderer = new();

        public ObservableCollection<BoardPage> Pages { get; } = new ObservableCollection<BoardPage>();

        public PageService(InkCanvas canvas, ZoomPanService zoomPanService, Action? onPageStateChanged = null)
        {
            _canvas = canvas;
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
                PanX = _zoomPanService.PanX,
                PanY = _zoomPanService.PanY,
                Strokes = _canvas.Strokes
            };

            Pages.Add(p);
            _currentPageIndex = 0;
            MarkCurrentPage();
            _onPageStateChanged?.Invoke();
            AttachStrokeEvents();
        }

        public void SaveCurrentPage()
        {
            if (Pages.Count == 0) return;
            var cur = Pages[_currentPageIndex];

            cur.CanvasWidth = _canvas.Width;
            cur.CanvasHeight = _canvas.Height;
            cur.Zoom = _zoomPanService.Zoom;
            cur.PanX = _zoomPanService.PanX;
            cur.PanY = _zoomPanService.PanY;

            // 重要：不要 Clone 当前页笔迹。InkCanvas 与当前页共享同一个 StrokeCollection，
            // 否则在“页面管理/切页”时会产生常驻双份笔迹，导致内存暴涨且无法回落。
            cur.Strokes = _canvas.Strokes;
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
                PanX = 0,
                PanY = 0,
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
                EnsurePagePreview(p);
            }
        }

        public void EnsurePagePreview(BoardPage page)
        {
            UpdatePagePreview(page);
        }

        public void AttachStrokeEvents()
        {
            if (_observedStrokes != null)
            {
                _observedStrokes.StrokesChanged -= CurrentStrokes_StrokesChanged;
            }

            _observedStrokes = _canvas.Strokes;
            if (_observedStrokes != null)
            {
                _observedStrokes.StrokesChanged += CurrentStrokes_StrokesChanged;
            }
        }

        private void CurrentStrokes_StrokesChanged(object? sender, StrokeCollectionChangedEventArgs e)
        {
            if (_suppressStrokeEvents) return;
            if (Pages.Count == 0) return;

            // 仅做“内容变更”标记，不做渲染（缩略图在弹窗打开时按需生成）。
            Pages[_currentPageIndex].ContentVersion++;
        }

        private void LoadPageIntoCanvas(BoardPage page)
        {
            _suppressStrokeEvents = true;
            try
            {
                _canvas.Width = page.CanvasWidth;
                _canvas.Height = page.CanvasHeight;

                // 直接复用每页的 StrokeCollection（切页不克隆）
                _canvas.Strokes = page.Strokes ?? new StrokeCollection();
                AttachStrokeEvents();

                _zoomPanService.SetViewDirect(page.Zoom, page.PanX, page.PanY);
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
            // 预览仅在用户打开页面管理弹窗时需要（IndicatorClicked 会调用 RefreshAllPreviews）。
            // 避免在每次书写/擦除时渲染缩略图导致 O(N^2) 重绘与内存抖动。
            if (Pages.Count <= 1)
            {
                page.Preview = null;
                return;
            }

            if (page.Preview != null && page.PreviewVersion == page.ContentVersion)
            {
                return;
            }

            page.Preview = _previewRenderer.Render(
                page.Strokes,
                canvasWidth: page.CanvasWidth,
                canvasHeight: page.CanvasHeight,
                width: 220,
                height: 120,
                padding: 10,
                maxZoomInFactor: 30.0);
            page.PreviewVersion = page.ContentVersion;
        }
    }
}
