using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Ink;
using System.Windows.Media;
using WindBoard.Core.Filters;
using WindBoard.Core.Input;
using WindBoard.Core.Modes;
using WindBoard.Services;
using InputEventArgs = WindBoard.Core.Input.InputEventArgs;
using InputManagerCore = WindBoard.Core.Input.InputManager;

namespace WindBoard
{
    public partial class MainWindow
    {
        private const double MinZoom = 0.5;
        private const double MaxZoom = 5.0;

        private double _baseThickness = 3.0;
        private readonly double _eraserCursorOffsetY = 12.0;

        private Canvas? _eraserOverlay;
        private Border? _eraserCursorRect;

        private ModeController _modeController = null!;
        private InputManagerCore _inputManager = null!;
        private ZoomPanService _zoomPanService = null!;
        private StrokeService _strokeService = null!;
        private AutoExpandService _autoExpandService = null!;
        private PageService _pageService = null!;
        private IInteractionMode? _modeBeforePan;
        private IInteractionMode? _modeBeforeGesture;
        private bool _gestureInputSuppressed;
        private bool _strokeSuppressionActive;

        private InkMode? _inkMode;
        private EraserMode? _eraserMode;
        private SelectMode? _selectMode;
        private NoMode? _noMode;

        public ObservableCollection<BoardPage> Pages => _pageService.Pages;
        public bool IsMultiPage => _pageService.IsMultiPage;
        public string PageIndicatorText => _pageService.PageIndicatorText;

        private void InitializeArchitecture()
        {
            _eraserOverlay = (Canvas)FindName("EraserOverlay");
            _eraserCursorRect = (Border)FindName("EraserCursorRect");

            _modeController = new ModeController();
            _strokeService = new StrokeService(MyCanvas, _baseThickness);
            _zoomPanService = new ZoomPanService(Viewport, ZoomTransform, MinZoom, MaxZoom, zoom => _strokeService.UpdatePenThickness(zoom));
            _pageService = new PageService(MyCanvas, Viewport, _zoomPanService, NotifyPageUiChanged);
            _autoExpandService = new AutoExpandService(MyCanvas, Viewport, _zoomPanService, () => _pageService.CurrentPage, () => _inkMode?.HasActiveStroke ?? false);

            _inkMode = new InkMode(MyCanvas, () => _zoomPanService.Zoom, _autoExpandService.FlushPendingShift);
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
            _inputManager.RegisterFilter(new GestureEraserFilter(_eraserMode));
            _inputManager.RegisterFilter(new ExclusiveModeFilter(_noMode));
            _inputManager.PointerMove += OnPointerMoveForServices;
            _inputManager.PointerDown += OnPointerDownForServices;
            _inputManager.PointerUp += OnPointerUpForServices;

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
            _pageService.AttachStrokeEvents();
            _pageService.Pages.CollectionChanged += (s, e) => NotifyPageUiChanged();
            NotifyPageUiChanged();
        }

        private void NotifyPageUiChanged()
        {
            OnPropertyChanged(nameof(IsMultiPage));
            OnPropertyChanged(nameof(PageIndicatorText));
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
            _modeBeforeGesture = _modeController.ActiveMode ?? _modeController.CurrentMode;
            _inputManager.InputSuppressed = true;
            MyCanvas.EditingMode = InkCanvasEditingMode.None;
            _strokeSuppressionActive = true;
            _inkMode?.CancelAllStrokes();
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
