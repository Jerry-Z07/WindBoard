using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;
using WindBoard.Features.ScreenAnnotation.Interop;
using WindBoard.Features.ScreenAnnotation.Models;
using WindBoard.Features.ScreenAnnotation.Services;
using WindBoard.Logging;

namespace WindBoard.Features.ScreenAnnotation.UI
{
    /// <summary>
    /// 屏幕批注悬浮工具栏。
    /// </summary>
    public sealed partial class ScreenAnnotationToolbarWindow : Window, IScreenAnnotationModeToolbar
    {
        private readonly ScreenAnnotationDisplayTarget _displayTarget;
        private bool _isWindowInitialized;
        private bool _isCollapsed;
        private uint? _dragPointerId;
        private PointInt32 _dragStartCursor;
        private PointInt32 _dragStartWindowOrigin;
        private bool _dragMoved;

        internal ScreenAnnotationToolbarWindow(ScreenAnnotationDisplayTarget displayTarget)
        {
            _displayTarget = displayTarget;

            InitializeComponent();
            Activated += OnWindowActivated;
        }

        internal event EventHandler<ScreenAnnotationMode>? ModeRequested;

        internal event EventHandler? ReturnToAppRequested;

        internal void SetSelectedMode(ScreenAnnotationMode mode)
        {
            PassThroughButton.IsChecked = mode == ScreenAnnotationMode.PassThrough;
            PenButton.IsChecked = mode == ScreenAnnotationMode.Pen;
            EraserButton.IsChecked = mode == ScreenAnnotationMode.Eraser;
        }

        internal void EnsureInteractiveTopMost(IScreenAnnotationModeOverlay? overlay)
        {
            IntPtr hwnd = ScreenAnnotationWindowInterop.GetWindowHandle(this);
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            if (!ScreenAnnotationWindowInterop.TryPromoteWindowToTopMost(hwnd, out string? error))
            {
                AppLog.Warn("ScreenAnnotation.Interop", $"刷新工具栏顶层顺序失败：error='{error}'");
                return;
            }

            if (overlay is null || !overlay.TryGetWindowHandle(out IntPtr overlayHwnd))
            {
                return;
            }

            if (!ScreenAnnotationWindowInterop.TryPlaceWindowBehind(overlayHwnd, hwnd, out string? stackError))
            {
                AppLog.Warn("ScreenAnnotation.Interop", $"恢复工具栏与批注层相对层级失败：error='{stackError}'");
            }
        }

        internal bool TryGetWindowHandle(out IntPtr hwnd)
        {
            hwnd = ScreenAnnotationWindowInterop.GetWindowHandle(this);
            return hwnd != IntPtr.Zero;
        }

        private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
        {
            if (_isWindowInitialized)
            {
                return;
            }

            IntPtr hwnd = ScreenAnnotationWindowInterop.GetWindowHandle(this);
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            var appWindow = ScreenAnnotationWindowInterop.TryGetAppWindow(hwnd);
            if (appWindow is null)
            {
                return;
            }

            RectInt32 bounds = _displayTarget.GetInitialToolbarBounds(width: 420, height: 72);
            if (!ScreenAnnotationWindowInterop.TryConfigureBorderlessWindow(appWindow, bounds, out string? windowError))
            {
                AppLog.Warn("ScreenAnnotation.Interop", $"配置工具栏窗口失败：error='{windowError}'");
                CloseWindowAfterInitializationFailure();
                return;
            }

            if (!ScreenAnnotationWindowInterop.TryPrepareToolbarWindow(hwnd, out string? interopError))
            {
                AppLog.Warn("ScreenAnnotation.Interop", $"初始化工具栏原生样式失败：error='{interopError}'");
                CloseWindowAfterInitializationFailure();
                return;
            }

            SetSelectedMode(ScreenAnnotationMode.PassThrough);
            _isWindowInitialized = true;
        }

        private void OnPassThroughButtonClicked(object sender, RoutedEventArgs e)
        {
            ModeRequested?.Invoke(this, ScreenAnnotationMode.PassThrough);
        }

        private void OnPenButtonClicked(object sender, RoutedEventArgs e)
        {
            ModeRequested?.Invoke(this, ScreenAnnotationMode.Pen);
        }

