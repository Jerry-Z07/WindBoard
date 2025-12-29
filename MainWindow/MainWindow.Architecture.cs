using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Ink;
using System.Windows.Media;
using System.Windows.Shapes;
using WindBoard.Core.Filters;
using WindBoard.Core.Input;
using WindBoard.Core.Input.RealTimeStylus;
using WindBoard.Core.Modes;
using WindBoard.Services;
using System.Windows.Threading;
using InputEventArgs = WindBoard.Core.Input.InputEventArgs;
using InputManagerCore = WindBoard.Core.Input.InputManager;

namespace WindBoard
{
    public partial class MainWindow
    {
        private const double MinZoom = 0.25;
        private const double MaxZoom = 5.25;

        private double _baseThickness = 1.75;
        private readonly double _eraserCursorOffsetY = 12.0;

        private Canvas? _eraserOverlay;
        private Border? _eraserCursorRect;

        private ModeController _modeController = null!;
        private InputManagerCore _inputManager = null!;
        private RealTimeStylusManager? _realTimeStylusManager;
        private InputSourceSelector? _inputSourceSelector;
        private ZoomPanService _zoomPanService = null!;
        private StrokeService _strokeService = null!;
        private AutoExpandService _autoExpandService = null!;
        private PageService _pageService = null!;
        private TouchGestureService _touchGestureService = null!;
        private IInteractionMode? _modeBeforePan;
        private IInteractionMode? _modeBeforeGesture;
        private bool _gestureInputSuppressed;
        private bool _strokeSuppressionActive;
        private bool _viewportBitmapCacheEnabled;
        private DispatcherTimer? _viewportCacheDisableTimer;
        private BitmapCache? _viewportBitmapCache;
        private StrokeCollection? _undoObservedStrokes;
        private readonly TranslateTransform _panTransform = new TranslateTransform();

        private InkMode? _inkMode;
        private EraserMode? _eraserMode;
        private SelectMode? _selectMode;
        private NoMode? _noMode;

        public ObservableCollection<BoardPage> Pages => _pageService.Pages;
        public bool IsMultiPage => _pageService.IsMultiPage;
        public string PageIndicatorText => _pageService.PageIndicatorText;
        public ObservableCollection<BoardAttachment>? CurrentAttachments => _pageService?.CurrentPage?.Attachments;

