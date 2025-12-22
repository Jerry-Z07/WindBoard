using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Controls.Primitives;
using WindBoard.Services;

namespace WindBoard
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        // 分页相关
        public ObservableCollection<BoardPage> Pages { get; } = new ObservableCollection<BoardPage>();
        private int _currentPageIndex = 0;
        private bool _suppressStrokeEvents = false;
        private StrokeCollection? _attachedStrokes = null;

        public bool IsMultiPage => Pages.Count > 1;
        public string PageIndicatorText => $"{_currentPageIndex + 1} / {Pages.Count}";

        // 页面缩略图渲染服务
        private readonly PagePreviewRenderer _previewRenderer = new PagePreviewRenderer();

        private void InitializePagesIfNeeded()
        {
            if (Pages.Count > 0) return;

            var p = new BoardPage
            {
                Number = 1,
                CanvasWidth = MyCanvas.Width,
                CanvasHeight = MyCanvas.Height,
                Zoom = _zoom,
                HorizontalOffset = Viewport.HorizontalOffset,
                VerticalOffset = Viewport.VerticalOffset,
                Strokes = MyCanvas.Strokes.Clone()
            };

            Pages.Add(p);
            _currentPageIndex = 0;
            MarkCurrentPage();

            UpdatePagePreview(p);
            NotifyPageUiChanged();
        }

        private void AttachStrokeEvents()
        {
            if (_attachedStrokes != null)
                _attachedStrokes.StrokesChanged -= CurrentStrokes_StrokesChanged;

            _attachedStrokes = MyCanvas?.Strokes;
            if (_attachedStrokes != null)
                _attachedStrokes.StrokesChanged += CurrentStrokes_StrokesChanged;
        }

        private void CurrentStrokes_StrokesChanged(object sender, StrokeCollectionChangedEventArgs e)
        {
            if (_suppressStrokeEvents) return;
            if (Pages.Count == 0) return;

            // 轻量做法：只更新预览；真正保存 strokes 在切页/打开管理器时做 SaveCurrentPage()
            UpdatePagePreview(Pages[_currentPageIndex]);
        }

        private void NotifyPageUiChanged()
        {
            OnPropertyChanged(nameof(IsMultiPage));
            OnPropertyChanged(nameof(PageIndicatorText));
        }

        private void MarkCurrentPage()
        {
            for (int i = 0; i < Pages.Count; i++)
            {
                Pages[i].IsCurrent = (i == _currentPageIndex);
            }
        }

        private void SaveCurrentPage()
        {
            if (Pages.Count == 0) return;
            var cur = Pages[_currentPageIndex];

            _suppressStrokeEvents = true;
            try
            {
                cur.CanvasWidth = MyCanvas.Width;
                cur.CanvasHeight = MyCanvas.Height;
                cur.Zoom = _zoom;
                cur.HorizontalOffset = Viewport.HorizontalOffset;
                cur.VerticalOffset = Viewport.VerticalOffset;
                cur.Strokes = MyCanvas.Strokes.Clone();
                UpdatePagePreview(cur);
            }
            finally
            {
                _suppressStrokeEvents = false;
            }
        }

        private void SwitchToPage(int newIndex)
        {
            if (newIndex < 0 || newIndex >= Pages.Count) return;
            if (newIndex == _currentPageIndex) return;

            SaveCurrentPage();

            _currentPageIndex = newIndex;
            LoadPageIntoCanvas(Pages[_currentPageIndex]);

            MarkCurrentPage();
            NotifyPageUiChanged();
        }

        private void LoadPageIntoCanvas(BoardPage page)
        {
            _suppressStrokeEvents = true;
            try
            {
                // 停掉橡皮擦浮标，避免切页残留
                _isEraserPressed = false;
                _isMouseErasing = false;
                UpdateEraserVisual(null);

                // 画布大小
                MyCanvas.Width = page.CanvasWidth;
                MyCanvas.Height = page.CanvasHeight;

                // 笔迹
                MyCanvas.Strokes = page.Strokes?.Clone() ?? new StrokeCollection();

                // 重新挂 strokes 监听（因为 Strokes 换了对象）
                AttachStrokeEvents();

                // 恢复 zoom + offset
                SetZoomDirect(page.Zoom);

                Viewport.UpdateLayout();
                Viewport.ScrollToHorizontalOffset(page.HorizontalOffset);
                Viewport.ScrollToVerticalOffset(page.VerticalOffset);

                UpdatePenThickness(_zoom);
                UpdateEraserVisual(null);
            }
            finally
            {
                _suppressStrokeEvents = false;
            }
        }

        private void SetZoomDirect(double newZoom)
        {
            _zoom = Clamp(newZoom, MinZoom, MaxZoom);
            ZoomTransform.ScaleX = _zoom;
            ZoomTransform.ScaleY = _zoom;
            Viewport.UpdateLayout();
        }

        private void RenumberPages()
        {
            for (int i = 0; i < Pages.Count; i++)
            {
                Pages[i].Number = i + 1;
            }
            NotifyPageUiChanged();
        }

        private void UpdatePagePreview(BoardPage page)
        {
            const int w = 220;
            const int h = 120;
            const double padding = 10;

            // 当前页用实时笔迹，其它页用各自保存的笔迹
            var strokes = (Pages.Count > 0 && page == Pages[_currentPageIndex]) ? MyCanvas.Strokes : page.Strokes;

            var preview = _previewRenderer.Render(strokes, w, h, padding);
            page.Preview = preview;
        }

        // —— 右下角分页条按钮 ——
        private void BtnPrevPage_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPageIndex <= 0) return;
            SwitchToPage(_currentPageIndex - 1);
        }

        private void BtnNextPage_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPageIndex >= Pages.Count - 1) return;
            SwitchToPage(_currentPageIndex + 1);
        }

        private void BtnAddPage_Click(object sender, RoutedEventArgs e)
        {
            SaveCurrentPage();

            var newPage = new BoardPage
            {
                Number = Pages.Count + 1,
                CanvasWidth = 8000,
                CanvasHeight = 8000,
                Zoom = _zoom, // 也可固定 1.0
                HorizontalOffset = 0,
                VerticalOffset = 0,
                Strokes = new StrokeCollection()
            };

            Pages.Add(newPage);
            RenumberPages();
            SwitchToPage(Pages.Count - 1);
        }

        private void BtnPageIndicator_Click(object sender, RoutedEventArgs e)
        {
            if (!IsMultiPage) return;

            // 打开前刷新所有缩略图（确保是最新的）
            SaveCurrentPage();
            foreach (var p in Pages) UpdatePagePreview(p);

            if (_popupPageManager != null) _popupPageManager.IsOpen = true;
        }

        // 页面列表项点击（切换到该页）
        private void PageItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is BoardPage page)
            {
                int index = Pages.IndexOf(page);
                if (index >= 0)
                {
                    if (_popupPageManager != null) _popupPageManager.IsOpen = false;
                    SwitchToPage(index);
                }
            }
        }

        // 删除指定页（保留至少一页）
        private void DeletePage_Click(object sender, RoutedEventArgs e)
        {
            if (Pages.Count <= 1) return; // 至少保留一页

            if (sender is FrameworkElement fe && fe.Tag is BoardPage page)
            {
                int deleteIndex = Pages.IndexOf(page);
                if (deleteIndex < 0) return;

                // 删除前先保存当前页（避免丢）
                SaveCurrentPage();

                Pages.RemoveAt(deleteIndex);
                RenumberPages();

                // 调整当前页索引
                if (_currentPageIndex >= Pages.Count) _currentPageIndex = Pages.Count - 1;
                if (_currentPageIndex < 0) _currentPageIndex = 0;

                LoadPageIntoCanvas(Pages[_currentPageIndex]);
                MarkCurrentPage();
                NotifyPageUiChanged();

            }
        }
    }
}