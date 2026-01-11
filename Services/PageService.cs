using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using WindBoard;

namespace WindBoard.Services
{
    public class PageService
    {
        private readonly FrameworkElement _canvas;
        private readonly ZoomPanService _zoomPanService;
        private readonly Action? _onPageStateChanged;

        private int _currentPageIndex;
        private readonly PagePreviewRenderer _previewRenderer = new();

        public ObservableCollection<BoardPage> Pages { get; } = new ObservableCollection<BoardPage>();

        public PageService(FrameworkElement canvas, ZoomPanService zoomPanService, Action? onPageStateChanged = null)
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
                PanY = _zoomPanService.PanY
            };

            Pages.Add(p);
            _currentPageIndex = 0;
            MarkCurrentPage();
            _onPageStateChanged?.Invoke();
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

            double canvasSize = Math.Max(_canvas.Width, _canvas.Height);
            if (canvasSize > 0 && !double.IsNaN(canvasSize) && !double.IsInfinity(canvasSize))
            {
                canvasSize = Math.Round(canvasSize);
            }
            else
            {
                canvasSize = _canvas.Width;
            }

            var newPage = new BoardPage
            {
                Number = Pages.Count + 1,
                CanvasWidth = canvasSize,
                CanvasHeight = canvasSize,
                Zoom = _zoomPanService.Zoom,
                PanX = 0,
                PanY = 0
            };

            Pages.Add(newPage);
            RenumberPages();

            _currentPageIndex = Pages.Count - 1;
            LoadPageIntoCanvas(Pages[_currentPageIndex]);
            MarkCurrentPage();
            _onPageStateChanged?.Invoke();
        }

        public void ReplaceAllPages(IList<BoardPage> newPages, int currentIndex = 0)
        {
            if (newPages == null) throw new ArgumentNullException(nameof(newPages));

            Pages.Clear();
            for (int i = 0; i < newPages.Count; i++)
            {
                Pages.Add(newPages[i]);
            }

            RenumberPages();

            if (Pages.Count == 0)
            {
                _currentPageIndex = 0;
                _onPageStateChanged?.Invoke();
                return;
            }

            _currentPageIndex = Math.Clamp(currentIndex, 0, Pages.Count - 1);
            LoadPageIntoCanvas(Pages[_currentPageIndex]);

            MarkCurrentPage();
            _onPageStateChanged?.Invoke();
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

        private void LoadPageIntoCanvas(BoardPage page)
        {
            _canvas.Width = page.CanvasWidth;
            _canvas.Height = page.CanvasHeight;
            _zoomPanService.SetViewDirect(page.Zoom, page.PanX, page.PanY);
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
                page,
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
