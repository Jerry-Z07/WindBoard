using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WindBoard.Localization;
using WindBoard.Logging;

namespace WindBoard.UI.Common
{
    /// <summary>
    /// 通用对话框工具：封装常见的消息弹窗与忙碌弹窗，避免逻辑散落在各个窗口/功能文件中。
    /// </summary>
    internal static class DialogHelpers
    {
        /// <summary>
        /// 显示一个简单的消息对话框（仅“关闭”按钮）。
        /// </summary>
        internal static async Task ShowMessageAsync(XamlRoot xamlRoot, string title, string message)
        {
            if (xamlRoot is null)
            {
                throw new ArgumentNullException(nameof(xamlRoot));
            }

            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = L10n.Get("Common_Close"),
                XamlRoot = xamlRoot,
            };

            await dialog.ShowAsync();
        }

        /// <summary>
        /// 显示“忙碌”对话框并执行指定异步操作。
        /// </summary>
        /// <remarks>
        /// 说明：
        /// - 该方法不会吞掉 <paramref name="action"/> 的异常，调用方应自行捕获并提示用户；
        /// - Hide() 失败不应影响流程：记录日志后忽略。
        /// </remarks>
        internal static async Task RunBusyAsync(XamlRoot xamlRoot, string title, string message, Func<Task> action, string logTag)
        {
            if (xamlRoot is null)
            {
                throw new ArgumentNullException(nameof(xamlRoot));
            }

            if (action is null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            string safeTag = string.IsNullOrWhiteSpace(logTag) ? "UI" : logTag;

            var ring = new ProgressRing
            {
                IsActive = true,
                Width = 32,
                Height = 32,
                HorizontalAlignment = HorizontalAlignment.Center,
            };

            var text = new TextBlock
            {
                Text = message ?? string.Empty,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center,
            };

            var content = new StackPanel { Spacing = 12 };
            content.Children.Add(ring);
            content.Children.Add(text);

            var dialog = new ContentDialog
            {
                Title = title,
                Content = content,
                XamlRoot = xamlRoot,
            };

            var _ = dialog.ShowAsync();
            try
            {
                await action();
            }
            finally
            {
                try
                {
                    dialog.Hide();
                }
                catch (Exception ex)
                {
                    // 忽略关闭失败：业务流程不应因弹窗状态异常而中断。
                    AppLog.Debug(safeTag, $"BusyDialog 关闭失败：title='{title}'", ex);
                }
            }
        }
    }
}

