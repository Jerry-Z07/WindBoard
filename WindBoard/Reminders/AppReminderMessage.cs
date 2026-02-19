namespace WindBoard.Reminders
{
    /// <summary>
    /// 统一提醒消息体（标题 + 内容 + 严重级别）。
    /// </summary>
    internal sealed class AppReminderMessage
    {
        public string Title { get; init; } = string.Empty;

        public string Body { get; init; } = string.Empty;

        public AppReminderSeverity Severity { get; init; } = AppReminderSeverity.Info;

        /// <summary>
        /// 点击提醒后的动作（可选）。
        /// </summary>
        public AppReminderClickAction ClickAction { get; init; } = AppReminderClickAction.None;
    }
}
