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
        private int _winUiCrashHandlingStarted;
        private int _appDomainCrashHandlingStarted;

        // 防御：避免重复拉起 CrashReporter（可能出现 WinUI + AppDomain 同时触发）。
        private int _crashReporterLaunchAttempted;
        private int _crashReporterLaunched;

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

        private bool TryLaunchCrashReporter(AppCrashReport? report, AppCrashSource source)
        {
            // 说明：该方法位于“崩溃链路”，必须极其保守：任何异常都不得冒泡。
            // 返回值语义：true 表示 CrashReporter 已经启动（或此前已启动）；false 表示未启动。

            try
            {
                if (Volatile.Read(ref _crashReporterLaunched) == 1)
                {
                    return true;
                }

                if (Interlocked.CompareExchange(ref _crashReporterLaunchAttempted, 1, 0) != 0)
                {
                    return Volatile.Read(ref _crashReporterLaunched) == 1;
                }

                string exePath = GetCrashReporterExePath();
                if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
                {
                    try
                    {
                        AppLog.Warn("App", $"CrashReporter 不存在，已跳过：path='{exePath}'");
                    }
                    catch
                    {
                        // 忽略：日志失败不影响崩溃兜底
                    }

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
                    try
                    {
                        AppLog.Warn("App", $"启动 CrashReporter 失败（Process.Start 返回 null）：path='{exePath}'");
                    }
                    catch
                    {
                        // 忽略
                    }

                    return false;
                }

                Interlocked.Exchange(ref _crashReporterLaunched, 1);

                try
                {
                    AppLog.Info("App", $"已启动 CrashReporter：path='{exePath}', report='{reportPath}', logsDir='{logsDir}', source={source}");
                }
                catch
                {
                    // 忽略
                }

                return true;
            }
            catch (Exception ex)
            {
                try
                {
                    AppLog.Warn("App", "启动 CrashReporter 异常", ex);
                }
                catch
                {
                    // 忽略
                }

                return false;
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
