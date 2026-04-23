using System;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WindBoard.Localization;
using WindBoard.Logging;
using WindBoard.Settings;

namespace WindBoard
{
    public sealed partial class MainWindow : Window
    {
        private bool _hasAppliedStartupWindowMode;

        private void ConfigureTitleBar()
        {
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(MainTitleBar);

            _appWindowTitleBar = AppWindow.TitleBar;
            _appWindowTitleBar.ButtonBackgroundColor = Colors.Transparent;
            _appWindowTitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        }

        private void SyncTitleBarVisibilityFromWindowState()
        {
            AppWindow? appWindow = TryGetAppWindow();
            if (appWindow is null)
            {
                return;
            }

            MainTitleBar.Visibility = appWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private void OnMoreMenuOpening(object sender, object e)
        {
            // “更多”菜单每次打开都同步一次当前窗口全屏状态，避免临时开关状态滞后。
            SyncTemporaryFullScreenMenuItemFromWindowState();

            // 设置为“最小化进入屏幕批注”时，不重复展示菜单入口；关闭该设置时再显示显式入口。
            bool enterScreenAnnotationWhenMinimized = AppSettingsService.Instance.GetEnterScreenAnnotationWhenMinimized();
            if (ScreenAnnotationMenuFlyoutItem is not null)
            {
                ScreenAnnotationMenuFlyoutItem.Visibility = enterScreenAnnotationWhenMinimized
                    ? Visibility.Collapsed
                    : Visibility.Visible;
            }
        }

        private void OnTemporaryFullScreenMenuItemClicked(object sender, RoutedEventArgs e)
        {
            AppWindow? appWindow = TryGetAppWindow();
            if (appWindow is null)
            {
                // 极端兜底：窗口句柄获取失败时不做切换，并禁用菜单项避免用户连续点击无响应。
                AppLog.Warn("WindowMode", "切换全屏失败：无法获取 AppWindow（窗口句柄可能尚未就绪）");
                SyncTemporaryFullScreenMenuItemFromWindowState();
                return;
            }

            bool currentIsFullScreen;
            try
            {
                currentIsFullScreen = appWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen;
            }
            catch (Exception ex)
            {
                // 判定失败不应影响主流程：记录日志便于排查，并保持菜单项展示不动。
                AppLog.Warn("WindowMode", "读取窗口全屏状态失败", ex);
                return;
            }

            bool targetFullScreen = !currentIsFullScreen;
            AppLog.Info("WindowMode", $"临时切换窗口全屏：currentIsFullScreen={currentIsFullScreen}, targetFullScreen={targetFullScreen}");

            TrySetWindowFullScreen(targetFullScreen);

            // 切换完成后再读一次真实状态，确保菜单文字/图标与窗口一致（避免 SetPresenter 失败时“假状态”）。
            SyncTemporaryFullScreenMenuItemFromWindowState();
        }

        /// <summary>
        /// 启动后按设置应用一次窗口形态（可能需要等待窗口句柄就绪，因此挂到 Activated 并允许重试）。
        /// </summary>
        private void TryApplyStartupWindowModeIfNeeded()
        {
            if (_hasAppliedStartupWindowMode)
            {
                return;
            }

            AppWindow? appWindow = TryGetAppWindow();
            if (appWindow is null)
            {
                // 窗口句柄尚未就绪：等待下一次 Activated 再尝试。
                return;
            }

            StartupWindowMode mode = AppSettingsService.Instance.GetStartupWindowMode();
            bool targetFullScreen = mode == StartupWindowMode.FullScreen;
            bool isFullScreen = appWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen;

            if (targetFullScreen != isFullScreen)
            {
                AppLog.Info("WindowMode", $"启动时应用窗口形态：mode={mode}, currentIsFullScreen={isFullScreen}");
                if (!TrySetWindowFullScreen(targetFullScreen))
                {
                    // 应用失败时不标记完成：允许后续重试（避免极端环境句柄变化/异常导致设置永远不生效）。
                    return;
                }
            }

            _hasAppliedStartupWindowMode = true;
            SyncTitleBarVisibilityFromWindowState();
        }

        private void SyncTemporaryFullScreenMenuItemFromWindowState()
        {
            if (TemporaryFullScreenMenuFlyoutItem is null)
            {
                return;
            }

            AppWindow? appWindow = TryGetAppWindow();
            if (appWindow is null)
            {
                // 极端兜底：窗口句柄获取失败时禁用该入口，避免用户点击无响应。
                TemporaryFullScreenMenuFlyoutItem.IsEnabled = false;
                TemporaryFullScreenMenuFlyoutItem.Text = L10n.Get("Common_FullScreen");
                if (TemporaryFullScreenMenuIcon is not null)
                {
                    TemporaryFullScreenMenuIcon.Symbol = Symbol.FullScreen;
                }

                return;
            }

            bool isFullScreen;
            try
            {
                isFullScreen = appWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen;
            }
            catch (Exception ex)
            {
                // 判定失败不应影响主流程：记录日志便于排查，并保持原 UI 状态不动。
                AppLog.Warn("WindowMode", "读取窗口全屏状态失败", ex);
                return;
            }

            TemporaryFullScreenMenuFlyoutItem.IsEnabled = true;

            // 取消勾选区：用“文字 + 图标”表达当前状态，避免 ToggleMenuFlyoutItem 带来的额外左侧留白。
            if (isFullScreen)
            {
                TemporaryFullScreenMenuFlyoutItem.Text = L10n.Get("Common_ExitFullScreen");
                if (TemporaryFullScreenMenuIcon is not null)
                {
                    TemporaryFullScreenMenuIcon.Symbol = Symbol.BackToWindow;
                }
            }
            else
            {
                TemporaryFullScreenMenuFlyoutItem.Text = L10n.Get("Common_FullScreen");
                if (TemporaryFullScreenMenuIcon is not null)
                {
                    TemporaryFullScreenMenuIcon.Symbol = Symbol.FullScreen;
                }
            }
        }

        private bool TrySetWindowFullScreen(bool fullScreen)
        {
            AppWindow? appWindow = TryGetAppWindow();
            if (appWindow is null)
            {
                AppLog.Warn("WindowMode", "切换全屏失败：无法获取 AppWindow（窗口句柄可能尚未就绪）");
                return false;
            }

            try
            {
                bool current = appWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen;
                if (current == fullScreen)
                {
                    return true;
                }

                // Windowed：回到 Overlapped Presenter（窗口化）；FullScreen：进入全屏 Presenter。
                appWindow.SetPresenter(fullScreen ? AppWindowPresenterKind.FullScreen : AppWindowPresenterKind.Overlapped);
                SyncTitleBarVisibilityFromWindowState();
                return true;
            }
            catch (Exception ex)
            {
                AppLog.Warn("WindowMode", $"切换全屏失败：fullScreen={fullScreen}", ex);
                return false;
            }
        }

        private AppWindow? TryGetAppWindow()
        {
            try
            {
                IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                if (hwnd == IntPtr.Zero)
                {
                    return null;
                }

                var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
                return AppWindow.GetFromWindowId(windowId);
            }
            catch
            {
                return null;
            }
        }
    }
}
