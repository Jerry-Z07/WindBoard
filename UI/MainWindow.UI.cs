using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Controls.Primitives;
using System.Diagnostics;

namespace WindBoard
{

    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name ?? string.Empty));

        // 工具状态
        private InkCanvasEditingMode _lastEditingMode = InkCanvasEditingMode.Ink;
        private double _baseThickness = 3.0;



        // 鼠标浮标与箭头的垂直偏移（屏幕像素）
        private double _eraserCursorOffsetY = 12.0;

        // XAML 命名元素缓存，避免编译器未生成字段导致的引用错误
        private Canvas? _eraserOverlay;
        private Border? _eraserCursorRect;
        private Popup? _popupPageManager;
        private Popup? _popupEraserClear;
        private Popup? _popupMoreMenu;
        private Slider? _sliderClear;
        private bool _isEraserPressed = false;
        // 当前是否为鼠标擦除（用于决定浮标定位方式）
        private bool _isMouseErasing = false;

        // 清屏滑块触发标记（防止重复触发）
        private bool _clearSlideTriggered = false;
        // 清屏后延迟关闭标记（等待 TouchUp/MouseUp 再关闭弹窗，避免触摸卡顿）
        private bool _clearPendingClose = false;

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
            _popupEraserClear = (Popup)FindName("PopupEraserClear");
            _popupMoreMenu = (Popup)FindName("PopupMoreMenu");
            _sliderClear = (Slider)FindName("SliderClear");

            // 即使 InkCanvas 将事件标记为 Handled，也要接收（擦除模式下很关键）
            MyCanvas.AddHandler(MouseDownEvent, new MouseButtonEventHandler(MyCanvas_MouseDown), true);
            MyCanvas.AddHandler(MouseMoveEvent, new MouseEventHandler(MyCanvas_MouseMove), true);
            MyCanvas.AddHandler(MouseUpEvent, new MouseButtonEventHandler(MyCanvas_MouseUp), true);

            Debug.WriteLine("[DEBUG] AddHandler MouseDown/Move/Up (handledEventsToo=true) 已注册");

            // 用于更新缩略图：监听 StrokesChanged（切页时会重新挂）
            AttachStrokeEvents();

            // Pages 数量变化时更新 UI 绑定属性
            Pages.CollectionChanged += (s, e) => NotifyPageUiChanged();
            
            // 初始化默认页面，确保启动后 Pages 集合不为空
            InitializePagesIfNeeded();
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


            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }



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
            if (_popupEraserClear != null)
                _popupEraserClear.IsOpen = false;
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
            if (_popupEraserClear != null)
                _popupEraserClear.IsOpen = false;
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

        #endregion

        // 再次点击“擦除”按钮：弹出清屏滑块（鼠标）
        private void RadioEraser_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (RadioEraser.IsChecked == true)
            {
                e.Handled = true; // 阻止重复切换状态
                _clearSlideTriggered = false;
                _clearPendingClose = false;
                if (_sliderClear != null) _sliderClear.Value = 0;
                if (_popupEraserClear != null) _popupEraserClear.IsOpen = true;
            }
        }

        // 再次触摸“擦除”按钮：弹出清屏滑块（触摸）
        private void RadioEraser_PreviewTouchDown(object sender, TouchEventArgs e)
        {
            if (RadioEraser.IsChecked == true)
            {
                e.Handled = true; // 防止触摸事件向下提升为鼠标事件
                _clearSlideTriggered = false;
                _clearPendingClose = false;
                if (_sliderClear != null) _sliderClear.Value = 0;
                if (_popupEraserClear != null) _popupEraserClear.IsOpen = true;
            }
        }

        // 滑动以清屏：滑块拉到尽头触发清屏
        private void SliderClear_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_clearSlideTriggered) return;
            if (_popupEraserClear == null || !_popupEraserClear.IsOpen) return;

            var slider = sender as Slider ?? _sliderClear;
            double max = slider?.Maximum ?? 100;
            double val = slider?.Value ?? 0;

            if (val >= max)
            {
                _clearSlideTriggered = true;

                // 记录清屏耗时
                int strokesBefore = MyCanvas.Strokes.Count;
                int childrenBefore = MyCanvas.Children.Count;
                var sw = Stopwatch.StartNew();
                Debug.WriteLine($"[PERF] Clear start: strokes={strokesBefore}, children={childrenBefore}");

                // 清除当前画布笔迹与可能的子元素
                MyCanvas.Strokes.Clear();
                MyCanvas.Children.Clear();

                sw.Stop();
                Debug.WriteLine($"[PERF] Clear done: elapsed={sw.ElapsedMilliseconds}ms");

                // 标记待关闭，由 PointerUp 再真正关闭（避免触摸设备上因 capture/提升造成的卡死）
                _clearPendingClose = true;
            }
        }

        // 触摸抬起后再关闭弹窗与复位（防止触摸 capture 引发卡顿）
        private void SliderClear_PreviewTouchUp(object sender, TouchEventArgs e)
        {
            e.Handled = true;
            var slider = sender as Slider ?? _sliderClear;
            if (slider == null) return;

            bool reached = _clearPendingClose || (slider.Value >= slider.Maximum);
            if (!reached) return;

            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
            {
                try { this.ReleaseAllTouchCaptures(); } catch { }
                try { MyCanvas.ReleaseAllTouchCaptures(); } catch { }
                try { Mouse.Capture(null); } catch { }

                if (_popupEraserClear != null) _popupEraserClear.IsOpen = false;
                slider.Value = 0;
                _clearPendingClose = false;
                _clearSlideTriggered = false;

                // 清屏完成后切回书写模式
                if (RadioPen != null) RadioPen.IsChecked = true;
            }));
        }

        // 鼠标抬起后也作相同处理（保持一致性）
        private void SliderClear_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            var slider = sender as Slider ?? _sliderClear;
            if (slider == null) return;

            bool reached = _clearPendingClose || (slider.Value >= slider.Maximum);
            if (!reached) return;

            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
            {
                try { this.ReleaseAllTouchCaptures(); } catch { }
                try { MyCanvas.ReleaseAllTouchCaptures(); } catch { }
                try { Mouse.Capture(null); } catch { }

                if (_popupEraserClear != null) _popupEraserClear.IsOpen = false;
                slider.Value = 0;
                _clearPendingClose = false;
                _clearSlideTriggered = false;

                // 清屏完成后切回书写模式
                if (RadioPen != null) RadioPen.IsChecked = true;
            }));
        }

        // 弹窗被关闭时，清理残留的捕获并复位标记
        private void PopupEraserClear_Closed(object sender, EventArgs e)
        {
            try { this.ReleaseAllTouchCaptures(); } catch { }
            try { MyCanvas.ReleaseAllTouchCaptures(); } catch { }
            try { Mouse.Capture(null); } catch { }
            _clearPendingClose = false;
        }

        #region System Dock UI（左下角 更多/最小化）
        private void BtnMore_Click(object sender, RoutedEventArgs e)
        {
            // 打开“更多”弹出菜单
            if (_popupMoreMenu == null)
                _popupMoreMenu = (Popup)FindName("PopupMoreMenu");
            if (_popupMoreMenu != null)
            {
                _popupMoreMenu.IsOpen = true;
            }
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            // 最小化窗口
            this.WindowState = WindowState.Minimized;
        }

        private void MenuSettings_Click(object sender, RoutedEventArgs e)
        {
            // 关闭菜单
            if (_popupMoreMenu != null) _popupMoreMenu.IsOpen = false;
            
            // 打开设置窗口
            var settingsWindow = new UI.SettingsWindow(this);
            settingsWindow.Owner = this;
            settingsWindow.ShowDialog();
        }

        public void SetBackgroundColor(Color color)
        {
            MyCanvas.Background = new SolidColorBrush(color);
        }

        private void MenuExit_Click(object sender, RoutedEventArgs e)
        {
            // 退出应用
            Application.Current.Shutdown();
        }
        #endregion
    }
}
