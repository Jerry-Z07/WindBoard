using System;
using System.Threading;
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
            await ShowMessageAsync(xamlRoot, title, message, closeButtonText: null);
        }

        /// <summary>
        /// 显示一个简单的消息对话框（仅单按钮，可自定义按钮文案）。
        /// </summary>
        internal static async Task ShowMessageAsync(XamlRoot xamlRoot, string title, string message, string? closeButtonText)
        {
            ArgumentNullException.ThrowIfNull(xamlRoot);

            string buttonText = string.IsNullOrWhiteSpace(closeButtonText)
                ? L10n.Get("Common_Close")
                : closeButtonText;

            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = buttonText,
                XamlRoot = xamlRoot,
            };

            await dialog.ShowAsync();
        }

        /// <summary>
        /// 显示确认对话框，返回用户是否选择了主按钮。
        /// </summary>
        internal static async Task<bool> ShowConfirmAsync(XamlRoot xamlRoot, string title, string message, string primaryButtonText, string closeButtonText)
        {
            ArgumentNullException.ThrowIfNull(xamlRoot);

            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                PrimaryButtonText = primaryButtonText,
                CloseButtonText = closeButtonText,
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = xamlRoot,
            };

            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }

        /// <summary>
        /// 显示“忙碌”对话框并执行指定异步操作，用户可通过关闭按钮请求取消。
        /// </summary>
        /// <remarks>
        /// 该方法不会吞掉 <paramref name="action"/> 的异常，调用方应自行捕获并提示用户；Hide() 失败不应影响流程，记录日志后忽略。
        /// 取消请求通过 <see cref="CancellationToken"/> 传递，对话框在 <paramref name="action"/> 真正停下后才关闭。
        /// </remarks>
        internal static async Task RunBusyAsync(XamlRoot xamlRoot, string title, string message, Func<CancellationToken, Task> action, string logTag)
        {
            ArgumentNullException.ThrowIfNull(xamlRoot);

            ArgumentNullException.ThrowIfNull(action);

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
                CloseButtonText = L10n.Get("Common_Cancel"),
                XamlRoot = xamlRoot,
            };

            using var cancellation = new CancellationTokenSource();

            dialog.CloseButtonClick += (_, args) =>
            {
                // 取消是请求：先让 action 观察 token，等它返回后由 finally 关闭对话框，避免界面已关闭但仍在写文件。
                args.Cancel = true;

                if (cancellation.IsCancellationRequested)
                {
                    return;
                }

                cancellation.Cancel();
                dialog.CloseButtonText = L10n.Get("Common_Canceling");
            };

            var _ = dialog.ShowAsync();
            try
            {
                await action(cancellation.Token);
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
