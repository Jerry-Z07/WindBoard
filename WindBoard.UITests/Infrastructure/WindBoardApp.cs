using System;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Tools;
using FlaUI.UIA3;

namespace WindBoard.UITests.Infrastructure
{
    /// <summary>
    /// 被测应用实例封装：启动进程 → 等待主窗口 → 提供自动化会话 → 退出清理。
    /// 每个用例一个实例（IAsyncLifetime），保证用例间互不残留。
    /// </summary>
    public sealed class WindBoardApp : IDisposable
    {
        public static readonly TimeSpan LaunchTimeout = TimeSpan.FromSeconds(15);
        public static readonly TimeSpan ElementTimeout = TimeSpan.FromSeconds(10);
        public static readonly TimeSpan DialogTimeout = TimeSpan.FromSeconds(10);

        private readonly Application _application;
        private readonly System.Diagnostics.Process _process;

        private WindBoardApp(Application application, UIA3Automation automation, Window mainWindow)
        {
            _application = application;
            Automation = automation;
            MainWindow = mainWindow;
            _process = System.Diagnostics.Process.GetProcessById(application.ProcessId);
        }

        public UIA3Automation Automation { get; }

        /// <summary>主窗口（FlaUI Window，可 FindFirstDescendant）。</summary>
        public Window MainWindow { get; }

        public int ProcessId => _application.ProcessId;

        public ConditionFactory Cf => Automation.ConditionFactory;

        /// <summary>启动应用并等待主窗口就绪；失败时保证进程被回收后抛出。</summary>
        public static WindBoardApp Start()
        {
            string? exePath = RepoRootLocator.TryFindAppExe()
                ?? throw new InvalidOperationException("未找到 WindBoard.exe（请先构建主工程，或设置 WINDBOARD_EXE）");

            Application application = Application.Launch(exePath);
            var automation = new UIA3Automation();

            try
            {
                Window? mainWindow = Retry.WhileNull(
                        () =>
                        {
                            try
                            {
                                return application.GetMainWindow(automation);
                            }
                            catch
                            {
                                // 窗口尚未创建/COM 尚未就绪：交给 retry 重试。
                                return null;
                            }
                        },
                        timeout: LaunchTimeout,
                        interval: TimeSpan.FromMilliseconds(250),
                        ignoreException: true)
                    .Result;

                if (mainWindow is null)
                {
                    TryKill(application);
                    throw new TimeoutException($"主窗口在 {LaunchTimeout.TotalSeconds}s 内未出现：'{exePath}'");
                }

                var app = new WindBoardApp(application, automation, mainWindow);
                UiInteraction.BringToFront(mainWindow);
                return app;
            }
            catch
            {
                automation.Dispose();
                TryKill(application);
                throw;
            }
        }

        /// <summary>应用进程是否已退出。</summary>
        public bool HasExited
        {
            get
            {
                try
                {
                    _process.Refresh();
                    return _process.HasExited;
                }
                catch
                {
                    return true;
                }
            }
        }

        /// <summary>
        /// 结束应用：向主窗口发送 WM_CLOSE（走正常退出路径），超时再强杀。
        /// 说明：不用 FlaUI Application.Close——它在 Launch 早期缓存主窗口句柄，
        /// WinUI 窗口晚创建时句柄为 0 导致静默失效（实测 Close 后进程不退出）。
        /// </summary>
        public void Quit()
        {
            // CloseMainWindow 内部取 MainWindowHandle，句柄未就绪时返回 false：短重试兜底。
            bool requested = Retry.WhileFalse(
                    TryCloseMainWindow,
                    timeout: TimeSpan.FromSeconds(2),
                    interval: TimeSpan.FromMilliseconds(200),
                    ignoreException: true)
                .Success;

            if (!requested)
            {
                TryKill(_process);
                return;
            }

            bool exited = Retry.WhileFalse(
                    () => HasExited,
                    timeout: TimeSpan.FromSeconds(5),
                    interval: TimeSpan.FromMilliseconds(200),
                    ignoreException: true)
                .Success;

            if (!exited)
            {
                TryKill(_process);
            }
        }

        private bool TryCloseMainWindow()
        {
            try
            {
                return !HasExited && _process.CloseMainWindow();
            }
            catch
            {
                return false;
            }
        }

        public void Dispose()
        {
            try
            {
                if (!HasExited)
                {
                    Quit();
                }
            }
            catch
            {
                // 清理路径尽力而为。
            }

            Automation.Dispose();
            _application.Dispose();
        }

        private static void TryKill(Application application)
        {
            try
            {
                if (!application.HasExited)
                {
                    application.Kill();
                }
            }
            catch
            {
                // 进程可能刚好退出：忽略。
            }
        }

        private static void TryKill(System.Diagnostics.Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // 进程可能刚好退出：忽略。
            }
        }
    }
}
