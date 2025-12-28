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
using MaterialDesignThemes.Wpf;
using System.Threading.Tasks;
using WindBoard.Services;

namespace WindBoard
{

    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name ?? string.Empty));

        private string _windowTitle = "WindBoard";
        public string WindowTitle
        {
            get => _windowTitle;
            set
            {
                if (_windowTitle != value)
                {
                    _windowTitle = value;
                    OnPropertyChanged();
                }
            }
        }

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

        private ImageSource? _defaultIcon;
        private string _defaultTitle = "WindBoard";

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;
            InitializeSettings();

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

            _touchGestureService = new TouchGestureService();
            var windowHandle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            _touchGestureService.DisableSystemGestures(windowHandle);

            Dispatcher.InvokeAsync(() =>
            {
                // 运行时缩放/拖动使用 RenderTransform，不再依赖 ScrollViewer offset。
                // 初始化时将画布中心对齐到视口中心。
                double viewportW = Viewport.ActualWidth;
                double viewportH = Viewport.ActualHeight;
                if (viewportW <= 0 || viewportH <= 0)
                {
                    Viewport.UpdateLayout();
                    viewportW = Viewport.ActualWidth;
                    viewportH = Viewport.ActualHeight;
                }

                double zoom = _zoomPanService.Zoom;
                double panX = viewportW / 2.0 - (MyCanvas.Width / 2.0) * zoom;
                double panY = viewportH / 2.0 - (MyCanvas.Height / 2.0) * zoom;
                _zoomPanService.SetPanDirect(panX, panY);

                _strokeService.UpdatePenThickness(_zoomPanService.Zoom);


            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }




    }
}
