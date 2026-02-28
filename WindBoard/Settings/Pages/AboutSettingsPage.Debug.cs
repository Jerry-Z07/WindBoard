using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Input;
using WindBoard.Logging;

namespace WindBoard.Settings.Pages
{
    public sealed partial class AboutSettingsPage
    {
        private void OnAppIconTapped(object sender, TappedRoutedEventArgs e)
        {
            try
            {
#if DEBUG
                // Debug 构建下调试入口默认显示，无需解锁。
                return;
#else
                if (DebugToolsGate.IsVisible)
                {
                    return;
                }

                if (_debugUnlockTapDetector.RegisterTap(DateTimeOffset.UtcNow))
                {
                    DebugToolsGate.UnlockForSession();
                    ShowDebugUnlockInfo();
                }
#endif
            }
            catch (Exception ex)
            {
                // 防御：隐藏入口的手势检测不应影响 About 页正常使用。
                AppLog.Warn("Debug", "关于页调试入口解锁点击处理失败", ex);
            }
        }

        private void ShowDebugUnlockInfo()
        {
            if (DebugUnlockInfoBar is null)
            {
                return;
            }

            DebugUnlockInfoBar.IsOpen = true;

            // 轻提示：自动关闭，避免占用页面空间。
            int nonce = ++_debugUnlockInfoNonce;
            _ = AutoDismissDebugUnlockInfoBarAsync(nonce);
        }

        private async Task AutoDismissDebugUnlockInfoBarAsync(int nonce)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(4)).ConfigureAwait(false);

                _ = DispatcherQueue.TryEnqueue(() =>
                {
                    // 如果期间又触发过提示（nonce 变化），则不关闭最新提示。
                    if (nonce != _debugUnlockInfoNonce)
                    {
                        return;
                    }

                    try
                    {
                        if (DebugUnlockInfoBar is not null)
                        {
                            DebugUnlockInfoBar.IsOpen = false;
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLog.Warn("Debug", "自动关闭调试解锁提示失败", ex);
                    }
                });
            }
            catch (Exception ex)
            {
                AppLog.Warn("Debug", "调试解锁提示延迟任务失败", ex);
            }
        }
    }
}

