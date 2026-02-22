using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using WindBoard.Localization;
using WindBoard.Logging;
using WindBoard.Reminders;

namespace WindBoard.Errors
{
    /// <summary>
    /// 应用统一错误处理中心：
    /// - 未处理异常：落盘崩溃报告 +（UI 线程尽力）弹出崩溃对话框并退出；
    /// - 已捕获异常：统一记录日志，并可选展示用户提示（提醒一次）。
    /// </summary>
    internal sealed class AppErrorService
    {
        private readonly object _gate = new();
        private global::WindBoard.MainWindow? _mainWindow;

        // 防御：避免崩溃处理重入（可能出现异常连锁/重复触发）。
        private int _winUiCrashHandlingStarted;
        private int _appDomainCrashHandlingStarted;

        internal static AppErrorService Instance { get; } = new();

        private AppErrorService()
        {
        }

        internal void Initialize(global::WindBoard.MainWindow mainWindow)
        {
            if (mainWindow is null)
            {
                throw new ArgumentNullException(nameof(mainWindow));
            }

            lock (_gate)
            {
                _mainWindow = mainWindow;
            }
        }

        internal void HandleWinUiUnhandledException(Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            if (e is null)
            {
                return;
            }

            // 必须尽早标记 Handled：否则可能直接被宿主终止，无法写报告/弹窗。
            try
            {
                e.Handled = true;
            }
            catch
            {
                // 忽略：不同运行时/配置下可能不允许设置
            }

            if (Interlocked.CompareExchange(ref _winUiCrashHandlingStarted, 1, 0) != 0)
            {
                return;
            }

            Exception? ex = null;
            try
            {
                ex = e.Exception;
            }
            catch
            {
                ex = null;
            }

            bool wrote = AppCrashReportStore.TryWriteCrashReport(
                AppCrashSource.WinUIUnhandledException,
                ex,
                exceptionObject: null,
                isTerminating: null,
                out AppCrashReport report,
                out Exception? writeError);

            try
            {
                if (wrote)
                {
                    AppLog.Critical("App", $"WinUI UnhandledException（已写崩溃报告）：report='{report.ReportFilePath}'", ex);
                }
                else
                {
                    AppLog.Critical("App", "WinUI UnhandledException（写崩溃报告失败）", writeError ?? ex);
                }
            }
            catch
            {
                // 兜底：全局异常处理本身不能再抛异常
            }

            // UI 线程尽力弹窗；无论弹窗成功与否，最终都退出进程，避免处于不一致状态继续运行。
            TryShowCrashDialogAndExit(wrote ? report : null);
        }

        internal void HandleAppDomainUnhandledException(System.UnhandledExceptionEventArgs e)
        {
            if (e is null)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref _appDomainCrashHandlingStarted, 1, 0) != 0)
            {
                return;
            }

            Exception? ex = e.ExceptionObject as Exception;
            object? exceptionObject = e.ExceptionObject;
            bool isTerminating = e.IsTerminating;

            bool wrote = AppCrashReportStore.TryWriteCrashReport(
                AppCrashSource.AppDomainUnhandledException,
                ex,
                exceptionObject,
                isTerminating,
                out AppCrashReport report,
                out Exception? writeError);

