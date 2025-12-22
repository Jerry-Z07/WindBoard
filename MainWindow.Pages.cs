using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Ink;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WindBoard.Services;
using System.Diagnostics;

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

            int added = 0, removed = 0, total = 0;
            try
            {
                added = e.Added?.Count ?? 0;
                removed = e.Removed?.Count ?? 0;
                total = MyCanvas.Strokes?.Count ?? 0;
            }
            catch { }

            var sw = Stopwatch.StartNew();


            // 轻量做法：只更新预览；真正保存 strokes 在切页/打开管理器时做 SaveCurrentPage()
            UpdatePagePreview(Pages[_currentPageIndex]);

            sw.Stop();

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
                Pages[i].IsCurrent = i == _currentPageIndex;
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

            // 允许“相对整页视图”最多放大多少倍（关键参数：防止小点被无限放大）
            const double MaxZoomInFactor = 30.0;

            // 当前页用实时笔迹，其它页用各自保存的笔迹
            var strokes = (Pages.Count > 0 && page == Pages[_currentPageIndex]) ? MyCanvas.Strokes : page.Strokes;

            var dv = new DrawingVisual();

            using (var dc = dv.RenderOpen())
            {
                // 背景
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

                        // bounds 太小会导致 scale 爆炸，这里先确保不为 0
                        double bw = Math.Max(bounds.Width, 1);
                        double bh = Math.Max(bounds.Height, 1);

                        // 1) “紧贴笔迹”的缩放
                        double scaleFitBounds = Math.Min(innerW / bw, innerH / bh);

                        // 2) “整页画布”的缩放（作为放大上限的基准）
                        double canvasW = Math.Max(page.CanvasWidth, 1);
                        double canvasH = Math.Max(page.CanvasHeight, 1);
                        double scaleFitCanvas = Math.Min(innerW / canvasW, innerH / canvasH);

                        // 关键：限制最大放大倍数，防止小点变巨圆
                        double scale = Math.Min(scaleFitBounds, scaleFitCanvas * MaxZoomInFactor);

                        // 居中显示 bounds
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
            // 弹窗的打开由 PageNavigatorControl 自身完成
        }

        // 兼容旧入口：从旧列表项按钮点击（现已不再使用）
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

        // 兼容旧入口：从旧删除按钮点击（现已不再使用）
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

        // —— 新控件事件桥接 ——
        private void PageNavigator_PageSelected(object sender, WindBoard.Controls.BoardPageEventArgs e)
        {
            int index = Pages.IndexOf(e.Page);
            if (index >= 0)
            {
                SwitchToPage(index);
            }
        }

        private void PageNavigator_PageDeleteRequested(object sender, WindBoard.Controls.BoardPageEventArgs e)
        {
            if (Pages.Count <= 1) return; // 至少保留一页

            int deleteIndex = Pages.IndexOf(e.Page);
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