using System;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppNotifications;

namespace WindBoard.Reminders.Channels
{
    /// <summary>
    /// Windows 通知（Toast）提醒通道（窗口化优先）。
    /// </summary>
    internal sealed class WindowsToastReminderChannel : IAppReminderChannel
    {
        public bool TryShow(Window window, AppReminderMessage message, out Exception? error)
        {
            error = null;

            try
            {
                string title = EscapeXml(message?.Title ?? string.Empty);
                string body = EscapeXml(message?.Body ?? string.Empty);

                // 最小 Toast XML：标题 + 内容。
                // 说明：
                // - ClickAction 不为空时，通过 toast.launch 携带参数；用户点击通知即可触发动作。
                // - Windows 通知是否可用由 AppReminderService 控制；失败会降级为应用内弹条。
                string? launch = AppReminderToastArguments.BuildLaunchArgument(message?.ClickAction ?? AppReminderClickAction.None);
                string launchAttr = string.IsNullOrWhiteSpace(launch) ? string.Empty : $" launch=\"{EscapeXml(launch)}\"";

                string xml = $"""
                              <toast{launchAttr}>
                                <visual>
                                  <binding template="ToastGeneric">
                                    <text>{title}</text>
                                    <text>{body}</text>
                                  </binding>
                                </visual>
                              </toast>
                              """;

                var notification = new AppNotification(xml);
                AppNotificationManager.Default.Show(notification);
                return true;
            }
            catch (Exception ex)
            {
                error = ex;
                return false;
            }
        }

        private static string EscapeXml(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            // 仅用于 Toast XML：做最小转义避免 XML 解析失败。
            return text
                .Replace("&", "&amp;", StringComparison.Ordinal)
                .Replace("<", "&lt;", StringComparison.Ordinal)
                .Replace(">", "&gt;", StringComparison.Ordinal)
                .Replace("\"", "&quot;", StringComparison.Ordinal)
                .Replace("'", "&apos;", StringComparison.Ordinal);
        }
    }
}
