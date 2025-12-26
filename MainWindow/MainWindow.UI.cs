using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Controls.Primitives;
using System.Diagnostics;
using System.Linq;
using System.Windows.Media.Animation;
using System.Globalization;

namespace WindBoard
{

    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name ?? string.Empty));

        private Popup? _popupPenSettings;
        private Popup? _popupPageManager;
        private Popup? _popupEraserClear;
        private Popup? _popupMoreMenu;
        private Slider? _sliderClear;
        private TextBlock? _textClearHint;
        private Grid? _rootGrid;
        // 清屏滑块触发标记（防止重复触发）
        private bool _clearSlideTriggered = false;
        // 清屏后延迟关闭标记（等待 TouchUp/MouseUp 再关闭弹窗，避免触摸卡顿）
        private bool _clearPendingClose = false;

        private bool _isVideoPresenterEnabled;
        public bool IsVideoPresenterEnabled
        {
            get => _isVideoPresenterEnabled;
            set
            {
                if (_isVideoPresenterEnabled != value)
                {
                    _isVideoPresenterEnabled = value;
                    OnPropertyChanged();
                }
            }
        }

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;

            // 加载并应用设置
            SettingsService.Instance.Load();
            SetBackgroundColor(SettingsService.Instance.GetBackgroundColor());
            IsVideoPresenterEnabled = SettingsService.Instance.GetVideoPresenterEnabled();
            // 监听设置变更
            SettingsService.Instance.SettingsChanged += (s, e) =>
            {
                SetBackgroundColor(SettingsService.Instance.GetBackgroundColor());
                IsVideoPresenterEnabled = SettingsService.Instance.GetVideoPresenterEnabled();
            };

            _popupPenSettings = (Popup)FindName("PopupPenSettings");
            _popupPageManager = (Popup)FindName("PopupPageManager");
            _popupEraserClear = (Popup)FindName("PopupEraserClear");
            _popupMoreMenu = (Popup)FindName("PopupMoreMenu");
            _sliderClear = (Slider)FindName("SliderClear");
            _textClearHint = (TextBlock)FindName("TextClearHint");

            _rootGrid = (Grid)FindName("RootGrid");
            if (_rootGrid != null)
            {
                _rootGrid.AddHandler(PreviewMouseDownEvent, new MouseButtonEventHandler(Root_PreviewMouseDown), true);
                _rootGrid.AddHandler(PreviewTouchDownEvent, new EventHandler<TouchEventArgs>(Root_PreviewTouchDown), true);
            }
            PreviewKeyDown += Window_PreviewKeyDown;

            InitializeArchitecture();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (_zoomPanService == null) return;
            // 启动后把视口移动到大画布中心
            Dispatcher.InvokeAsync(() =>
            {
                // 先确保布局完成，否则 Viewport.ViewportWidth/Height 可能为 0
                Viewport.UpdateLayout();

                var extentW = MyCanvas.Width * _zoomPanService.Zoom;
                var extentH = MyCanvas.Height * _zoomPanService.Zoom;

                Viewport.ScrollToHorizontalOffset((extentW - Viewport.ViewportWidth) / 2.0);
                Viewport.ScrollToVerticalOffset((extentH - Viewport.ViewportHeight) / 2.0);

                _strokeService.UpdatePenThickness(_zoomPanService.Zoom);


            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }




        #region Tool UI

        private void RadioPen_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (RadioPen.IsChecked == true)
            {
                e.Handled = true;
                // Toggle 行为：已打开则关闭，否则打开
                if (PopupPenSettings.IsOpen)
                {
                    PopupPenSettings.IsOpen = false;
                    return;
                }
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
                {
                    PopupPenSettings.IsOpen = true;
                }));
            }
        }

        private void RadioPen_Checked(object sender, RoutedEventArgs e)
        {
            if (_modeController == null || _inkMode == null) return;
            _modeController.SetCurrentMode(_inkMode);
            if (_popupEraserClear != null)
                _popupEraserClear.IsOpen = false;
        }

        private void RadioEraser_Checked(object sender, RoutedEventArgs e)
        {
            if (_modeController == null || _eraserMode == null) return;
            _modeController.SetCurrentMode(_eraserMode);
        }

        private void RadioSelect_Checked(object sender, RoutedEventArgs e)
        {
            if (_modeController == null || _selectMode == null) return;
            _modeController.SetCurrentMode(_selectMode);
            if (_popupEraserClear != null)
                _popupEraserClear.IsOpen = false;
        }

        private void Thickness_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && double.TryParse(rb.Tag?.ToString(), out double thickness))
            {
                if (_strokeService == null) return;
                _baseThickness = thickness;
                _strokeService.SetBaseThickness(thickness, _zoomPanService.Zoom);
            }
        }

        private void Color_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string colorCode)
            {
                if (_strokeService == null) return;
                try
                {
                    Color color = (Color)ColorConverter.ConvertFromString(colorCode);
                    _strokeService.SetColor(color);
                }
                catch (FormatException)
                {
                    _strokeService.SetColor(Colors.White);
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

                // Toggle 行为：已打开则关闭，否则打开
                if (_popupEraserClear != null && _popupEraserClear.IsOpen)
                {
                    _popupEraserClear.IsOpen = false;
                    return;
                }
                if (_popupEraserClear != null) _popupEraserClear.IsOpen = true;
            }
        }

        // 再次触摸“擦除”按钮：单击即可弹出清屏滑块（触摸）
        private void RadioEraser_PreviewTouchDown(object sender, TouchEventArgs e)
        {
            // 仅用于阻止事件提升，不在 TouchDown 做逻辑，避免必须“长按”
            e.Handled = true;
        }

        private void RadioEraser_PreviewTouchUp(object sender, TouchEventArgs e)
        {
            if (RadioEraser.IsChecked == true)
            {
                e.Handled = true; // 防止触摸事件向下提升为鼠标事件
                _clearSlideTriggered = false;
                _clearPendingClose = false;
                if (_sliderClear != null) _sliderClear.Value = 0;

                // Toggle 行为：已打开则关闭，否则打开（与鼠标一致）
                if (_popupEraserClear != null && _popupEraserClear.IsOpen)
                {
                    _popupEraserClear.IsOpen = false;
                    return;
                }
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
            if (max <= 0) return;

            // 读取阈值（优先从 Tag，默认 0.96），限制在 [0.5, 0.999]
            double threshold = 0.96;
            try
            {
                if (slider != null)
                {
                    if (slider.Tag is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var t)) threshold = t;
                    else if (slider.Tag is double td) threshold = td;
                }
            }
            catch { }
            threshold = Math.Min(0.999, Math.Max(0.5, threshold));

            double ratio = val / max;

            if (ratio >= threshold)
            {
                _clearSlideTriggered = true;

                if (_textClearHint != null)
                {
                    _textClearHint.Text = "松开清空";
                }

                int strokesBefore = MyCanvas.Strokes.Count;
                int childrenBefore = MyCanvas.Children.Count;

                MyCanvas.Strokes.Clear();
                MyCanvas.Children.Clear();

                _clearPendingClose = true;
            }
            else if (_textClearHint != null && _textClearHint.Text == "松开清空")
            {
                _textClearHint.Text = "向右滑动清空";
            }
        }

        // 触摸抬起后再关闭弹窗与复位（防止触摸 capture 引发卡顿）
        private void SliderClear_PreviewTouchUp(object sender, TouchEventArgs e)
        {
            e.Handled = true;
            var slider = sender as Slider ?? _sliderClear;
            if (slider == null) return;

            // 读取阈值（优先从 Tag，默认 0.96）
            double threshold = 0.96;
            try
            {
                if (slider.Tag is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var t)) threshold = t;
                else if (slider.Tag is double td) threshold = td;
            }
            catch { }
            threshold = Math.Min(0.999, Math.Max(0.5, threshold));

            bool reached = _clearPendingClose || (slider.Maximum > 0 && (slider.Value / slider.Maximum) >= threshold);
            if (!reached)
            {
                // 未达阈值：回弹但不关闭弹窗
                try
                {
                    var anim = new DoubleAnimation
                    {
                        To = 0,
                        Duration = TimeSpan.FromMilliseconds(180),
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };
                    slider.BeginAnimation(RangeBase.ValueProperty, anim);
                }
                catch { slider.Value = 0; }

                _clearPendingClose = false;
                _clearSlideTriggered = false;

                if (_textClearHint != null)
                {
                    _textClearHint.Text = "向右滑动清空";
                }

                return;
            }

            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
            {
                try { ReleaseAllTouchCaptures(); } catch { }
                try { MyCanvas.ReleaseAllTouchCaptures(); } catch { }
                try { Mouse.Capture(null); } catch { }

                if (_popupEraserClear != null) _popupEraserClear.IsOpen = false;
                try { slider.BeginAnimation(RangeBase.ValueProperty, null); } catch { }
                slider.Value = 0;
                _clearPendingClose = false;
                _clearSlideTriggered = false;

                if (_textClearHint != null)
                {
                    _textClearHint.Text = "向右滑动清空";
                }

                if (RadioPen != null) RadioPen.IsChecked = true;
            }));
        }

        // 鼠标抬起后也作相同处理（保持一致性）
        private void SliderClear_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            var slider = sender as Slider ?? _sliderClear;
            if (slider == null) return;

            // 读取阈值（优先从 Tag，默认 0.96）
            double threshold = 0.96;
            try
            {
                if (slider.Tag is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var t)) threshold = t;
                else if (slider.Tag is double td) threshold = td;
            }
            catch { }
            threshold = Math.Min(0.999, Math.Max(0.5, threshold));

            Debug.WriteLine($"[DBG] SliderClear_PreviewMouseUp: val={slider.Value:F2}, max={slider.Maximum:F2}, threshold={threshold:F3}, pending={_clearPendingClose}");

            bool reached = _clearPendingClose || (slider.Maximum > 0 && (slider.Value / slider.Maximum) >= threshold);
            if (!reached)
            {
                Debug.WriteLine("[DBG] SliderClear_PreviewMouseUp: not reached, animate back to 0");
                // 未达阈值：回弹但不关闭弹窗
                try
                {
                    var anim = new DoubleAnimation
                    {
                        To = 0,
                        Duration = TimeSpan.FromMilliseconds(180),
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };
                    slider.BeginAnimation(RangeBase.ValueProperty, anim);
                }
                catch { slider.Value = 0; }

                _clearPendingClose = false;
                _clearSlideTriggered = false;

                if (_textClearHint != null)
                {
                    _textClearHint.Text = "向右滑动清空";
                }

                return;
            }

            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
            {
                try { ReleaseAllTouchCaptures(); } catch { Debug.WriteLine("[DBG] SliderClear_PreviewMouseUp: ReleaseAllTouchCaptures(window) failed"); }
                try { MyCanvas.ReleaseAllTouchCaptures(); } catch { Debug.WriteLine("[DBG] SliderClear_PreviewMouseUp: ReleaseAllTouchCaptures(canvas) failed"); }
                try { Mouse.Capture(null); } catch { Debug.WriteLine("[DBG] SliderClear_PreviewMouseUp: Mouse.Capture(null) failed"); }

                if (_popupEraserClear != null) _popupEraserClear.IsOpen = false;
                try { slider.BeginAnimation(RangeBase.ValueProperty, null); } catch { }
                slider.Value = 0;
                _clearPendingClose = false;
                _clearSlideTriggered = false;

                if (_textClearHint != null)
                {
                    _textClearHint.Text = "向右滑动清空";
                }

                Debug.WriteLine("[DBG] SliderClear_PreviewMouseUp: popup closed and slider reset; switch to pen");
                if (RadioPen != null) RadioPen.IsChecked = true;
            }));
        }

        // 弹窗被关闭时，清理残留的捕获并复位标记
        private void PopupEraserClear_Closed(object sender, EventArgs e)
        {
            Debug.WriteLine("[DBG] PopupEraserClear_Closed: releasing captures and resetting flags");
            try { ReleaseAllTouchCaptures(); } catch { Debug.WriteLine("[DBG] PopupEraserClear_Closed: ReleaseAllTouchCaptures(window) failed"); }
            try { MyCanvas.ReleaseAllTouchCaptures(); } catch { Debug.WriteLine("[DBG] PopupEraserClear_Closed: ReleaseAllTouchCaptures(canvas) failed"); }
            try { Mouse.Capture(null); } catch { Debug.WriteLine("[DBG] PopupEraserClear_Closed: Mouse.Capture(null) failed"); }
            _clearPendingClose = false;
        }

        // 点击外部关闭弹窗（支持鼠标与触摸） + Esc 关闭
        private void Root_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            var src = e.OriginalSource as DependencyObject;
            if (ShouldSkipCloseForSource(src)) return;
            CloseAllPopups();
        }

        private void Root_PreviewTouchDown(object? sender, TouchEventArgs e)
        {
            var src = e.OriginalSource as DependencyObject;
            if (ShouldSkipCloseForSource(src)) return;
            CloseAllPopups();
        }

        private void Window_PreviewKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                CloseAllPopups();
            }
        }

        private bool ShouldSkipCloseForSource(DependencyObject? source)
        {
            if (source == null) { return false; }
            try
            {
                // 点击工具按钮本身不关闭（Toggle 自己处理）
                if (RadioPen != null && IsVisualAncestorOf(RadioPen, source)) { return true; }
                if (RadioEraser != null && IsVisualAncestorOf(RadioEraser, source)) { return true; }
                if (BtnMore != null && IsVisualAncestorOf(BtnMore, source)) { return true; }

                // 点击弹窗内容不关闭（允许正常操作）：使用视觉祖先判断，兼容触摸（MouseOver 可能为 false）
                if (_popupPenSettings?.IsOpen == true)
                {
                    bool inside = _popupPenSettings.Child is DependencyObject penChild && IsVisualAncestorOf(penChild, source);
                    if (inside) return true;
                }
                if (_popupEraserClear?.IsOpen == true)
                {
                    bool insideChild = _popupEraserClear.Child is DependencyObject eraserChild && IsVisualAncestorOf(eraserChild, source);
                    bool insideSlider = _sliderClear != null && IsVisualAncestorOf(_sliderClear, source);
                    if (insideChild || insideSlider) return true;
                }
                if (_popupMoreMenu?.IsOpen == true)
                {
                    bool inside = _popupMoreMenu.Child is DependencyObject moreChild && IsVisualAncestorOf(moreChild, source);
                    if (inside) return true;
                }
            }
            catch { }
            return false;
        }

        private static bool IsVisualAncestorOf(DependencyObject ancestor, DependencyObject? descendant)
        {
            for (var d = descendant; d != null; d = VisualTreeHelper.GetParent(d))
            {
                if (ReferenceEquals(d, ancestor)) return true;
            }
            return false;
        }

        private static bool IsMouseOverElement(UIElement el)
        {
            try
            {
                if (el.IsMouseDirectlyOver || el.IsMouseOver) return true;
            }
            catch { }
            return false;
        }

        private void CloseAllPopups()
        {
            bool any = false;
            if (_popupPenSettings?.IsOpen == true)
            {
                _popupPenSettings.IsOpen = false;
                any = true;
            }
            if (_popupEraserClear?.IsOpen == true)
            {
                _popupEraserClear.IsOpen = false;
                any = true;
            }
            if (_popupMoreMenu?.IsOpen == true)
            {
                _popupMoreMenu.IsOpen = false;
                any = true;
            }
            if (any)
            {
                Debug.WriteLine("[TRACE] CloseAllPopups by outside click/Esc");
            }
        }

        #region System Dock UI（左下角）
        private void BtnMore_Click(object sender, RoutedEventArgs e)
        {
            // Toggle “更多”弹出菜单
            if (_popupMoreMenu == null)
                _popupMoreMenu = (Popup)FindName("PopupMoreMenu");
            if (_popupMoreMenu != null)
            {
                _popupMoreMenu.IsOpen = !_popupMoreMenu.IsOpen;
            }
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            // 最小化窗口
            WindowState = WindowState.Minimized;
        }

        private void MenuSettings_Click(object sender, RoutedEventArgs e)
        {
            // 关闭菜单
            if (_popupMoreMenu != null) _popupMoreMenu.IsOpen = false;
            
            // 打开设置窗口（无业务依赖）
            var settingsWindow = new SettingsWindow();
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

        // --- 视频展台 ---
        private void BtnVideoPresenter_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var path = SettingsService.Instance.GetVideoPresenterPath();
                var args = SettingsService.Instance.GetVideoPresenterArgs();

                if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
                {
                    ShowVideoPresenterNotFoundDialog(null);
                    return;
                }

                var psi = new ProcessStartInfo
                {
                    FileName = path,
                    Arguments = string.IsNullOrWhiteSpace(args) ? string.Empty : args,
                    UseShellExecute = true,
                    WorkingDirectory = System.IO.Path.GetDirectoryName(path) ?? Environment.CurrentDirectory
                };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                ShowVideoPresenterNotFoundDialog(ex.Message);
            }
        }

        private void ShowVideoPresenterNotFoundDialog(string? error)
        {
            string msg = "未找到“视频展台”程序。请前往 基本设置-视频展台 进行设置。";
            if (!string.IsNullOrWhiteSpace(error))
            {
                msg += "\n\n错误详情: " + error;
            }
            var result = MessageBox.Show(msg, "视频展台", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.Yes);
            if (result == MessageBoxResult.Yes)
            {
                // 直接打开设置窗口（暂不强制导航到具体卡片）
                // 后续可在 SettingsWindow 内新增接口以导航到“基本设置”
                var settingsWindow = new SettingsWindow { Owner = this };
                settingsWindow.ShowDialog();
            }
        }
    }
}
