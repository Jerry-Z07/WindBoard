using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.AppNotifications;
using WindBoard.Localization;
using WindBoard.Logging;
using WindBoard.Persistence;
using WindBoard.Reminders;
using Windows.ApplicationModel.DataTransfer;

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

        private void OnOpenLogDirectoryClicked(object sender, RoutedEventArgs e)
        {
            TryOpenFolder(AppLog.LogDirectory, L10n.Get("Settings_Debug_Feedback_OpenedLogDir"));
        }

        private void OnOpenCurrentLogFileClicked(object sender, RoutedEventArgs e)
        {
            string? path = AppLog.CurrentLogFilePath;
            if (string.IsNullOrWhiteSpace(path))
            {
                AppLog.Warn("Debug", "打开当前日志文件失败：当前日志文件路径为空");
                ShowFeedback(InfoBarSeverity.Warning, L10n.Get("Settings_Debug_CurrentLogFileMissing_Message"));
                return;
            }

            TryOpenFile(path, L10n.Get("Settings_Debug_Feedback_OpenedCurrentLogFile"));
        }

        private void OnCopyLogDirectoryClicked(object sender, RoutedEventArgs e)
        {
            TryCopyResolvedPath(AppLog.LogDirectory, "文件夹", L10n.Get("Settings_Debug_Feedback_CopiedLogDir"));
        }

        private void OnOpenSettingsDirectoryClicked(object sender, RoutedEventArgs e)
        {
            string path = AppSettingsService.Instance.SettingsFilePath;
            string directory = Path.GetDirectoryName(path) ?? string.Empty;
            TryOpenFolder(directory, L10n.Get("Settings_Debug_Feedback_OpenedSettingsDir"));
        }

        private void OnOpenSettingsFileClicked(object sender, RoutedEventArgs e)
        {
            string path = AppSettingsService.Instance.SettingsFilePath;
            if (!TryResolveVisiblePath(path, "文件", out string visiblePath))
            {
                return;
            }

            // 存在性判断针对“外部可见路径”：打包形态下友好路径在外部进程的真实视图中并不存在。
            if (!File.Exists(visiblePath))
            {
                AppLog.Warn("Debug", $"打开设置文件失败：文件不存在，path='{visiblePath}'");
                ShowFeedback(InfoBarSeverity.Warning, L10n.Get("Settings_Debug_SettingsFileMissing_Message"));
                return;
            }

            OpenFileWithShell(visiblePath, L10n.Get("Settings_Debug_Feedback_OpenedSettingsFile"));
        }

        private void OnCopySettingsFilePathClicked(object sender, RoutedEventArgs e)
        {
            string path = AppSettingsService.Instance.SettingsFilePath;
            TryCopyResolvedPath(path, "文件", L10n.Get("Settings_Debug_Feedback_CopiedSettingsPath"));
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
        private void TryOpenFolder(string friendlyFolderPath, string successMessage)
        {
            if (!TryResolveVisiblePath(friendlyFolderPath, "文件夹", out string visiblePath))
            {
                return;
            }

            // 存在性判断针对“外部可见路径”：打包形态下友好路径在外部进程的真实视图里并不存在。
            if (!Directory.Exists(visiblePath))
            {
                AppLog.Warn("Debug", $"打开文件夹失败：目录不存在，path='{visiblePath}'");
                ShowFeedback(InfoBarSeverity.Warning, L10n.Format("Settings_Debug_FolderNotFound_Fmt", visiblePath));
                return;
            }

            try
            {
                // 统一走 shell（与 AboutSettingsPage 的打开约定一致）：
                // 打包形态下 WinRT Launcher 会把“重定向前的友好路径”交给资源管理器，
                // 外部进程在真实磁盘上找不到它（系统弹「找不到路径」），而 Launcher 仍返回成功。
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{visiblePath}\"") { UseShellExecute = true });

                AppLog.Info("Debug", $"已打开文件夹：path='{visiblePath}'");
                ShowFeedback(InfoBarSeverity.Success, successMessage);
            }
            catch (Exception ex)
            {
                AppLog.Warn("Debug", $"打开文件夹失败：path='{visiblePath}'", ex);
                ShowFeedback(InfoBarSeverity.Error, L10n.Format("Settings_Debug_ActionFailed_Fmt", ex.Message));
            }
        }

        /// <summary>
        /// 尝试打开文件（默认关联程序）。
        /// </summary>
        private void TryOpenFile(string friendlyFilePath, string successMessage)
        {
            if (!TryResolveVisiblePath(friendlyFilePath, "文件", out string visiblePath))
            {
                return;
            }

            if (!File.Exists(visiblePath))
            {
                AppLog.Warn("Debug", $"打开文件失败：文件不存在，path='{visiblePath}'");
                ShowFeedback(InfoBarSeverity.Warning, L10n.Format("Settings_Debug_FileNotFound_Fmt", visiblePath));
                return;
            }

            OpenFileWithShell(visiblePath, successMessage);
        }

        /// <summary>
        /// 用 shell（默认关联程序）打开已确认存在的文件。
        /// </summary>
        private void OpenFileWithShell(string visiblePath, string successMessage)
        {
            try
            {
                Process.Start(new ProcessStartInfo(visiblePath) { UseShellExecute = true });

                AppLog.Info("Debug", $"已打开文件：path='{visiblePath}'");
                ShowFeedback(InfoBarSeverity.Success, successMessage);
            }
            catch (Exception ex)
            {
                AppLog.Warn("Debug", $"打开文件失败：path='{visiblePath}'", ex);
                ShowFeedback(InfoBarSeverity.Error, L10n.Format("Settings_Debug_ActionFailed_Fmt", ex.Message));
            }
        }

        /// <summary>
        /// 把应用内部使用的友好路径推导为外部进程可见路径。
        /// 失败时给出反馈与日志（打包形态下不得静默回退到外部进程看不到的友好路径）。
        /// </summary>
        private bool TryResolveVisiblePath(string friendlyPath, string target, out string visiblePath)
        {
            visiblePath = string.Empty;

            if (string.IsNullOrWhiteSpace(friendlyPath))
            {
                AppLog.Warn("Debug", $"无法推导外部可见路径，操作已取消：target={target}, path=(空)");
                ShowFeedback(InfoBarSeverity.Warning, L10n.Get("Settings_Debug_PathEmpty_Message"));
                return false;
            }

            if (AppDataVisiblePathResolver.TryResolve(friendlyPath, out visiblePath))
            {
                return true;
            }

            // 提示中带上可核对的路径，避免出现「界面说成功、外部却打不开」的假成功。
            AppLog.Warn("Debug", $"无法推导外部可见路径，操作已取消：target={target}, path='{friendlyPath}'");
            ShowFeedback(InfoBarSeverity.Error, L10n.Format("Settings_Debug_ActionFailed_Fmt", friendlyPath));
            return false;
        }

        /// <summary>
        /// 复制路径到剪贴板。
        /// 说明：打包形态下复制的是外部进程可见路径（与「打开」使用的路径一致）。
        /// </summary>
        private void TryCopyResolvedPath(string friendlyPath, string target, string successMessage)
        {
            if (!TryResolveVisiblePath(friendlyPath, target, out string visiblePath))
            {
                return;
            }

            TryCopyToClipboard(visiblePath, successMessage);
        }

        private void TryCopyToClipboard(string? text, string successMessage)
        {
            try
            {
                string value = text ?? string.Empty;
                var package = new DataPackage();
                package.SetText(value);
                Clipboard.SetContent(package);

                // SetContent 成功即表示内容已进入剪贴板；Flush 只负责让内容在应用退出后仍可用，失败不影响本次复制。
                TryFlushClipboard(value);

                AppLog.Info("Debug", $"已复制路径到剪贴板：value='{value}'");
                ShowFeedback(InfoBarSeverity.Success, successMessage);
            }
            catch (Exception ex)
            {
                AppLog.Warn("Debug", "复制到剪贴板失败", ex);
                ShowFeedback(InfoBarSeverity.Error, L10n.Format("Settings_Debug_ActionFailed_Fmt", ex.Message));
            }
        }

        /// <summary>
        /// 尝试把剪贴板内容持久化（使其在应用退出后仍可用）。
        ///
        /// 失败属非致命：官方语义下 <c>Clipboard.Flush</c> 只负责“让内容在源应用退出后仍可用”，
        /// 而内容在 <c>Clipboard.SetContent</c> 成功时已经进入剪贴板；实测失败码为
        /// <c>0x800401D0</c>（<c>CLIPBRD_E_CANT_OPEN</c>，剪贴板被其它进程瞬时占用）。
        /// 因此这里只记日志，不改写为“操作失败”反馈（避免出现“实际已复制却报失败”的假失败）。
        /// </summary>
        private static void TryFlushClipboard(string value)
        {
            try
            {
                Clipboard.Flush();
            }
            catch (Exception ex)
            {
                AppLog.Warn("Debug", $"剪贴板内容持久化失败（内容已复制，仅应用退出后可能丢失）：value='{value}'", ex);
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
