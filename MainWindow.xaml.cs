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

namespace WindBoard
{
    public class BoardPage : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private int _number;
        private bool _isCurrent;
        private ImageSource? _preview;

        public int Number
        {
            get => _number;
            set { _number = value; OnPropertyChanged(); }
        }

        public bool IsCurrent
        {
            get => _isCurrent;
            set { _isCurrent = value; OnPropertyChanged(); }
        }

        public ImageSource? Preview
        {
            get => _preview;
            set { _preview = value; OnPropertyChanged(); }
        }

        // 页面内容
        public StrokeCollection Strokes { get; set; } = new StrokeCollection();

        // 每页画布大小
        public double CanvasWidth { get; set; } = 8000;
        public double CanvasHeight { get; set; } = 8000;

        // 每页视图状态（可选但体验更好）
        public double Zoom { get; set; } = 1.0;
        public double HorizontalOffset { get; set; } = 0.0;
        public double VerticalOffset { get; set; } = 0.0;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name ?? string.Empty));
    }

    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name ?? string.Empty));

        // 工具状态
        private InkCanvasEditingMode _lastEditingMode = InkCanvasEditingMode.Ink;
        private double _baseThickness = 3.0;

        // 分页相关
        public ObservableCollection<BoardPage> Pages { get; } = new ObservableCollection<BoardPage>();
        private int _currentPageIndex = 0;
        private bool _suppressStrokeEvents = false;
        private StrokeCollection? _attachedStrokes = null;

        public bool IsMultiPage => Pages.Count > 1;
        public string PageIndicatorText => $"{_currentPageIndex + 1} / {Pages.Count}";

        // 鼠标浮标与箭头的垂直偏移（屏幕像素）
        private double _eraserCursorOffsetY = 12.0;

        // XAML 命名元素缓存，避免编译器未生成字段导致的引用错误
        private Canvas? _eraserOverlay;
        private Border? _eraserCursorRect;
        private Popup? _popupPageManager;
        private bool _isEraserPressed = false;
        // 当前是否为鼠标擦除（用于决定浮标定位方式）
        private bool _isMouseErasing = false;

        // Zoom（视口缩放）
        private const double MinZoom = 0.5;
        private const double MaxZoom = 5.0;

        // 鼠标平移相关
        private bool _isPanning = false;
        private Point _lastMousePosition;

        // 左/上扩容需要“内容平移”的延迟量（避免正在书写时平移导致动态笔迹不同步）
        private double _pendingShiftX = 0;
        private double _pendingShiftY = 0;

        // 触摸点集合：DeviceId -> Point (Viewport坐标)
        private readonly Dictionary<int, Point> _activeTouches = new Dictionary<int, Point>();

        private bool _gestureActive = false;
        private int _gestureId1 = -1;
        private int _gestureId2 = -1;
        private Point _lastGestureP1;
        private Point _lastGestureP2;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;

            MyCanvas.StrokeCollected += MyCanvas_StrokeCollected;
            _eraserOverlay = (Canvas)FindName("EraserOverlay");
            _eraserCursorRect = (Border)FindName("EraserCursorRect");
            _popupPageManager = (Popup)FindName("PopupPageManager");

            // 即使 InkCanvas 将事件标记为 Handled，也要接收（擦除模式下很关键）
            MyCanvas.AddHandler(UIElement.MouseDownEvent, new MouseButtonEventHandler(MyCanvas_MouseDown), true);
            MyCanvas.AddHandler(UIElement.MouseMoveEvent, new MouseEventHandler(MyCanvas_MouseMove), true);
            MyCanvas.AddHandler(UIElement.MouseUpEvent, new MouseButtonEventHandler(MyCanvas_MouseUp), true);

            System.Diagnostics.Debug.WriteLine("[DEBUG] AddHandler MouseDown/Move/Up (handledEventsToo=true) 已注册");

            // 用于更新缩略图：监听 StrokesChanged（切页时会重新挂）
            AttachStrokeEvents();

            // Pages 数量变化时更新 UI 绑定属性
            Pages.CollectionChanged += (s, e) => NotifyPageUiChanged();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // 启动后把视口移动到大画布中心
            Dispatcher.InvokeAsync(() =>
            {
                // 先确保布局完成，否则 Viewport.ViewportWidth/Height 可能为 0
                Viewport.UpdateLayout();

                var extentW = MyCanvas.Width * _zoom;
                var extentH = MyCanvas.Height * _zoom;

                Viewport.ScrollToHorizontalOffset((extentW - Viewport.ViewportWidth) / 2.0);
                Viewport.ScrollToVerticalOffset((extentH - Viewport.ViewportHeight) / 2.0);

                UpdatePenThickness(_zoom);
                UpdateEraserVisual(null);

                // 初始化第一页（若未初始化）
                InitializePagesIfNeeded();
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }


        #region Auto Expansion

        /// 自动扩容补丁函数：当操作点接近画布边缘时，自动增加画布尺寸
        /// <param name="currentContentPosition">当前操作点在 MyCanvas 内部的坐标</param>
        private void AutoExpandCanvas(Point currentContentPosition)
        {
            // 阈值/步长（内容坐标）
            const double ExpansionThreshold = 1000.0;
            const double ExpansionStep = 2000.0;

            double expandLeft = 0, expandTop = 0, expandRight = 0, expandBottom = 0;

            if (currentContentPosition.X < ExpansionThreshold) expandLeft = ExpansionStep;
            if (currentContentPosition.Y < ExpansionThreshold) expandTop = ExpansionStep;

            if (currentContentPosition.X > MyCanvas.Width - ExpansionThreshold) expandRight = ExpansionStep;
            if (currentContentPosition.Y > MyCanvas.Height - ExpansionThreshold) expandBottom = ExpansionStep;

            if (expandLeft == 0 && expandTop == 0 && expandRight == 0 && expandBottom == 0)
                return;

            // 先扩尺寸：左/上扩容也需要先把 Width/Height 变大，否则平移后内容可能被推到边界外
            double newW = MyCanvas.Width + expandLeft + expandRight;
            double newH = MyCanvas.Height + expandTop + expandBottom;
            double newSize = Math.Max(newW, newH); // 保持正方形

            if (newSize > MyCanvas.Width || newSize > MyCanvas.Height)
            {
                MyCanvas.Width = newSize;
                MyCanvas.Height = newSize;

                // 同步更新当前页的画布尺寸，防止切页回来尺寸回滚
                if (Pages.Count > 0)
                {
                    Pages[_currentPageIndex].CanvasWidth = MyCanvas.Width;
                    Pages[_currentPageIndex].CanvasHeight = MyCanvas.Height;
                }
            }

            // 左/上扩容：需要把已有内容整体往右/下挪，才能“腾出”左/上空间
            if (expandLeft > 0 || expandTop > 0)
            {
                // 正在书写(Ink)时，立刻 Transform 可能导致“动态笔迹”和最终笔迹错位
                bool inkingActive = (MyCanvas.EditingMode == InkCanvasEditingMode.Ink) &&
                                    (Mouse.LeftButton == MouseButtonState.Pressed || _activeTouches.Count > 0);

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

        // 平移内容（笔迹 + 子元素 + 视口补偿）
        private void ShiftCanvasContent(double dx, double dy)
        {
            if (dx == 0 && dy == 0) return;

            // 平移笔迹
            var m = Matrix.Identity;
            m.Translate(dx, dy);
            MyCanvas.Strokes.Transform(m, false);

            // 平移 InkCanvas.Children（如果你未来往 InkCanvas 里加 UIElement，也能一起挪）
            foreach (UIElement child in MyCanvas.Children)
            {
                double left = InkCanvas.GetLeft(child);
                double top = InkCanvas.GetTop(child);
                if (double.IsNaN(left)) left = 0;
                if (double.IsNaN(top)) top = 0;

                InkCanvas.SetLeft(child, left + dx);
                InkCanvas.SetTop(child, top + dy);
            }

            // 关键：内容整体向右/下挪了，为了让用户“视野不跳”，ScrollViewer 的 offset 也要补偿
            Viewport.UpdateLayout();
            Viewport.ScrollToHorizontalOffset(Viewport.HorizontalOffset + dx * _zoom);
            Viewport.ScrollToVerticalOffset(Viewport.VerticalOffset + dy * _zoom);
        }

        private void MyCanvas_StrokeCollected(object sender, InkCanvasStrokeCollectedEventArgs e)
        {
            if (_pendingShiftX == 0 && _pendingShiftY == 0) return;

            double dx = _pendingShiftX;
            double dy = _pendingShiftY;
            _pendingShiftX = _pendingShiftY = 0;

            ShiftCanvasContent(dx, dy);
        }

        #endregion

        #region Tool UI

        private void RadioPen_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (RadioPen.IsChecked == true)
            {
                e.Handled = true;
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
                {
                    PopupPenSettings.IsOpen = true;
                }));
            }
        }

        private void RadioPen_Checked(object sender, RoutedEventArgs e)
        {
            MyCanvas.EditingMode = InkCanvasEditingMode.Ink;
            MyCanvas.UseCustomCursor = false; // 恢复默认光标行为
            MyCanvas.ClearValue(CursorProperty);
            if (_eraserOverlay != null)
                _eraserOverlay.Visibility = Visibility.Collapsed;
        }

        private void RadioEraser_Checked(object sender, RoutedEventArgs e)
        {
            MyCanvas.EditingMode = InkCanvasEditingMode.EraseByPoint;
            MyCanvas.UseCustomCursor = true; // 关键：禁用 InkCanvas 默认的橡皮擦光标（白色方块）

            // 橡皮擦模式：默认显示箭头光标，按下左键时显示自定义浮标
            MyCanvas.Cursor = Cursors.Arrow;
            _isEraserPressed = false;
            UpdateEraserVisual(null);
        }

        private void RadioSelect_Checked(object sender, RoutedEventArgs e)
        {
            MyCanvas.EditingMode = InkCanvasEditingMode.Select;
            MyCanvas.UseCustomCursor = false; // 恢复默认光标行为
            MyCanvas.ClearValue(CursorProperty);
            if (_eraserOverlay != null)
                _eraserOverlay.Visibility = Visibility.Collapsed;
        }

        private void Thickness_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && double.TryParse(rb.Tag?.ToString(), out double thickness))
            {
                _baseThickness = thickness;
                UpdatePenThickness(_zoom);
            }
        }

        private void Color_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string colorCode)
            {
                try
                {
                    Color color = (Color)ColorConverter.ConvertFromString(colorCode);
                    MyCanvas.DefaultDrawingAttributes.Color = color;
                }
                catch (FormatException)
                {
                    MyCanvas.DefaultDrawingAttributes.Color = Colors.White;
                }

                PopupPenSettings.IsOpen = false;
            }
        }

        // ===================== 分页相关逻辑 =====================

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

            // 轻量：当前页直接用当前画布的 Strokes，其他页用各自保存的 Strokes
            var strokes = (Pages.Count > 0 && page == Pages[_currentPageIndex]) ? MyCanvas.Strokes : page.Strokes;
            var dv = new DrawingVisual();

            using (var dc = dv.RenderOpen())
            {
                // 背景：和你白板更接近一点的深色
                dc.DrawRoundedRectangle(
                    new SolidColorBrush(Color.FromRgb(0x0F, 0x12, 0x16)),
                    null,
                    new Rect(0, 0, w, h),
                    10, 10);

                if (strokes != null && strokes.Count > 0)
                {
                    Rect bounds = strokes.GetBounds();

                    // 防止 0 宽高
                    double bw = Math.Max(bounds.Width, 1);
                    double bh = Math.Max(bounds.Height, 1);

                    double scale = Math.Min((w - 2 * padding) / bw, (h - 2 * padding) / bh);

                    // 将 strokes bounds 贴到 padding 内并居中
                    double targetW = bw * scale;
                    double targetH = bh * scale;

                    double tx = padding + (w - 2 * padding - targetW) / 2.0;
                    double ty = padding + (h - 2 * padding - targetH) / 2.0;

                    dc.PushTransform(new TranslateTransform(tx, ty));
                    dc.PushTransform(new ScaleTransform(scale, scale));
                    dc.PushTransform(new TranslateTransform(-bounds.X, -bounds.Y));

                    strokes.Draw(dc); // StrokeCollection.Draw
                    // 还原 transform
                    dc.Pop(); dc.Pop(); dc.Pop();
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

                // 继续保持 Popup 打开也可以；你想更像图里的行为也可以不关
                // _popupPageManager.IsOpen = true;
            }
        }

        #endregion
    }
}
