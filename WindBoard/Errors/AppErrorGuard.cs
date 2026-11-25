using System;
using System.Threading.Tasks;

namespace WindBoard.Errors
{
    /// <summary>
    /// 安全执行封装：捕获异常并交给 AppErrorService 上报。
    /// 用于 UI 事件回调（避免异常冒泡导致崩溃或静默失败）与 fire-and-forget 异步任务（统一兜底与日志）。
    /// </summary>
    internal static class AppErrorGuard
    {
        internal static void Run(string category, Action action, AppErrorUserPrompt? prompt = null)
        {
            ArgumentNullException.ThrowIfNull(action);

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
            ArgumentNullException.ThrowIfNull(action);

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
            ArgumentNullException.ThrowIfNull(taskFactory);

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