        private void InitializeArchitecture()
        {
            _eraserOverlay = (Canvas)FindName("EraserOverlay");
            _eraserCursorRect = (Border)FindName("EraserCursorRect");

            _modeController = new ModeController();
            _strokeService = new StrokeService(MyCanvas, _baseThickness);

            // 性能：避免使用 LayoutTransform（会触发布局）；改用 RenderTransform 实现“相机式”缩放/平移。
            // XAML 中仍声明了 ZoomTransform（原用于 LayoutTransform），这里在运行时将其移到 RenderTransform。
            if (CanvasHost != null)
            {
                CanvasHost.LayoutTransform = null;
                var group = new TransformGroup();
                group.Children.Add(ZoomTransform);
                group.Children.Add(_panTransform);
                CanvasHost.RenderTransform = group;
            }

            _zoomPanService = new ZoomPanService(ZoomTransform, _panTransform, MinZoom, MaxZoom, zoom => _strokeService.UpdatePenThickness(zoom));
            _strokeService.SetStrokeThicknessConsistencyEnabled(
                SettingsService.Instance.GetStrokeThicknessConsistencyEnabled(),
                _zoomPanService.Zoom);
            _pageService = new PageService(MyCanvas, _zoomPanService, NotifyPageUiChanged);
            _autoExpandService = new AutoExpandService(MyCanvas, _zoomPanService, () => _pageService.CurrentPage, () => _inkMode?.HasActiveStroke ?? false);

            _inkMode = new InkMode(MyCanvas, () => _zoomPanService.Zoom, OnInkStrokeEndedOrCanceled);
            _selectMode = new SelectMode(MyCanvas);
            _noMode = new NoMode(MyCanvas);
            _eraserMode = new EraserMode(
                MyCanvas,
                _eraserOverlay ?? new Canvas(),
                _eraserCursorRect ?? new Border(),
                () => _zoomPanService.Zoom,
                _eraserCursorOffsetY);

            _modeController.SetCurrentMode(_inkMode);

            _inputManager = new InputManagerCore(_modeController);
            _inputManager.RegisterFilter(new ExclusiveModeFilter(_noMode));
            _inputManager.PointerMove += OnPointerMoveForServices;
            _inputManager.PointerDown += OnPointerDownForServices;
            _inputManager.PointerUp += OnPointerUpForServices;
            if (MyCanvas != null && Viewport != null)
            {
                _realTimeStylusManager = new RealTimeStylusManager(MyCanvas, Viewport, DispatchStylusFromRealTimeStylus);
                _inputSourceSelector = new InputSourceSelector(_realTimeStylusManager);
                _inputSourceSelector.InitializeAuto();

                // 输出当前输入源信息
                var sourceName = _inputSourceSelector.ActiveSource == InputSourceKind.RealTimeStylus
                    ? "RealTimeStylus"
                    : "WPF 标准输入";
                System.Diagnostics.Debug.WriteLine($"[InputSource] 当前输入源: {sourceName}");
                System.Diagnostics.Debug.WriteLine($"[InputSource] RTS 是否运行: {_inputSourceSelector.IsRealTimeStylusActive}");
                System.Diagnostics.Debug.WriteLine($"[InputSource] 是否支持 RTS: {_realTimeStylusManager.IsSupported}");
            }

            ConfigureStylusForTouchInk();

            if (MyCanvas == null) return;

            // 即使 InkCanvas 将事件标记为 Handled，也要接收
            MyCanvas.AddHandler(MouseDownEvent, new MouseButtonEventHandler(MyCanvas_MouseDown), true);
            MyCanvas.AddHandler(MouseMoveEvent, new MouseEventHandler(MyCanvas_MouseMove), true);
            MyCanvas.AddHandler(MouseUpEvent, new MouseButtonEventHandler(MyCanvas_MouseUp), true);

            MyCanvas.AddHandler(StylusDownEvent, new StylusDownEventHandler(MyCanvas_StylusDown), true);
            MyCanvas.AddHandler(StylusMoveEvent, new StylusEventHandler(MyCanvas_StylusMove), true);
            MyCanvas.AddHandler(StylusUpEvent, new StylusEventHandler(MyCanvas_StylusUp), true);
            MyCanvas.AddHandler(StylusInAirMoveEvent, new StylusEventHandler(MyCanvas_StylusInAirMove), true);

#pragma warning disable CS8622 // 参数类型中引用类型的为 Null 性与目标委托不匹配(可能是由于为 Null 性特性)。
            MyCanvas.TouchDown += MyCanvas_TouchDown;
#pragma warning restore CS8622 // 参数类型中引用类型的为 Null 性与目标委托不匹配(可能是由于为 Null 性特性)。
#pragma warning disable CS8622 // 参数类型中引用类型的为 Null 性与目标委托不匹配(可能是由于为 Null 性特性)。
            MyCanvas.TouchMove += MyCanvas_TouchMove;
#pragma warning restore CS8622 // 参数类型中引用类型的为 Null 性与目标委托不匹配(可能是由于为 Null 性特性)。
#pragma warning disable CS8622 // 参数类型中引用类型的为 Null 性与目标委托不匹配(可能是由于为 Null 性特性)。
            MyCanvas.TouchUp += MyCanvas_TouchUp;
#pragma warning restore CS8622 // 参数类型中引用类型的为 Null 性与目标委托不匹配(可能是由于为 Null 性特性)。
#pragma warning disable CS8622 // 参数类型中引用类型的为 Null 性与目标委托不匹配(可能是由于为 Null 性特性)。
            MyCanvas.TouchLeave += MyCanvas_TouchUp;
#pragma warning restore CS8622 // 参数类型中引用类型的为 Null 性与目标委托不匹配(可能是由于为 Null 性特性)。

            MyCanvas.StrokeCollected += _autoExpandService.OnStrokeCollected;
            MyCanvas.StrokeCollected += SuppressGestureStroke;

            _pageService.InitializePagesIfNeeded();
            _pageService.Pages.CollectionChanged += (s, e) => NotifyPageUiChanged();
            NotifyPageUiChanged();

            AttachUndoToCurrentStrokes();
            MyCanvas.CommandBindings.Add(new CommandBinding(ApplicationCommands.Undo, Undo_Executed, Undo_CanExecute));
            MyCanvas.CommandBindings.Add(new CommandBinding(ApplicationCommands.Redo, Redo_Executed, Redo_CanExecute));

            InitializeAttachmentUi();
        }

