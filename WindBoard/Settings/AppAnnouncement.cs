using System;
using Microsoft.UI.Xaml.Controls;

namespace WindBoard.Settings
{
    /// <summary>
    /// 公告被点击后要执行的动作。
    /// </summary>
    internal enum AppAnnouncementAction
    {
        None,
        OpenBackupSettings,
    }

    /// <summary>
    /// 设置窗口壳层的公告定义（只描述“展示什么、点了做什么”，不负责渲染）。
    /// </summary>
    internal sealed class AppAnnouncement
    {
        internal AppAnnouncement(
            string id,
            InfoBarSeverity severity,
            Func<string> titleProvider,
            Func<string> messageProvider,
            Func<string>? actionButtonProvider,
            AppAnnouncementAction action)
        {
            Id = id;
            Severity = severity;
            TitleProvider = titleProvider;
            MessageProvider = messageProvider;
            ActionButtonProvider = actionButtonProvider;
            Action = action;
        }

        /// <summary>
        /// 稳定标识，同时作为“已关闭”去重的键。
        /// </summary>
        internal string Id { get; }

        internal InfoBarSeverity Severity { get; }

        /// <summary>
        /// 文案用提供器而非 key 字符串：本地化审计要求 <c>L10n.Get</c> 的 key 是字面量，
        /// 提供器可以在 lambda 内保留字面量，同时把文案定义收敛到公告目录。
        /// </summary>
        internal Func<string> TitleProvider { get; }

        internal Func<string> MessageProvider { get; }

        internal Func<string>? ActionButtonProvider { get; }

        internal AppAnnouncementAction Action { get; }
    }
}
