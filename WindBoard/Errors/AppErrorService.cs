using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using WindBoard.Logging;
using WindBoard.Reminders;

namespace WindBoard.Errors
{
    /// <summary>
    /// 应用统一错误处理中心：
    /// - 未处理异常：落盘崩溃报告 + 拉起独立 CrashReporter 并退出；
    /// - 已捕获异常：统一记录日志，并可选展示用户提示（提醒一次）。
    /// </summary>
    internal sealed class AppErrorService
    {
        private readonly object _gate = new();
        private global::WindBoard.MainWindow? _mainWindow;

        // 防御：避免崩溃处理重入（可能出现异常连锁/重复触发）。
        private readonly OneTimeGate _winUiCrashGate = new();
        private readonly OneTimeGate _appDomainCrashGate = new();

        // 防御：避免重复拉起 CrashReporter（可能出现 WinUI + AppDomain 同时触发）。
        // 语义：只尝试启动一次；如果成功，则视为“已启动”（后续直接返回 true）。
        private readonly OneTimeGate _crashReporterLaunchAttemptGate = new();
        private readonly OneTimeGate _crashReporterLaunchedGate = new();

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

            // 必须尽早标记 Handled：否则可能直接被宿主终止，无法写报告/拉起 CrashReporter。
            try
            {
                e.Handled = true;
            }
            catch
            {
                // 忽略：不同运行时/配置下可能不允许设置
            }

            if (!_winUiCrashGate.TryOpen())
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

            if (wrote)
            {
                SafeLogCritical("App", $"WinUI UnhandledException（已写崩溃报告）：report='{report.ReportFilePath}'", ex);
            }
            else
            {
                SafeLogCritical("App", "WinUI UnhandledException（写崩溃报告失败）", writeError ?? ex);
            }

            // 写入完成后尽力拉起 CrashReporter；无论拉起成功与否，最终都退出进程，避免处于不一致状态继续运行。
            AppCrashReport? crashReport = wrote ? report : null;

            // 使用独立 CrashReporter：不依赖 WinUI 视觉树，避免“主进程已坏导致弹窗出不来”。
            try
            {
                _ = TryLaunchCrashReporter(crashReport, AppCrashSource.WinUIUnhandledException);
            }
            catch
            {
                // 忽略：崩溃链路兜底不能再抛异常
            }

            // 无论 CrashReporter 是否拉起成功，主进程都应退出，避免处于不一致状态继续运行。
            TryExitApplication();
        }

