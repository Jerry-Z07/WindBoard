using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using WindBoard.Features.ScreenAnnotation.Interop;
using WindBoard.Features.ScreenAnnotation.Models;
using WindBoard.Features.ScreenAnnotation.UI;
using WindBoard.Logging;

namespace WindBoard.Features.ScreenAnnotation.Services
{
    /// <summary>
    /// 屏幕批注总控流程。
    /// </summary>
    internal sealed class ScreenAnnotationFlow
    {
        private readonly ScreenAnnotationDisplayResolver _displayResolver = new();

        private ScreenAnnotationStartOptions? _options;
        private ScreenAnnotationSessionHost? _sessionHost;
        private ScreenAnnotationWindowState? _windowState;
        private ScreenAnnotationOwnerActivationTracker? _ownerActivationTracker;
        private ScreenAnnotationWindow? _annotationWindow;
        private ScreenAnnotationToolbarWindow? _toolbarWindow;
        private bool _isStopping;

        internal bool IsRunning => _annotationWindow is not null || _toolbarWindow is not null;

        internal async Task<bool> StartAsync(ScreenAnnotationStartOptions options)
        {
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (IsRunning)
            {
                AppLog.Info("ScreenAnnotation", "忽略重复启动请求：屏幕批注已在运行。");
                return true;
            }

            IntPtr ownerHwnd = options.OwnerHwnd != IntPtr.Zero
                ? options.OwnerHwnd
                : ScreenAnnotationWindowInterop.GetWindowHandle(options.OwnerWindow);

            if (ownerHwnd == IntPtr.Zero)
            {
                AppLog.Warn("ScreenAnnotation", "启动失败：无法获取主窗口句柄。");
                return false;
            }

            AppLog.Info("ScreenAnnotation", $"开始进入屏幕批注：source='{options.Source}', minimizeOwner={options.MinimizeOwnerWindow}");

            _options = new ScreenAnnotationStartOptions
            {
                OwnerWindow = options.OwnerWindow,
                OwnerHwnd = ownerHwnd,
                MinimizeOwnerWindow = options.MinimizeOwnerWindow,
                Source = options.Source,
            };
            _windowState = new ScreenAnnotationWindowState();
            _sessionHost = new ScreenAnnotationSessionHost();
            _ownerActivationTracker = new ScreenAnnotationOwnerActivationTracker();

            try
            {
                ScreenAnnotationDisplayTarget displayTarget = _displayResolver.Resolve(ownerHwnd);
                AppLog.Info(
                    "ScreenAnnotation.Display",
                    $"目标显示器：bounds=({displayTarget.Bounds.X},{displayTarget.Bounds.Y},{displayTarget.Bounds.Width},{displayTarget.Bounds.Height}), workArea=({displayTarget.WorkArea.X},{displayTarget.WorkArea.Y},{displayTarget.WorkArea.Width},{displayTarget.WorkArea.Height})");

                _annotationWindow = new ScreenAnnotationWindow(displayTarget, _sessionHost, _windowState);
                _toolbarWindow = new ScreenAnnotationToolbarWindow(displayTarget);

                HookWindowEvents();

                _annotationWindow.Activate();
                _toolbarWindow.Activate();

                ApplyMode(ScreenAnnotationMode.PassThrough);

                if (_options.MinimizeOwnerWindow
                    && !ScreenAnnotationWindowInterop.TryMinimizeWindow(ownerHwnd, out string? minimizeError))
                {
                    AppLog.Warn("ScreenAnnotation", $"最小化主窗口失败：{minimizeError}");
                }

                AppLog.Info("ScreenAnnotation", "进入屏幕批注成功。");
                return true;
            }
            catch (Exception ex)
            {
                AppLog.Error("ScreenAnnotation", "进入屏幕批注失败。", ex);
                await StopAsync(restoreOwnerWindow: true, activateOwnerWindow: true);
                return false;
            }
        }

        internal Task StopAsync(bool restoreOwnerWindow, bool activateOwnerWindow)
        {
            if (_isStopping)
            {
                return Task.CompletedTask;
            }

            _isStopping = true;

            try
            {
                AppLog.Info(
                    "ScreenAnnotation",
                    $"开始退出屏幕批注：restoreOwnerWindow={restoreOwnerWindow}, activateOwnerWindow={activateOwnerWindow}");

                UnhookWindowEvents();

                CloseWindowSafely(_toolbarWindow, "Toolbar");
                CloseWindowSafely(_annotationWindow, "Overlay");

                _toolbarWindow = null;
                _annotationWindow = null;
                _sessionHost = null;
                _windowState = null;
                _ownerActivationTracker = null;

                if (_options is ScreenAnnotationStartOptions options)
                {
                    if (restoreOwnerWindow)
                    {
                        if (!ScreenAnnotationWindowInterop.TryRestoreWindow(options.OwnerHwnd, out string? restoreError))
                        {
                            AppLog.Warn("ScreenAnnotation", $"恢复主窗口失败：{restoreError}");
                        }
                    }

                    if (activateOwnerWindow)
                    {
                        try
                        {
                            options.OwnerWindow.Activate();
                        }
                        catch (Exception ex)
                        {
                            AppLog.Warn("ScreenAnnotation", "激活主窗口失败。", ex);
                        }

                        if (!ScreenAnnotationWindowInterop.TryActivateWindow(options.OwnerHwnd, out string? activateError))
                        {
                            AppLog.Warn("ScreenAnnotation", $"前置主窗口失败：{activateError}");
                        }
                    }
                }

                _options = null;
                AppLog.Info("ScreenAnnotation", "退出屏幕批注完成。");
            }
            finally
            {
                _isStopping = false;
            }

            return Task.CompletedTask;
        }