        private void OnInkStrokeEndedOrCanceled()
        {
            _autoExpandService.FlushPendingShift();
            var cur = _pageService.CurrentPage;
            if (cur != null)
            {
                cur.ContentVersion++;
            }
        }

        private void SetViewportBitmapCache(bool enabled)
        {
            // 注意：不要对 CanvasHost 做 BitmapCache，它的尺寸等于整张画布（默认 8000x8000），
            // 会直接分配上百 MB 的缓存位图，拖动时内存暴涨。
            if (Viewport == null) return;

            if (enabled)
            {
                if (_viewportBitmapCacheEnabled) return;
                _viewportBitmapCache ??= new BitmapCache(1.0);
                Viewport.CacheMode = _viewportBitmapCache;

                // 降低交互时缩放质量以减轻 GPU/CPU 压力（结束后恢复）
                if (CanvasHost != null)
                {
                    RenderOptions.SetBitmapScalingMode(CanvasHost, BitmapScalingMode.LowQuality);
                }
                _viewportBitmapCacheEnabled = true;
            }
            else
            {
                if (!_viewportBitmapCacheEnabled) return;
                Viewport.CacheMode = null;
                if (CanvasHost != null)
                {
                    RenderOptions.SetBitmapScalingMode(CanvasHost, BitmapScalingMode.HighQuality);
                }
                _viewportBitmapCacheEnabled = false;
            }
        }

        private void ConfigureStylusForTouchInk()
        {
            if (MyCanvas == null) return;
            try
            {
                Stylus.SetIsPressAndHoldEnabled(MyCanvas, false);
                Stylus.SetIsTapFeedbackEnabled(MyCanvas, false);
                Stylus.SetIsFlicksEnabled(MyCanvas, false);
            }
            catch
            {
            }
        }

        private void ScheduleViewportCacheDisable(int delayMs = 180)
        {
            if (_viewportCacheDisableTimer == null)
            {
                _viewportCacheDisableTimer = new DispatcherTimer(DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromMilliseconds(delayMs)
                };
                _viewportCacheDisableTimer.Tick += (_, __) =>
                {
                    _viewportCacheDisableTimer?.Stop();
                    SetViewportBitmapCache(false);
                };
            }

            _viewportCacheDisableTimer.Interval = TimeSpan.FromMilliseconds(delayMs);
            _viewportCacheDisableTimer.Stop();
            _viewportCacheDisableTimer.Start();
        }

        private void NotifyPageUiChanged()
        {
            OnPropertyChanged(nameof(IsMultiPage));
            OnPropertyChanged(nameof(PageIndicatorText));
            OnPropertyChanged(nameof(CurrentAttachments));
        }

        private void BeginUndoTransactionForCurrentMode()
        {
            var cur = _pageService.CurrentPage;
            if (cur == null) return;

            var mode = _modeController.ActiveMode ?? _modeController.CurrentMode;
            if (ReferenceEquals(mode, _inkMode) || ReferenceEquals(mode, _eraserMode))
            {
                cur.UndoHistory.Begin();
            }
        }