        internal void HandleAppDomainUnhandledException(System.UnhandledExceptionEventArgs e)
        {
            if (e is null)
            {
                return;
            }

            if (!_appDomainCrashGate.TryOpen())
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

            if (wrote)
            {
                SafeLogCritical("App", $"AppDomain UnhandledException（已写崩溃报告）：isTerminating={isTerminating}, report='{report.ReportFilePath}'", ex);
            }
            else if (ex is not null)
            {
                SafeLogCritical("App", $"AppDomain UnhandledException（写崩溃报告失败）：isTerminating={isTerminating}", writeError ?? ex);
            }
            else
            {
                SafeLogCritical(
                    "App",
                    $"AppDomain UnhandledException（写崩溃报告失败）：isTerminating={isTerminating}, exceptionObjectType={exceptionObject?.GetType().FullName ?? "(null)"}",
                    writeError);
            }

            // AppDomain 未处理异常时也尽力拉起 CrashReporter（不依赖窗口是否创建）。
            try
            {
                _ = TryLaunchCrashReporter(wrote ? report : null, AppCrashSource.AppDomainUnhandledException);
            }
            catch
            {
                // 忽略：崩溃链路兜底不能再抛异常
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
                SafeLogError("App", "TaskScheduler.UnobservedTaskException", e.Exception);

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

            SafeLogError(c, m, ex);

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

            TryEnqueueHandledErrorPrompt(window, signature, prompt);
        }

        private void TryEnqueueHandledErrorPrompt(global::WindBoard.MainWindow window, string signature, AppErrorUserPrompt prompt)
        {
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
                    SafeLogWarn("Errors", "展示错误提醒失败", reminderEx);
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

        private static void TryExitApplication()
        {
            try
            {
                Application.Current.Exit();
                return;
            }
            catch (Exception ex)
            {
                SafeLogCritical("App", "退出应用失败，将强制结束进程", ex);
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

        private bool TryLaunchCrashReporter(AppCrashReport? report, AppCrashSource source)
        {
            // 说明：该方法位于“崩溃链路”，必须极其保守：任何异常都不得冒泡。
            // 返回值语义：true 表示 CrashReporter 已经启动（或此前已启动）；false 表示未启动。

            try
            {
                if (_crashReporterLaunchedGate.IsOpened)
                {
                    return true;
                }

                if (!_crashReporterLaunchAttemptGate.TryOpen())
                {
                    return _crashReporterLaunchedGate.IsOpened;
                }

                string exePath = GetCrashReporterExePath();
                if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
                {
                    SafeLogWarn("App", $"CrashReporter 不存在，已跳过：path='{exePath}'");

                    return false;
                }

                string reportPath = report?.ReportFilePath ?? string.Empty;
                string logsDir = GetLogsDirectoryForCrashReporter();

                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = AppContext.BaseDirectory,
                };

                psi.ArgumentList.Add("--report");
                psi.ArgumentList.Add(reportPath ?? string.Empty);

                psi.ArgumentList.Add("--logs-dir");
                psi.ArgumentList.Add(logsDir ?? string.Empty);

                psi.ArgumentList.Add("--source");
                psi.ArgumentList.Add(source.ToString());

                Process? p = Process.Start(psi);
                if (p is null)
                {
                    SafeLogWarn("App", $"启动 CrashReporter 失败（Process.Start 返回 null）：path='{exePath}'");

                    return false;
                }

                _crashReporterLaunchedGate.Open();

                SafeLogInfo("App", $"已启动 CrashReporter：path='{exePath}', report='{reportPath}', logsDir='{logsDir}', source={source}");

                return true;
            }
            catch (Exception ex)
            {
                SafeLogWarn("App", "启动 CrashReporter 异常", ex);

                return false;
            }
        }

        private static void SafeLogInfo(string category, string message)
        {
            try
            {
                AppLog.Info(category, message);
            }
            catch
            {
                // 忽略：日志失败不影响关键路径
            }
        }

        private static void SafeLogWarn(string category, string message, Exception? ex = null)
        {
            try
            {
                AppLog.Warn(category, message, ex);
            }
            catch
            {
                // 忽略：日志失败不影响关键路径
            }
        }

        private static void SafeLogError(string category, string message, Exception? ex = null)
        {
            try
            {
                AppLog.Error(category, message, ex);
            }
            catch
            {
                // 忽略：日志失败不影响关键路径
            }
        }

        private static void SafeLogCritical(string category, string message, Exception? ex = null)
        {
            try
            {
                AppLog.Critical(category, message, ex);
            }
            catch
            {
                // 忽略：日志失败不影响关键路径
            }
        }

        private static string GetCrashReporterExePath()
        {
            // 说明：把 CrashReporter 放在应用目录根部，便于：
            // - self-contained 发布时复用同目录的 app-local runtime（hostfxr/hostpolicy 等）；
            // - 安装包/便携版打包更直观（同目录递归复制即可）。
            try
            {
                return Path.Combine(AppContext.BaseDirectory, "WindBoard.CrashReporter.exe");
            }
            catch
            {
                return string.Empty;
            }
        }

        // 说明：用于封装一次性标记位（Interlocked + Volatile），让主流程更聚焦于业务编排。
        private sealed class OneTimeGate
        {
            private int _opened;

            internal bool TryOpen()
            {
                return Interlocked.CompareExchange(ref _opened, 1, 0) == 0;
            }

            internal bool IsOpened => Volatile.Read(ref _opened) == 1;

            internal void Open()
            {
                Interlocked.Exchange(ref _opened, 1);
            }
        }

        private static string GetLogsDirectoryForCrashReporter()
        {
            // 说明：优先使用当前日志目录（由 AppLog/AppDataPaths 决定），失败时再回退到 LocalAppData。
            try
            {
                string dir = (AppLog.LogDirectory ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    return dir;
                }
            }
            catch
            {
                // 忽略：继续走兜底
            }

            try
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "WindBoard",
                    "Logs");
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