        private void HookWindowEvents()
        {
            if (_options is ScreenAnnotationStartOptions options)
            {
                options.OwnerWindow.Activated += OnOwnerWindowActivated;
            }

            if (_toolbarWindow is not null)
            {
                _toolbarWindow.ModeRequested += OnToolbarModeRequested;
                _toolbarWindow.ReturnToAppRequested += OnReturnToAppRequested;
                _toolbarWindow.Closed += OnManagedWindowClosed;
            }

            if (_annotationWindow is not null)
            {
                _annotationWindow.OverlayActivated += OnAnnotationWindowActivated;
                _annotationWindow.OverlayWindowPositionChanged += OnAnnotationWindowPositionChanged;
                _annotationWindow.Closed += OnManagedWindowClosed;
            }
        }

        private void UnhookWindowEvents()
        {
            if (_options is ScreenAnnotationStartOptions options)
            {
                options.OwnerWindow.Activated -= OnOwnerWindowActivated;
            }

            if (_toolbarWindow is not null)
            {
                _toolbarWindow.ModeRequested -= OnToolbarModeRequested;
                _toolbarWindow.ReturnToAppRequested -= OnReturnToAppRequested;
                _toolbarWindow.Closed -= OnManagedWindowClosed;
            }

            if (_annotationWindow is not null)
            {
                _annotationWindow.OverlayActivated -= OnAnnotationWindowActivated;
                _annotationWindow.OverlayWindowPositionChanged -= OnAnnotationWindowPositionChanged;
                _annotationWindow.Closed -= OnManagedWindowClosed;
            }
        }

        private void ApplyMode(ScreenAnnotationMode mode)
        {
            ScreenAnnotationToolbarInteractivityCoordinator.ApplyMode(
                mode,
                _windowState,
                _annotationWindow,
                _toolbarWindow);

            AppLog.Info("ScreenAnnotation", $"模式切换：mode={mode}");
        }

        private void OnToolbarModeRequested(object? sender, ScreenAnnotationMode mode)
        {
            try
            {
                ApplyMode(mode);
            }
            catch (Exception ex)
            {
                AppLog.Warn("ScreenAnnotation", $"模式切换失败：mode={mode}", ex);
            }
        }

        private async void OnReturnToAppRequested(object? sender, EventArgs e)
        {
            await StopAsync(restoreOwnerWindow: true, activateOwnerWindow: true);
        }

        private async void OnOwnerWindowActivated(object sender, WindowActivatedEventArgs args)
        {
            if (_isStopping)
            {
                return;
            }

            ScreenAnnotationOwnerActivationTracker? tracker = _ownerActivationTracker;
            if (tracker is null)
            {
                return;
            }

            bool shouldStop = tracker.Observe(args.WindowActivationState);
            if (!shouldStop)
            {
                return;
            }

            AppLog.Info(
                "ScreenAnnotation",
                $"检测到主窗口重新激活，开始退出屏幕批注：state={args.WindowActivationState}");
            await StopAsync(restoreOwnerWindow: false, activateOwnerWindow: false);
        }

        private async void OnManagedWindowClosed(object sender, WindowEventArgs args)
        {
            if (_isStopping)
            {
                return;
            }

            AppLog.Warn("ScreenAnnotation", "检测到桌面批注窗口被关闭，开始执行回收流程。");
            await StopAsync(restoreOwnerWindow: true, activateOwnerWindow: true);
        }

        private void OnAnnotationWindowActivated(object? sender, EventArgs e)
        {
            if (_isStopping)
            {
                return;
            }

            try
            {
                // 批注层在书写过程中可能重新跃升到顶层，这里立即把工具栏重新抬回最上面。
                ScreenAnnotationToolbarInteractivityCoordinator.EnsureToolbarInteractiveAfterOverlayActivation(
                    _annotationWindow,
                    _toolbarWindow);
            }
            catch (Exception ex)
            {
                AppLog.Warn("ScreenAnnotation", "批注层重新激活后刷新工具栏层级失败。", ex);
            }
        }

        private void OnAnnotationWindowPositionChanged(object? sender, ScreenAnnotationOverlayWindowPositionChangedEventArgs e)
        {
            if (_isStopping)
            {
                return;
            }

            try
            {
                ScreenAnnotationToolbarInteractivityCoordinator.EnsureToolbarInteractiveAfterOverlayWindowPositionChanged(
                    _annotationWindow,
                    _toolbarWindow,
                    e.InsertAfterHwnd,
                    e.WindowPosFlags);
            }
            catch (Exception ex)
            {
                AppLog.Warn("ScreenAnnotation", "批注层窗口顺序变化后刷新工具栏层级失败。", ex);
            }
        }

        private static void CloseWindowSafely(Window? window, string name)
        {
            if (window is null)
            {
                return;
            }

            try
            {
                window.Close();
            }
            catch (Exception ex)
            {
                AppLog.Warn("ScreenAnnotation", $"关闭桌面批注窗口失败：name={name}", ex);
            }
        }
    }
}