        private void EndUndoTransactionForCurrentMode()
        {
            var cur = _pageService.CurrentPage;
            if (cur == null) return;

            var mode = _modeController.ActiveMode ?? _modeController.CurrentMode;
            if (ReferenceEquals(mode, _inkMode) || ReferenceEquals(mode, _eraserMode))
            {
                cur.UndoHistory.End();
            }
        }

        private void OnPointerDownForServices(object? sender, InputEventArgs e)
        {
            // reserved for future hooks
        }

        private void OnPointerMoveForServices(object? sender, InputEventArgs e)
        {
            bool pressed = e.DeviceType == InputDeviceType.Touch
                || (e.DeviceType == InputDeviceType.Mouse && (e.LeftButton || e.RightButton || e.MiddleButton))
                || (e.DeviceType == InputDeviceType.Stylus && !e.IsInAir);

            if (pressed)
            {
                _autoExpandService.EnsureCanvasSpace(e.CanvasPoint);
            }
        }

        private void OnPointerUpForServices(object? sender, InputEventArgs e)
        {
            // reserved for future hooks
        }

        private void BeginGestureSuppression()
        {
            if (_gestureInputSuppressed) return;
            _gestureInputSuppressed = true;
            _modeBeforeGesture = _modeController.CurrentMode;
            _modeController.ClearActiveMode();
            _inputManager.InputSuppressed = true;
            MyCanvas.EditingMode = InkCanvasEditingMode.None;
            SetViewportBitmapCache(true);
            _strokeSuppressionActive = true;
            _pageService.CurrentPage?.UndoHistory.Cancel();
            _inkMode?.CancelAllStrokes();
        }

        private void AttachUndoToCurrentStrokes()
        {
            if (_undoObservedStrokes != null)
            {
                _undoObservedStrokes.StrokesChanged -= UndoObservedStrokes_StrokesChanged;
            }

            _undoObservedStrokes = MyCanvas.Strokes;
            if (_undoObservedStrokes != null)
            {
                _undoObservedStrokes.StrokesChanged += UndoObservedStrokes_StrokesChanged;
            }
        }

        private void UndoObservedStrokes_StrokesChanged(object? sender, StrokeCollectionChangedEventArgs e)
        {
            _pageService.CurrentPage?.UndoHistory.Record(e);
        }

        private void Undo_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = _pageService.CurrentPage?.UndoHistory.CanUndo == true;
            e.Handled = true;
        }

        private void Undo_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            var cur = _pageService.CurrentPage;
            if (cur == null) return;
            cur.UndoHistory.Undo(MyCanvas.Strokes);
            e.Handled = true;
        }

        private void Redo_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = _pageService.CurrentPage?.UndoHistory.CanRedo == true;
            e.Handled = true;
        }

        private void Redo_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            var cur = _pageService.CurrentPage;
            if (cur == null) return;
            cur.UndoHistory.Redo(MyCanvas.Strokes);
            e.Handled = true;
        }

        private void EndGestureSuppression()
        {
            if (!_gestureInputSuppressed) return;
            _gestureInputSuppressed = false;
            _inputManager.InputSuppressed = false;
            _strokeSuppressionActive = false;

            var targetMode = _modeBeforeGesture ?? _inkMode;
            if (targetMode != null)
            {
                if (ReferenceEquals(_modeController.CurrentMode, targetMode))
                {
                    targetMode.SwitchOn();
                }
                else
                {
                    _modeController.SetCurrentMode(targetMode);
                }
            }
            _modeBeforeGesture = null;
            ScheduleViewportCacheDisable();
        }

        private void SuppressGestureStroke(object? sender, InkCanvasStrokeCollectedEventArgs e)
        {
            if (!_strokeSuppressionActive) return;
            try
            {
                MyCanvas.Strokes.Remove(e.Stroke);
            }
            catch
            {
            }
        }
    }
}
