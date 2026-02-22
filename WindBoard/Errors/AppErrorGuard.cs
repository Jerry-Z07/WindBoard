using System;
using System.Threading.Tasks;

namespace WindBoard.Errors
{
    /// <summary>
    /// 安全执行封装：统一捕获异常并走 AppErrorService 上报。
    ///
    /// 适用场景：
    /// - UI 事件回调（避免异常冒泡导致崩溃/静默失败）
    /// - fire-and-forget 异步任务（统一兜底与日志记录）
    /// </summary>
    internal static class AppErrorGuard
    {
        internal static void Run(string category, Action action, AppErrorUserPrompt? prompt = null)
        {
            if (action is null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            try
            {
                action();
            }
            catch (OperationCanceledException)
            {
                // 取消属于正常控制流：不当作错误。
            }
            catch (Exception ex)
            {
                AppErrorService.Instance.ReportHandledException(category, "安全执行捕获异常", ex, prompt);
            }
        }

        internal static async Task RunAsync(string category, Func<Task> action, AppErrorUserPrompt? prompt = null)
        {
            if (action is null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            try
            {
                await action();
            }
            catch (OperationCanceledException)
            {
                // 取消属于正常控制流：不当作错误。
            }
            catch (Exception ex)
            {
                AppErrorService.Instance.ReportHandledException(category, "安全执行捕获异常", ex, prompt);
            }
        }

        internal static void FireAndForget(string category, Func<Task> taskFactory, AppErrorUserPrompt? prompt = null)
        {
            if (taskFactory is null)
            {
                throw new ArgumentNullException(nameof(taskFactory));
            }

            _ = FireAndForgetCoreAsync(category, taskFactory, prompt);
        }

        private static async Task FireAndForgetCoreAsync(string category, Func<Task> taskFactory, AppErrorUserPrompt? prompt)
        {
            try
            {
                Task task = taskFactory();
                if (task is null)
                {
                    return;
                }

                await task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 取消属于正常控制流：不当作错误。
            }
            catch (Exception ex)
            {
                AppErrorService.Instance.ReportHandledException(category, "Fire-and-forget 任务异常", ex, prompt);
            }
        }
    }
}

