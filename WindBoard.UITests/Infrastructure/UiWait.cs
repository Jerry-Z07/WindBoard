using System;

namespace WindBoard.UITests.Infrastructure
{
    /// <summary>
    /// 等待/重试基元：统一封装 FlaUI Retry，失败信息可读（带“在等什么”）。
    /// 原则：不使用固定 sleep，全部走条件轮询。
    /// </summary>
    public static class UiWait
    {
        /// <summary>反复执行查找，直到返回非 null（超时抛出带上下文的 TimeoutException）。</summary>
        public static T ForElement<T>(Func<T?> finder, string what, TimeSpan? timeout = null)
            where T : class
        {
            var result = FlaUI.Core.Tools.Retry.WhileNull(
                finder,
                timeout: timeout ?? WindBoardApp.ElementTimeout,
                interval: TimeSpan.FromMilliseconds(200),
                ignoreException: true);

            return result.Result
                ?? throw new TimeoutException($"等待超时（{(timeout ?? WindBoardApp.ElementTimeout).TotalSeconds:0.#}s）：{what}");
        }

        /// <summary>反复检查条件，直到成立（超时抛出 TimeoutException）。</summary>
        public static void ForCondition(Func<bool> condition, string what, TimeSpan? timeout = null)
        {
            bool success = FlaUI.Core.Tools.Retry.WhileFalse(
                condition,
                timeout: timeout ?? WindBoardApp.ElementTimeout,
                interval: TimeSpan.FromMilliseconds(200),
                ignoreException: true)
                .Success;

            if (!success)
            {
                throw new TimeoutException($"等待超时（{(timeout ?? WindBoardApp.ElementTimeout).TotalSeconds:0.#}s）：{what}");
            }
        }
    }
}
