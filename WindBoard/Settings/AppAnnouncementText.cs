using System;

namespace WindBoard.Settings
{
    /// <summary>
    /// 公告在某一语言下的文案。
    /// </summary>
    internal sealed class AppAnnouncementText
    {
        internal AppAnnouncementText(string title, string message, string? actionText)
        {
            ArgumentNullException.ThrowIfNull(title);
            ArgumentNullException.ThrowIfNull(message);

            Title = title;
            Message = message;
            ActionText = actionText;
        }

        internal string Title { get; }

        internal string Message { get; }

        /// <summary>
        /// 动作按钮文案；公告无动作时为 <c>null</c>。
        /// </summary>
        internal string? ActionText { get; }
    }
}
