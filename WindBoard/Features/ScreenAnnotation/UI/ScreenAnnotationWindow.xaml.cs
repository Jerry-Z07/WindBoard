using System;
using System.Numerics;
using Microsoft.UI.Xaml;
using WindBoard.Board.Editing;
using WindBoard.Controls;
using WindBoard.Features.ScreenAnnotation.Interop;
using WindBoard.Features.ScreenAnnotation.Models;
using WindBoard.Features.ScreenAnnotation.Services;
using WindBoard.Features.ScreenAnnotation.UI.Backdrop;
using WindBoard.Logging;

namespace WindBoard.Features.ScreenAnnotation.UI
{
    /// <summary>
    /// 透明批注层窗口。
    /// </summary>
    public sealed partial class ScreenAnnotationWindow : Window, IScreenAnnotationModeOverlay
    {
        private const uint WmWindowPosChanged = 0x0047;

        private readonly ScreenAnnotationDisplayTarget _displayTarget;
        private readonly ScreenAnnotationSessionHost _sessionHost;
        private readonly ScreenAnnotationWindowState _windowState;
        private readonly IBoardEraser _pixelEraser;
        private readonly IBoardEraser _wholeStrokeEraser = new WholeStrokeEraser();
        private ScreenAnnotationTransparentBackdrop? _transparentBackdrop;
        private bool _isWindowInitialized;
        private bool _isCanvasConfigured;
        private Windows.UI.Color _currentPenColor;
        private float _currentPenBaseSize;
        private ScreenAnnotationEraserMode _currentEraserMode;

        internal ScreenAnnotationWindow(
            ScreenAnnotationDisplayTarget displayTarget,
            ScreenAnnotationSessionHost sessionHost,
            ScreenAnnotationWindowState windowState)
        {
            _displayTarget = displayTarget;
            _sessionHost = sessionHost ?? throw new ArgumentNullException(nameof(sessionHost));
            _windowState = windowState ?? throw new ArgumentNullException(nameof(windowState));
            _pixelEraser = _sessionHost.DefaultEraser;
            _currentPenColor = _sessionHost.DefaultPenColor;
            _currentPenBaseSize = _sessionHost.DefaultPenBaseSize;
            _currentEraserMode = _sessionHost.DefaultEraserMode;

            InitializeComponent();

            Activated += OnWindowActivated;
            Closed += OnWindowClosed;
            BoardCanvas.Loaded += OnBoardCanvasLoaded;
        }

        internal event Action<ScreenAnnotationDrawingStateSnapshot>? DrawingStateChanged;

        internal void ApplyMode(ScreenAnnotationMode mode)
        {
            _windowState.SetMode(mode);

            if (_isCanvasConfigured)
            {
                BoardCanvas.Tool = _windowState.ActiveCanvasTool;
            }

            IntPtr hwnd = ScreenAnnotationWindowInterop.GetWindowHandle(this);
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            if (!ScreenAnnotationWindowInterop.TrySetPassThrough(hwnd, _windowState.IsPassThrough, out string? error))
            {
                AppLog.Warn("ScreenAnnotation.Interop", $"切换批注层穿透失败：mode={mode}, error='{error}'");
            }
        }

        internal ScreenAnnotationDrawingStateSnapshot GetDrawingStateSnapshot()
        {
            bool canClear = _isCanvasConfigured
                ? BoardCanvas.CanClear
                : _sessionHost.Session.HasStrokes;

            return new ScreenAnnotationDrawingStateSnapshot(
                PenColor: _currentPenColor,
                PenBaseSize: _currentPenBaseSize,
                EraserMode: _currentEraserMode,
                CanClear: canClear);
        }

        internal void SetPenColor(Windows.UI.Color color)
        {
            _currentPenColor = NormalizeOpaqueColor(color);

            if (_isCanvasConfigured)
            {
                // 组合 ToolOptions（design C）：颜色仅影响后续新建笔迹。
                BoardCanvas.ToolOptions = BoardCanvas.ToolOptions with
                {
                    PenColor = BoardCanvasControl.ToColor4(_currentPenColor),
                };
            }

            RaiseDrawingStateChanged();
        }

