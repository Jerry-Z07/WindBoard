using System;
using System.Collections.Generic;

namespace WindBoard.Settings
{
    /// <summary>
    /// 公告选择逻辑：按内容顺序返回第一条尚未被关闭的公告。
    /// </summary>
    internal static class AppAnnouncementSelector
    {
        /// <summary>
        /// 按顺序返回第一条 Id 未被关闭的公告；全部已关闭或列表为空时返回 <c>null</c>。
        /// </summary>
        internal static AppAnnouncement? SelectNext(
            IReadOnlyList<AppAnnouncement> announcements,
            IReadOnlyCollection<string> dismissedIds)
        {
            ArgumentNullException.ThrowIfNull(announcements);
            ArgumentNullException.ThrowIfNull(dismissedIds);

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