            try
            {
                if (wrote)
                {
                    AppLog.Critical("App", $"AppDomain UnhandledException（已写崩溃报告）：isTerminating={isTerminating}, report='{report.ReportFilePath}'", ex);
                }
                else if (ex is not null)
                {
                    AppLog.Critical("App", $"AppDomain UnhandledException（写崩溃报告失败）：isTerminating={isTerminating}", writeError ?? ex);
                }
                else
                {
                    AppLog.Critical("App", $"AppDomain UnhandledException（写崩溃报告失败）：isTerminating={isTerminating}, exceptionObjectType={exceptionObject?.GetType().FullName ?? "(null)"}", writeError);
                }
            }
            catch
            {
                // 兜底：全局异常处理本身不能再抛异常
            }
        }

        internal void HandleUnobservedTaskException(UnobservedTaskExceptionEventArgs e)
        {
            if (e is null)
            {
                return;
            }

            try
            {
                AppLog.Error("App", "TaskScheduler.UnobservedTaskException", e.Exception);

                // 标记已观察：避免宿主将其升级为进程级异常（不同运行时/配置下行为可能不同）。
                e.SetObserved();
            }
            catch
            {
                // 兜底：全局异常处理本身不能再抛异常
            }
        }

        internal void ReportHandledException(string category, string message, Exception ex, AppErrorUserPrompt? prompt = null)
        {
            if (ex is null)
            {
                return;
            }

            string c = string.IsNullOrWhiteSpace(category) ? "Errors" : category.Trim();
            string m = message ?? string.Empty;

            try
            {
                AppLog.Error(c, m, ex);
            }
            catch
            {
                // 忽略：错误上报本身不能影响主流程
            }

            if (prompt is null)
            {
                return;
            }

            global::WindBoard.MainWindow? window = GetMainWindowSnapshot();
            if (window is null)
            {
                return;
            }

            string signature = string.IsNullOrWhiteSpace(prompt.Signature)
                ? BuildDefaultSignature(c, m, ex)
                : prompt.Signature.Trim();

            // 统一切回 UI 线程走提醒服务，避免在后台线程访问视觉树/窗口状态。
            if (!window.DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    AppReminderService.Instance.RemindOncePerSignature(
                        window,
                        signature,
                        new AppReminderMessage
                        {
                            Title = prompt.Title ?? string.Empty,
                            Body = prompt.Body ?? string.Empty,
                            Severity = prompt.Severity,
                            ClickAction = prompt.ClickAction,
                        });
                }
                catch (Exception reminderEx)
                {
                    AppLog.Warn("Errors", "展示错误提醒失败", reminderEx);
                }
            }))
            {
                // DispatcherQueue 不可用：直接忽略提醒，不影响主流程。
            }
        }

        private static string BuildDefaultSignature(string category, string message, Exception ex)
        {
            // 说明：签名用于“提醒一次”去重，口径要稳定，避免把可变信息（路径/时间）纳入导致去重失效。
            string type = ex.GetType().Name;
            return $"Err:{category}:{type}:{message}";
        }

        private global::WindBoard.MainWindow? GetMainWindowSnapshot()
        {
            lock (_gate)
            {
                return _mainWindow;
            }
        }

        private void TryShowCrashDialogAndExit(AppCrashReport? report)
        {
            global::WindBoard.MainWindow? window = GetMainWindowSnapshot();
            if (window is null)
            {
                // 没有窗口：只能直接退出。
                TryExitApplication();
                return;
            }

            bool enqueued = window.DispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    await ShowCrashDialogAsync(window, report);
                }
                catch (Exception ex)
                {
                    // 注意：即使弹窗失败也要退出；这里用 Critical 便于排查“为什么没弹窗”。
                    AppLog.Critical("App", "展示崩溃对话框失败", ex);
                }
                finally
                {
                    TryExitApplication();
                }
            });

            if (!enqueued)
            {
                // DispatcherQueue 入队失败：尝试直接执行（可能已在 UI 线程）。
                _ = ShowCrashDialogAndExitFallbackAsync(window, report);
            }
        }

        private async Task ShowCrashDialogAndExitFallbackAsync(global::WindBoard.MainWindow window, AppCrashReport? report)
        {
            try
            {
                await ShowCrashDialogAsync(window, report);
            }
            catch (Exception ex)
            {
                AppLog.Critical("App", "展示崩溃对话框失败（fallback）", ex);
            }
            finally
            {
                TryExitApplication();
            }
        }

        private static async Task ShowCrashDialogAsync(global::WindBoard.MainWindow window, AppCrashReport? report)
        {
            // 说明：崩溃对话框属于“尽力而为”，任何异常都必须被捕获，不能影响退出流程。
            if (window is null)
            {
                return;
            }

            if (window.Content is not FrameworkElement root || root.XamlRoot is null)
            {
                return;
            }

            string reportText = report?.ReportText ?? string.Empty;
            string reportPath = report?.ReportFilePath ?? string.Empty;

            var feedback = new TextBlock
            {
                Text = string.Empty,
                TextWrapping = TextWrapping.Wrap,
            };

            var info = new TextBlock
            {
                Text = L10n.Get("Common_CrashDialog_Body"),
                TextWrapping = TextWrapping.Wrap,
            };

            var path = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(reportPath) ? string.Empty : reportPath,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Opacity = 0.80,
            };

            var details = new TextBox
            {
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.NoWrap,
                MinHeight = 220,
                MaxHeight = 420,
            };
            // 注意：必须先设置 AcceptsReturn=true，再赋值 Text；
            // 否则 TextBox 仍处于“单行模式”时会截断换行后的内容，导致只显示第一行。
            details.Text = reportText;

            var content = new StackPanel { Spacing = 10 };
            content.Children.Add(info);
            if (!string.IsNullOrWhiteSpace(path.Text))
            {
                content.Children.Add(path);
            }

            if (!string.IsNullOrWhiteSpace(reportText))
            {
                content.Children.Add(details);
            }

            content.Children.Add(feedback);

            var dialog = new ContentDialog
            {
                Title = L10n.Get("Common_CrashDialog_Title"),
                Content = content,
                PrimaryButtonText = L10n.Get("Common_CopyDiagnostics"),
                SecondaryButtonText = L10n.Get("Common_OpenLogDirectory"),
                CloseButtonText = L10n.Get("Common_Exit"),
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = root.XamlRoot,
            };

            dialog.PrimaryButtonClick += (_, args) =>
            {
                args.Cancel = true;
                try
                {
                    string text = string.IsNullOrWhiteSpace(reportText) ? BuildFallbackDiagnosticsText(reportPath) : reportText;
                    if (TryCopyToClipboard(text, out string? error))
                    {
                        feedback.Text = L10n.Get("Common_DiagnosticsCopied");
                    }
                    else
                    {
                        feedback.Text = string.IsNullOrWhiteSpace(error) ? L10n.Get("Common_UnknownError") : error;
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Warn("Errors", "复制诊断信息失败", ex);
                    feedback.Text = L10n.Get("Common_UnknownError");
                }
            };

            dialog.SecondaryButtonClick += (_, args) =>
            {
                args.Cancel = true;
                try
                {
                    // 统一走提醒动作执行器：失败只记日志，不影响主流程。
                    AppReminderActionExecutor.TryExecute(AppReminderClickAction.OpenLogsDirectory);
                }
                catch (Exception ex)
                {
                    AppLog.Warn("Errors", "打开日志目录失败", ex);
                    feedback.Text = L10n.Get("Common_UnknownError");
                }
            };

            await dialog.ShowAsync();
        }

        private static string BuildFallbackDiagnosticsText(string reportPath)
        {
            // 兜底：报告文本为空时，至少让用户复制到“报告文件路径 + 日志目录”。
            try
            {
                return $"report='{reportPath}', logs='{AppLog.LogDirectory}', logFile='{AppLog.CurrentLogFilePath ?? string.Empty}'";
            }
            catch
            {
                return reportPath ?? string.Empty;
            }
        }

        private static bool TryCopyToClipboard(string text, out string? error)
        {
            error = null;

            try
            {
                string value = text ?? string.Empty;
                var package = new DataPackage();
                package.SetText(value);
                Clipboard.SetContent(package);
                Clipboard.Flush();
                return true;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        private static void TryExitApplication()
        {
            try
            {
                Application.Current.Exit();
                return;
            }
            catch (Exception ex)
            {
                try
                {
                    AppLog.Critical("App", "退出应用失败，将强制结束进程", ex);
                }
                catch
                {
                    // 忽略：日志失败不影响退出兜底
                }
            }

            try
            {
                Environment.Exit(-1);
            }
            catch
            {
                // 忽略：最终兜底也失败时，交给宿主处理
            }
        }
    }
}
