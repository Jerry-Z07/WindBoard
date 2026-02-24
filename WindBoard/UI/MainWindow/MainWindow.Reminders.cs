using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WindBoard.Logging;
using WindBoard.Reminders;

namespace WindBoard
{
    public sealed partial class MainWindow
    {
        private const int MaxBannerCount = 3;

        internal void ShowInAppBanner(AppReminderMessage message)
        {
            if (message is null)
            {
                return;
            }

            // UI 安全：统一切回 UI 线程操作视觉树。
            if (!DispatcherQueue.TryEnqueue(() => ShowInAppBannerOnUiThread(message)))
            {
                ShowInAppBannerOnUiThread(message);
            }
        }

        private void ShowInAppBannerOnUiThread(AppReminderMessage message)
        {
            if (ReminderBannerStackPanel is null)
            {
                return;
            }

            var bar = new InfoBar
            {
                Title = message.Title ?? string.Empty,
                Message = message.Body ?? string.Empty,
                Severity = MapInfoBarSeverity(message.Severity),
                IsOpen = true,
                IsClosable = true,
                IsIconVisible = true,
            };

            if (message.ClickAction != AppReminderClickAction.None)
            {
                // 说明：
                // - 允许点击弹条触发动作（例如打开数据目录）；
                // - 为避免“点击关闭按钮也触发打开”，这里跳过 Button 触发源（InfoBar 关闭按钮为 Button）。
                bar.Tapped += (_, e) =>
                {
                    try
                    {
                        if (e.OriginalSource is Button)
                        {
                            return;
                        }

                        AppReminderActionExecutor.TryExecute(message.ClickAction);
                    }
                    catch (Exception ex)
                    {
                        AppLog.Warn("Reminders", "处理应用内弹条点击动作失败", ex);
                    }
                };
            }

            // 新消息放到最上方，避免重要提醒被旧消息压住。
            ReminderBannerStackPanel.Children.Insert(0, bar);

            // 限制最多展示数量，避免极端情况下堆叠占满视野。
            while (ReminderBannerStackPanel.Children.Count > MaxBannerCount)
            {
                ReminderBannerStackPanel.Children.RemoveAt(ReminderBannerStackPanel.Children.Count - 1);
            }

            TimeSpan autoDismissDelay = message.Severity switch
            {
                AppReminderSeverity.Error => TimeSpan.FromSeconds(10),
                AppReminderSeverity.Warning => TimeSpan.FromSeconds(8),
                _ => TimeSpan.FromSeconds(6),
            };

            _ = AutoDismissBannerAsync(bar, autoDismissDelay);
        }

        private async Task AutoDismissBannerAsync(InfoBar bar, TimeSpan delay)
        {
            try
            {
                await Task.Delay(delay).ConfigureAwait(false);

                // 延迟后再切回 UI 线程移除控件。
                _ = DispatcherQueue.TryEnqueue(() =>
                {
                    try
                    {
                        bar.IsOpen = false;
                        ReminderBannerStackPanel?.Children.Remove(bar);
                    }
                    catch (Exception ex)
                    {
                        AppLog.Warn("Reminders", "移除应用内弹条失败", ex);
                    }
                });
            }
            catch (Exception ex)
            {
                // 延迟任务本身失败不影响主流程；记录日志便于排查。
                AppLog.Warn("Reminders", "应用内弹条自动关闭任务失败", ex);
            }
        }

        private static InfoBarSeverity MapInfoBarSeverity(AppReminderSeverity severity)
        {
            return severity switch
            {
                AppReminderSeverity.Warning => InfoBarSeverity.Warning,
                AppReminderSeverity.Error => InfoBarSeverity.Error,
                _ => InfoBarSeverity.Informational,
            };
        }
    }
}
