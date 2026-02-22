using WindBoard.Reminders;

namespace WindBoard.Errors
{
    /// <summary>
    /// 用户提示（可选）：用于“已捕获异常”的统一提醒展示。
    /// </summary>
    internal sealed class AppErrorUserPrompt
    {
        internal string Title { get; init; } = string.Empty;

        internal string Body { get; init; } = string.Empty;

        internal AppReminderSeverity Severity { get; init; } = AppReminderSeverity.Error;

        /// <summary>
        /// “提醒一次”签名：相同签名本会话内只提示一次。
        /// </summary>
        internal string Signature { get; init; } = string.Empty;

        internal AppReminderClickAction ClickAction { get; init; } = AppReminderClickAction.OpenLogsDirectory;
    }
}