        internal void SetPenBaseSize(float size)
        {
            if (float.IsNaN(size) || float.IsInfinity(size) || size < 0.5f || size > 64.0f)
            {
                throw new ArgumentOutOfRangeException(nameof(size));
            }

            _currentPenBaseSize = size;

            if (_isCanvasConfigured)
            {
                // 组合 ToolOptions（design C）：粗细仅影响后续新建笔迹。
                BoardCanvas.ToolOptions = BoardCanvas.ToolOptions with
                {
                    PenBaseSize = _currentPenBaseSize,
                };
            }

            RaiseDrawingStateChanged();
        }

        internal void SetEraserMode(ScreenAnnotationEraserMode mode)
        {
            _currentEraserMode = mode;

            if (_isCanvasConfigured)
            {
                BoardCanvas.Eraser = ResolveEraser(mode);
            }

            RaiseDrawingStateChanged();
        }

        internal void ClearAll()
        {
            if (_isCanvasConfigured)
            {
                BoardCanvas.ClearAll();
                return;
            }

            _sessionHost.Session.ClearAll();
            RaiseDrawingStateChanged();
        }

        private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
        {
            if (!_isWindowInitialized)
            {
                return;
            }

            if (args.WindowActivationState != WindowActivationState.Deactivated)
            {
                OverlayActivated?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// 在窗口首次显示（<see cref="Window.Activate"/>）之前完成原生窗口与透明背景配置。
        /// </summary>
        /// <remarks>
        /// 必须前置到显示之前：WinUI 顶层窗口在未挂透明 backdrop 时会以主题底色绘制首帧
        /// （浅色主题为白），若推迟到首次 Activated 再配置，用户会先看到一个未配置的白底窗口；
        /// 主窗口处于全屏时该窗口还会与全屏呈现切换叠加，白闪更明显。
        /// </remarks>
        internal bool TryInitializeForDisplay()
        {
            if (_isWindowInitialized)
            {
                return true;
            }

            IntPtr hwnd = ScreenAnnotationWindowInterop.GetWindowHandle(this);
            if (hwnd == IntPtr.Zero)
            {
                AppLog.Warn("ScreenAnnotation.Interop", "配置批注层窗口失败：无法获取窗口句柄。");
                return false;
            }

            var appWindow = ScreenAnnotationWindowInterop.TryGetAppWindow(hwnd);
            if (appWindow is null)
            {
                AppLog.Warn("ScreenAnnotation.Interop", "配置批注层窗口失败：无法获取 AppWindow。");
                return false;
            }

            if (!ScreenAnnotationWindowInterop.TryConfigureBorderlessWindow(appWindow, _displayTarget.Bounds, out string? windowError))
            {
                AppLog.Warn("ScreenAnnotation.Interop", $"配置批注层窗口失败：error='{windowError}'");
                return false;
            }

            if (!TryAttachTransparentBackdrop(hwnd, out string? backdropError))
            {
                AppLog.Warn("ScreenAnnotation.Interop", $"初始化批注层透明背景失败：error='{backdropError}'");
                return false;
            }

            if (!ScreenAnnotationWindowInterop.TryPrepareAnnotationWindow(hwnd, out string? interopError))
            {
                AppLog.Warn("ScreenAnnotation.Interop", $"初始化批注层原生样式失败：error='{interopError}'");
                return false;
            }

            _isWindowInitialized = true;
            return true;
        }

        internal event EventHandler? OverlayActivated;

        internal event EventHandler<ScreenAnnotationOverlayWindowPositionChangedEventArgs>? OverlayWindowPositionChanged;

        internal bool TryGetWindowHandle(out IntPtr hwnd)
        {
            hwnd = ScreenAnnotationWindowInterop.GetWindowHandle(this);
            return hwnd != IntPtr.Zero;
        }

        private void OnBoardCanvasLoaded(object sender, RoutedEventArgs e)
        {
            if (_isCanvasConfigured)
            {
                return;
            }

            // 批注层复用现有画布控件，但关闭视口手势与选择交互，只保留书写/擦除。
            BoardCanvas.BindSession(_sessionHost.Session);
            BoardCanvas.CanvasBackgroundColor = _sessionHost.CanvasBackgroundColor;
            // 组合 ToolOptions（design C）：一次写入颜色与粗细（仅影响后续新建笔迹）。
            BoardCanvas.ToolOptions = BoardCanvas.ToolOptions with
            {
                PenColor = BoardCanvasControl.ToColor4(_currentPenColor),
                PenBaseSize = _currentPenBaseSize,
            };
            BoardCanvas.Eraser = ResolveEraser(_currentEraserMode);
            BoardCanvas.SetInteractionOptions(allowViewportManipulation: false, allowSelectionInteraction: false);
            BoardCanvas.CommandStateChanged += OnBoardCanvasCommandStateChanged;

            ScreenAnnotationViewportPreset preset = _sessionHost.BuildViewportPreset(
                new Vector2(
                    Math.Max(1, _displayTarget.Bounds.Width),
                    Math.Max(1, _displayTarget.Bounds.Height)));
            BoardCanvas.SetView(preset.CameraWorld, preset.Zoom);

            _isCanvasConfigured = true;
            ApplyMode(_windowState.Mode);
            RaiseDrawingStateChanged();
        }

        private bool TryAttachTransparentBackdrop(IntPtr hwnd, out string? error)
        {
            if (_transparentBackdrop is not null)
            {
                error = null;
                return true;
            }

            try
            {
                // 透明 backdrop 用于避免 WinUI 顶层窗口在透明场景下退化为黑底。
                var backdrop = new ScreenAnnotationTransparentBackdrop(hwnd);
                backdrop.WindowMessageObserved += OnBackdropWindowMessageObserved;
                SystemBackdrop = backdrop;
                _transparentBackdrop = backdrop;
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private void OnWindowClosed(object sender, WindowEventArgs args)
        {
            BoardCanvas.CommandStateChanged -= OnBoardCanvasCommandStateChanged;

            if (_transparentBackdrop is not null)
            {
                _transparentBackdrop.WindowMessageObserved -= OnBackdropWindowMessageObserved;
            }

            SystemBackdrop = null;
            _transparentBackdrop = null;
        }

        private void OnBackdropWindowMessageObserved(object? sender, Backdrop.ScreenAnnotationWindowMessageEventArgs e)
        {
            if (!_isWindowInitialized || e.MessageId != WmWindowPosChanged)
            {
                return;
            }

            if (!ScreenAnnotationWindowInterop.TryReadWindowPos(e.LParam, out IntPtr insertAfterHwnd, out uint flags))
            {
                return;
            }

            // 只把窗口位置变化事实向上层汇报，由流程层决定是否需要恢复“工具栏在上、批注层在下”的顺序。
            OverlayWindowPositionChanged?.Invoke(
                this,
                new ScreenAnnotationOverlayWindowPositionChangedEventArgs(insertAfterHwnd, flags));
        }

        private void OnBoardCanvasCommandStateChanged(object? sender, EventArgs e)
        {
            RaiseDrawingStateChanged();
        }

        private IBoardEraser ResolveEraser(ScreenAnnotationEraserMode mode)
        {
            return mode == ScreenAnnotationEraserMode.WholeStroke ? _wholeStrokeEraser : _pixelEraser;
        }

        private static Windows.UI.Color NormalizeOpaqueColor(Windows.UI.Color color)
        {
            return Windows.UI.Color.FromArgb(0xFF, color.R, color.G, color.B);
        }

        private void RaiseDrawingStateChanged()
        {
            DrawingStateChanged?.Invoke(GetDrawingStateSnapshot());
        }

        void IScreenAnnotationModeOverlay.ApplyMode(ScreenAnnotationMode mode)
        {
            ApplyMode(mode);
        }

        bool IScreenAnnotationModeOverlay.TryGetWindowHandle(out IntPtr hwnd)
        {
            return TryGetWindowHandle(out hwnd);
        }
    }

    internal sealed class ScreenAnnotationOverlayWindowPositionChangedEventArgs : EventArgs
    {
        internal ScreenAnnotationOverlayWindowPositionChangedEventArgs(IntPtr insertAfterHwnd, uint windowPosFlags)
        {
            InsertAfterHwnd = insertAfterHwnd;
            WindowPosFlags = windowPosFlags;
        }

        internal IntPtr InsertAfterHwnd { get; }

        internal uint WindowPosFlags { get; }
    }
}
