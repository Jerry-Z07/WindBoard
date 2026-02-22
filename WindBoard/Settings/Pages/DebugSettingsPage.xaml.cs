using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.AppNotifications;
using WindBoard.Localization;
using WindBoard.Logging;
using WindBoard.Reminders;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;

namespace WindBoard.Settings.Pages
{
    public sealed partial class DebugSettingsPage : Page
    {
        public DebugSettingsPage()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
#if DEBUG
            // Debug 构建下入口默认显示，这里隐藏“会话控制”，避免用户误以为可锁回去。
            SessionSection.Visibility = Visibility.Collapsed;
#else
            SessionSection.Visibility = Visibility.Visible;
#endif
        }

        private void ShowFeedback(InfoBarSeverity severity, string message)
        {
            if (ActionFeedbackBar is null)
            {
                return;
            }

            ActionFeedbackBar.Severity = severity;
            ActionFeedbackBar.Message = message ?? string.Empty;
            ActionFeedbackBar.IsOpen = true;
        }

        private async void OnOpenLogDirectoryClicked(object sender, RoutedEventArgs e)
        {
            await TryOpenFolderAsync(AppLog.LogDirectory, L10n.Get("Settings_Debug_Feedback_OpenedLogDir"));
        }

        private async void OnOpenCurrentLogFileClicked(object sender, RoutedEventArgs e)
        {
            string? path = AppLog.CurrentLogFilePath;
            if (string.IsNullOrWhiteSpace(path))
            {
                ShowFeedback(InfoBarSeverity.Warning, L10n.Get("Settings_Debug_CurrentLogFileMissing_Message"));
                return;
            }

            await TryOpenFileAsync(path, L10n.Get("Settings_Debug_Feedback_OpenedCurrentLogFile"));
        }

        private void OnCopyLogDirectoryClicked(object sender, RoutedEventArgs e)
        {
            TryCopyToClipboard(AppLog.LogDirectory, L10n.Get("Settings_Debug_Feedback_CopiedLogDir"));
        }

        private async void OnOpenSettingsDirectoryClicked(object sender, RoutedEventArgs e)
        {
            string path = AppSettingsService.Instance.SettingsFilePath;
            string directory = Path.GetDirectoryName(path) ?? string.Empty;
            await TryOpenFolderAsync(directory, L10n.Get("Settings_Debug_Feedback_OpenedSettingsDir"));
        }

        private async void OnOpenSettingsFileClicked(object sender, RoutedEventArgs e)
        {
            string path = AppSettingsService.Instance.SettingsFilePath;
            if (!File.Exists(path))
            {
                ShowFeedback(InfoBarSeverity.Warning, L10n.Get("Settings_Debug_SettingsFileMissing_Message"));
                return;
            }

            await TryOpenFileAsync(path, L10n.Get("Settings_Debug_Feedback_OpenedSettingsFile"));
        }

        private void OnCopySettingsFilePathClicked(object sender, RoutedEventArgs e)
        {
            string path = AppSettingsService.Instance.SettingsFilePath;
            TryCopyToClipboard(path, L10n.Get("Settings_Debug_Feedback_CopiedSettingsPath"));
        }

        private void OnHideDebugEntryThisSessionClicked(object sender, RoutedEventArgs e)
        {
            try
            {
                DebugToolsGate.LockForSession();
            }
            catch (Exception ex)
            {
                AppLog.Warn("Debug", "隐藏调试入口失败（仅本次会话）", ex);
                ShowFeedback(InfoBarSeverity.Error, L10n.Format("Settings_Debug_ActionFailed_Fmt", ex.Message));
            }
        }