        private void OnEraserButtonClicked(object sender, RoutedEventArgs e)
        {
            ModeRequested?.Invoke(this, ScreenAnnotationMode.Eraser);
        }

        private void OnReturnToAppButtonClicked(object sender, RoutedEventArgs e)
        {
            ReturnToAppRequested?.Invoke(this, EventArgs.Empty);
        }

        private void OnDragHandlePointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (sender is not FrameworkElement element)
            {
                return;
            }

            if (!ScreenAnnotationWindowInterop.TryGetCursorScreenPosition(out PointInt32 cursorPosition))
            {
                return;
            }

            IntPtr hwnd = ScreenAnnotationWindowInterop.GetWindowHandle(this);
            if (!ScreenAnnotationWindowInterop.TryGetWindowRect(hwnd, out RectInt32 windowBounds))
            {
                return;
            }

            _dragPointerId = e.Pointer.PointerId;
            _dragStartCursor = cursorPosition;
            _dragStartWindowOrigin = new PointInt32(windowBounds.X, windowBounds.Y);
            _dragMoved = false;

            element.CapturePointer(e.Pointer);
            e.Handled = true;
        }

        private void OnDragHandlePointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_dragPointerId != e.Pointer.PointerId)
            {
                return;
            }

            if (!ScreenAnnotationWindowInterop.TryGetCursorScreenPosition(out PointInt32 cursorPosition))
            {
                return;
            }

            int deltaX = cursorPosition.X - _dragStartCursor.X;
            int deltaY = cursorPosition.Y - _dragStartCursor.Y;
            if (!_dragMoved && (Math.Abs(deltaX) > 3 || Math.Abs(deltaY) > 3))
            {
                _dragMoved = true;
            }

            var appWindow = ScreenAnnotationWindowInterop.TryGetAppWindow(this);
            if (appWindow is null)
            {
                return;
            }

            RectInt32 area = _displayTarget.WorkArea.Width > 0 && _displayTarget.WorkArea.Height > 0
                ? _displayTarget.WorkArea
                : _displayTarget.Bounds;

            int maxX = area.X + Math.Max(0, area.Width - appWindow.Size.Width);
            int maxY = area.Y + Math.Max(0, area.Height - appWindow.Size.Height);

            int newX = Math.Clamp(_dragStartWindowOrigin.X + deltaX, area.X, maxX);
            int newY = Math.Clamp(_dragStartWindowOrigin.Y + deltaY, area.Y, maxY);

            appWindow.Move(new PointInt32(newX, newY));
            e.Handled = true;
        }

        private void OnDragHandlePointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (_dragPointerId != e.Pointer.PointerId)
            {
                return;
            }

            if (sender is FrameworkElement element)
            {
                element.ReleasePointerCapture(e.Pointer);
            }

            bool shouldToggle = !_dragMoved;
            ResetDragState();

            if (shouldToggle)
            {
                ToggleCollapsed();
            }

            e.Handled = true;
        }

        private void OnDragHandlePointerCanceled(object sender, PointerRoutedEventArgs e)
        {
            ResetDragState();
        }

        private void OnDragHandlePointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            ResetDragState();
        }

        private void ResetDragState()
        {
            _dragPointerId = null;
            _dragMoved = false;
        }

        private void ToggleCollapsed()
        {
            _isCollapsed = !_isCollapsed;
            ToolButtonsPanel.Visibility = _isCollapsed ? Visibility.Collapsed : Visibility.Visible;
        }

        private void CloseWindowAfterInitializationFailure()
        {
            try
            {
                Close();
            }
            catch (Exception ex)
            {
                AppLog.Warn("ScreenAnnotation.Interop", "关闭初始化失败的工具栏窗口时发生异常。", ex);
            }
        }

        void IScreenAnnotationModeToolbar.SetSelectedMode(ScreenAnnotationMode mode)
        {
            SetSelectedMode(mode);
        }

        void IScreenAnnotationModeToolbar.EnsureInteractiveTopMost(IScreenAnnotationModeOverlay? overlay)
        {
            EnsureInteractiveTopMost(overlay);
        }

        bool IScreenAnnotationModeToolbar.TryGetWindowHandle(out IntPtr hwnd)
        {
            return TryGetWindowHandle(out hwnd);
        }
    }
}
