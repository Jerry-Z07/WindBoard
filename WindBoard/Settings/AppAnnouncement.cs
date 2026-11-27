using System;
using System.Collections.Generic;
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
    /// 内容来自远端 announcements.json 或本地缓存，不内置任何文案。
    /// </summary>
    internal sealed class AppAnnouncement
    {
        private const string DefaultLanguage = "zh-CN";

        private readonly Dictionary<string, AppAnnouncementText> _texts;

        internal AppAnnouncement(
            string id,
            InfoBarSeverity severity,
            AppAnnouncementAction action,
            IReadOnlyDictionary<string, AppAnnouncementText> texts)
        {
            ArgumentNullException.ThrowIfNull(id);
            ArgumentNullException.ThrowIfNull(texts);

            Id = id;
            Severity = severity;
            Action = action;

            // 语言标签按 BCP 47 大小写不敏感；复制一份避免调用方后续修改原字典。
            _texts = new Dictionary<string, AppAnnouncementText>(texts, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 稳定标识，同时作为“已关闭”去重的键。
        /// </summary>
        internal string Id { get; }

        internal InfoBarSeverity Severity { get; }

        internal AppAnnouncementAction Action { get; }

        /// <summary>
        /// 按界面语言取文案：自身 → 父语言链 → <c>zh-CN</c>；语言标签中的 <c>_</c> 视作 <c>-</c>。
        /// </summary>
        internal AppAnnouncementText ResolveText(string? cultureName)
        {
            foreach (string candidate in EnumerateLanguageCandidates(cultureName))
            {
                if (_texts.TryGetValue(candidate, out AppAnnouncementText? text))
                {
                    return text;
                }
            }

            // 解析器保证 zh-CN 存在；走到这里说明数据绕过了校验，取任一文案避免 UI 崩溃。
            foreach (AppAnnouncementText text in _texts.Values)
            {
                return text;
            }

            return new AppAnnouncementText(string.Empty, string.Empty, null);
        }

        private static IEnumerable<string> EnumerateLanguageCandidates(string? cultureName)
        {
            string normalized = (cultureName ?? string.Empty).Trim().Replace('_', '-');
            while (normalized.Length > 0)
            {
                yield return normalized;

                int separatorIndex = normalized.LastIndexOf('-');
                if (separatorIndex <= 0)
                {
                    break;
                }

                normalized = normalized[..separatorIndex];
            }

            yield return DefaultLanguage;
        }
    }
}