        private void OnSendTestToastClicked(object sender, RoutedEventArgs e)
        {
            try
            {
                string title = EscapeXml(L10n.Format("Settings_Debug_TestToast_Title", global::WindBoard.AppDisplayName.Get()));
                string body = EscapeXml(L10n.Get("Settings_Debug_TestToast_Body"));

                // 最小 Toast XML：标题 + 内容（与应用内提醒通道保持一致，便于排查通知通道问题）。
                string xml = $"""
                              <toast>
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

                AppLog.Info("Debug", "已发送测试 Windows Toast");
                ShowFeedback(InfoBarSeverity.Success, L10n.Get("Settings_Debug_Feedback_SentToast"));
            }
            catch (Exception ex)
            {
                AppLog.Warn("Debug", "发送测试 Windows Toast 失败", ex);
                ShowFeedback(InfoBarSeverity.Error, L10n.Format("Settings_Debug_ActionFailed_Fmt", ex.Message));
            }
        }

        private void OnShowTestInAppBannerClicked(object sender, RoutedEventArgs e)
        {
            try
            {
                // 说明：应用内弹条目前仅由 MainWindow 承载（右上角 InfoBar 栈）。
                // 调试页运行在 SettingsWindow 内，因此需要从 App 拿到主窗口引用。
                var app = Application.Current as global::WindBoard.App;
                global::WindBoard.MainWindow? mainWindow = app?.TryGetMainWindow();
                if (mainWindow is null)
                {
                    AppLog.Warn("Debug", "展示测试应用内弹条失败：未找到 MainWindow");
                    ShowFeedback(InfoBarSeverity.Warning, L10n.Get("Settings_Debug_MainWindowMissing_Message"));
                    return;
                }

                mainWindow.ShowInAppBanner(
                    new AppReminderMessage
                    {
                        Title = L10n.Format("Settings_Debug_TestToast_Title", global::WindBoard.AppDisplayName.Get()),
                        Body = L10n.Get("Settings_Debug_TestToast_Body"),
                        Severity = AppReminderSeverity.Info,
                    });

                AppLog.Info("Debug", "已展示测试应用内弹条");
                ShowFeedback(InfoBarSeverity.Success, L10n.Get("Settings_Debug_Feedback_ShownBanner"));
            }
            catch (Exception ex)
            {
                AppLog.Warn("Debug", "展示测试应用内弹条失败", ex);
                ShowFeedback(InfoBarSeverity.Error, L10n.Format("Settings_Debug_ActionFailed_Fmt", ex.Message));
            }
        }

        private async void OnCrashWinUiClicked(object sender, RoutedEventArgs e)
        {
            if (!await ConfirmCrashAsync(scenario: "WinUIUnhandledException").ConfigureAwait(true))
            {
                return;
            }

            // 注意：此处故意抛出未处理异常，用于验证 WinUI UnhandledException 链路。
            // 不要 try/catch，否则会吞掉异常导致测试无效。
            AppLog.Critical("Debug", "用户触发崩溃测试：WinUI UI 线程未处理异常");
            throw new InvalidOperationException("Debug crash test: WinUI UnhandledException");
        }

        private async void OnCrashAppDomainClicked(object sender, RoutedEventArgs e)
        {
            if (!await ConfirmCrashAsync(scenario: "AppDomainUnhandledException").ConfigureAwait(true))
            {
                return;
            }

            // 注意：此处故意在新线程抛出未处理异常，用于验证 AppDomain.UnhandledException 链路。
            // 不要 try/catch，否则会吞掉异常导致测试无效。
            AppLog.Critical("Debug", "用户触发崩溃测试：AppDomain 后台线程未处理异常");

            var thread = new Thread(() =>
            {
                throw new InvalidOperationException("Debug crash test: AppDomain UnhandledException");
            })
            {
                IsBackground = true,
                Name = "WindBoard.DebugCrashTest",
            };

            thread.Start();
        }

        /// <summary>
        /// 崩溃测试强确认：要求用户输入 CRASH 才能继续。
        /// 说明：崩溃测试会写入崩溃报告并退出进程，必须防误触。
        /// </summary>
        private async Task<bool> ConfirmCrashAsync(string scenario)
        {
            try
            {
                Microsoft.UI.Xaml.XamlRoot? xamlRoot = TryGetDialogXamlRoot();
                if (xamlRoot is null)
                {
                    AppLog.Warn("Debug", $"展示崩溃确认对话框失败：XamlRoot 为空，scenario='{scenario}'");
                    ShowFeedback(InfoBarSeverity.Warning, L10n.Get("Settings_Debug_CrashDialogUnavailable_Message"));
                    return false;
                }

                var body = new TextBlock
                {
                    Text = L10n.Get("Settings_Debug_CrashConfirm_Body"),
                    TextWrapping = TextWrapping.Wrap,
                };

                var input = new TextBox
                {
                    PlaceholderText = L10n.Get("Settings_Debug_CrashConfirm_Placeholder"),
                };

                var content = new StackPanel { Spacing = 10 };
                content.Children.Add(body);
                content.Children.Add(input);

                var dialog = new ContentDialog
                {
                    Title = L10n.Get("Settings_Debug_CrashConfirm_Title"),
                    Content = content,
                    PrimaryButtonText = L10n.Get("Common_Continue"),
                    CloseButtonText = L10n.Get("Common_Cancel"),
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = xamlRoot,
                };

                dialog.IsPrimaryButtonEnabled = false;
                input.TextChanged += (_, _) =>
                {
                    dialog.IsPrimaryButtonEnabled = IsCrashConfirmTextMatched(input.Text);
                };

                ContentDialogResult result = await dialog.ShowAsync();
                return result == ContentDialogResult.Primary;
            }
            catch (Exception ex)
            {
                AppLog.Warn("Debug", $"展示崩溃确认对话框失败：scenario='{scenario}'", ex);
                ShowFeedback(InfoBarSeverity.Error, L10n.Format("Settings_Debug_ActionFailed_Fmt", ex.Message));
                return false;
            }
        }

        private Microsoft.UI.Xaml.XamlRoot? TryGetDialogXamlRoot()
        {
            try
            {
                if (ActionFeedbackBar?.XamlRoot is not null)
                {
                    return ActionFeedbackBar.XamlRoot;
                }

                if (Content is FrameworkElement root && root.XamlRoot is not null)
                {
                    return root.XamlRoot;
                }
            }
            catch
            {
                // 忽略：兜底返回 null
            }

            return null;
        }

        private static bool IsCrashConfirmTextMatched(string? text)
        {
            // 说明：Trim + 不区分大小写，避免用户输入 CRASH（大小写/前后空格）导致误判。
            return string.Equals((text ?? string.Empty).Trim(), "CRASH", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 尝试打开文件夹（资源管理器）。
        /// 说明：该功能属于“调试辅助”，失败不应阻断 UI，仅做提示与日志记录。
        /// </summary>
        private async Task TryOpenFolderAsync(string folderPath, string successMessage)
        {
            string path = (folderPath ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(path))
            {
                ShowFeedback(InfoBarSeverity.Warning, L10n.Get("Settings_Debug_PathEmpty_Message"));
                return;
            }

            if (!Directory.Exists(path))
            {
                ShowFeedback(InfoBarSeverity.Warning, L10n.Format("Settings_Debug_FolderNotFound_Fmt", path));
                return;
            }

            try
            {
                StorageFolder folder = await StorageFolder.GetFolderFromPathAsync(path).AsTask().ConfigureAwait(true);
                bool launched = await Launcher.LaunchFolderAsync(folder).AsTask().ConfigureAwait(true);
                if (!launched)
                {
                    ShowFeedback(InfoBarSeverity.Warning, L10n.Get("Settings_Debug_LaunchFailed_Message"));
                    return;
                }

                ShowFeedback(InfoBarSeverity.Success, successMessage);
            }
            catch (Exception ex)
            {
                AppLog.Warn("Debug", $"打开文件夹失败：path='{path}'", ex);
                ShowFeedback(InfoBarSeverity.Error, L10n.Format("Settings_Debug_ActionFailed_Fmt", ex.Message));
            }
        }

        /// <summary>
        /// 尝试打开文件（默认关联程序）。
        /// </summary>
        private async Task TryOpenFileAsync(string filePath, string successMessage)
        {
            string path = (filePath ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(path))
            {
                ShowFeedback(InfoBarSeverity.Warning, L10n.Get("Settings_Debug_PathEmpty_Message"));
                return;
            }

            if (!File.Exists(path))
            {
                ShowFeedback(InfoBarSeverity.Warning, L10n.Format("Settings_Debug_FileNotFound_Fmt", path));
                return;
            }

            try
            {
                StorageFile file = await StorageFile.GetFileFromPathAsync(path).AsTask().ConfigureAwait(true);
                bool launched = await Launcher.LaunchFileAsync(file).AsTask().ConfigureAwait(true);
                if (!launched)
                {
                    ShowFeedback(InfoBarSeverity.Warning, L10n.Get("Settings_Debug_LaunchFailed_Message"));
                    return;
                }

                ShowFeedback(InfoBarSeverity.Success, successMessage);
            }
            catch (Exception ex)
            {
                AppLog.Warn("Debug", $"打开文件失败：path='{path}'", ex);
                ShowFeedback(InfoBarSeverity.Error, L10n.Format("Settings_Debug_ActionFailed_Fmt", ex.Message));
            }
        }

        private void TryCopyToClipboard(string? text, string successMessage)
        {
            try
            {
                string value = text ?? string.Empty;
                var package = new DataPackage();
                package.SetText(value);
                Clipboard.SetContent(package);
                Clipboard.Flush();
                ShowFeedback(InfoBarSeverity.Success, successMessage);
            }
            catch (Exception ex)
            {
                AppLog.Warn("Debug", "复制到剪贴板失败", ex);
                ShowFeedback(InfoBarSeverity.Error, L10n.Format("Settings_Debug_ActionFailed_Fmt", ex.Message));
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
