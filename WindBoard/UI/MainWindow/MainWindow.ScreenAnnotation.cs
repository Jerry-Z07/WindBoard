using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WindBoard.Features.ScreenAnnotation.Models;
using WindBoard.Features.ScreenAnnotation.Services;
using WindBoard.Localization;
using WindBoard.Logging;
using WindBoard.Settings;

namespace WindBoard
{
    public sealed partial class MainWindow
    {
        private ScreenAnnotationFlow? _screenAnnotationFlow;

        private async void OnMinimizeButtonClicked(object sender, RoutedEventArgs e)
        {
            bool enterScreenAnnotationWhenMinimized = AppSettingsService.Instance.GetEnterScreenAnnotationWhenMinimized();
            if (enterScreenAnnotationWhenMinimized)
            {
                await StartScreenAnnotationAsync(source: "minimize", minimizeOwnerWindow: true);
                return;
            }

            MinimizeWindow();
        }

        private async void OnEnterScreenAnnotationClicked(object sender, RoutedEventArgs e)
        {
            await StartScreenAnnotationAsync(source: "more-menu", minimizeOwnerWindow: true);
        }

        private async Task<bool> StartScreenAnnotationAsync(string source, bool minimizeOwnerWindow)
        {
            try
            {
                IntPtr ownerHwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                if (ownerHwnd == IntPtr.Zero)
                {
                    AppLog.Warn("ScreenAnnotation", $"进入屏幕批注失败：主窗口句柄无效，source={source}");
                    await ShowScreenAnnotationStartFailedDialogAsync(L10n.Get("Common_WindowHandleFailed_Message"));
                    return false;
                }

                _screenAnnotationFlow ??= new ScreenAnnotationFlow();

                var options = new ScreenAnnotationStartOptions
                {
                    OwnerWindow = this,
                    OwnerHwnd = ownerHwnd,
                    MinimizeOwnerWindow = minimizeOwnerWindow,
                    Source = source,
                };

                bool started = await _screenAnnotationFlow.StartAsync(options);
                if (started)
                {
                    return true;
                }

                AppLog.Warn("ScreenAnnotation", $"主窗口触发进入屏幕批注失败：source={source}, minimizeOwnerWindow={minimizeOwnerWindow}");
                await ShowScreenAnnotationStartFailedDialogAsync(L10n.Get("Common_UnknownError"));
                return false;
            }
            catch (Exception ex)
            {
                // 关键路径：记录异常细节并提示用户，避免静默失败。
                AppLog.Error("ScreenAnnotation", $"主窗口触发进入屏幕批注异常：source={source}, minimizeOwnerWindow={minimizeOwnerWindow}", ex);
                await ShowScreenAnnotationStartFailedDialogAsync(ex.Message);
                return false;
            }
        }

        private void StopScreenAnnotationForMainWindowClose()
        {
            if (_screenAnnotationFlow is null || !_screenAnnotationFlow.IsRunning)
            {
                return;
            }

            try
            {
                // 主窗口已进入关闭流程：无需恢复或激活主窗口，避免无意义前置。
                _screenAnnotationFlow
                    .StopAsync(restoreOwnerWindow: false, activateOwnerWindow: false)
                    .GetAwaiter()
                    .GetResult();
            }
            catch (Exception ex)
            {
                AppLog.Warn("ScreenAnnotation", "主窗口关闭时停止屏幕批注流程失败。", ex);
            }
        }

        private async Task ShowScreenAnnotationStartFailedDialogAsync(string reason)
        {
            XamlRoot? xamlRoot = TryGetDialogXamlRoot();
            if (xamlRoot is null)
            {
                AppLog.Warn("ScreenAnnotation", $"显示进入屏幕批注失败提示时未拿到 XamlRoot，reason={reason}");
                return;
            }

            var dialog = new ContentDialog
            {
                Title = L10n.Get("MainWindow_ScreenAnnotationStartFailed_Title"),
                Content = L10n.Format("MainWindow_ScreenAnnotationStartFailed_Content_Fmt", reason),
                CloseButtonText = L10n.Get("Common_Close"),
                XamlRoot = xamlRoot,
            };

            await dialog.ShowAsync();
        }
    }
}
