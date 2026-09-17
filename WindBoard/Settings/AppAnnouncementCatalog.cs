using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml.Controls;
using WindBoard.Localization;

namespace WindBoard.Settings
{
    /// <summary>
    /// 壳层公告定义目录。
    ///
    /// 新增公告只需在这里追加一条定义与对应本地化文案，设置窗口的渲染逻辑无需改动。
    /// </summary>
    internal static class AppAnnouncementCatalog
    {
        internal static IReadOnlyList<AppAnnouncement> All { get; } =
        [
            new AppAnnouncement(
                "installer-distribution-changed",
                InfoBarSeverity.Warning,
                static () => L10n.Get("SettingsWindow_Announcement_InstallerDistribution_Title"),
                static () => L10n.Get("SettingsWindow_Announcement_InstallerDistribution_Message"),
                static () => L10n.Get("SettingsWindow_Announcement_InstallerDistribution_Action"),
                AppAnnouncementAction.OpenBackupSettings),
        ];

        /// <summary>
        /// 按目录顺序返回第一条尚未被关闭的公告；全部已关闭时返回 <c>null</c>。
        /// </summary>
        internal static AppAnnouncement? SelectNext(
            IReadOnlyList<AppAnnouncement> announcements,
            IReadOnlyCollection<string> dismissedIds)
        {
            // Id 是程序内部稳定标识，大小写与文化无关，因此用 Ordinal 比较。
            HashSet<string> dismissed = new(dismissedIds, StringComparer.Ordinal);
            foreach (AppAnnouncement announcement in announcements)
            {
                if (!dismissed.Contains(announcement.Id))
                {
                    return announcement;
                }
            }

            return null;
        }
    }
}
