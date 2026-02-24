using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using WindBoard.Features.Shortcuts.Models;
using WindBoard.Localization;
using WindBoard.Logging;
using WindBoard.Reminders;

namespace WindBoard.Features.Shortcuts.Services
{
    /// <summary>
    /// 快捷键归一化问题提醒：把“自动修复了什么”以一次性提醒的形式反馈给用户。
    /// </summary>
    internal sealed class KeyboardShortcutIssuesReminder
    {
        internal void TryRemindIfNeeded(Window window, IReadOnlyList<KeyboardShortcutNormalizationIssue> issues, bool enabled)
        {
            if (window is null)
            {
                throw new ArgumentNullException(nameof(window));
            }

            if (issues is null || issues.Count == 0)
            {
                return;
            }

            if (!enabled)
            {
                AppLog.Debug("Shortcuts", $"检测到快捷键归一化问题，但已关闭提醒：count={issues.Count}");
                return;
            }

            try
            {
                string signature = BuildShortcutIssuesSignature(issues);
                string title = L10n.Get("Reminder_Shortcuts_AutoFix_Title");
                string body = BuildShortcutIssuesBody(issues);

                AppReminderService.Instance.RemindOncePerSignature(
                    window,
                    signature,
                    new AppReminderMessage
                    {
                        Title = title,
                        Body = body,
                        Severity = AppReminderSeverity.Warning,
                    });
            }
            catch (Exception ex)
            {
                // 兜底：提醒失败不影响主流程；记录日志便于排查（例如通道不可用/本地化资源缺失等）。
                AppLog.Warn("Shortcuts", "展示快捷键自动修复提醒失败", ex);
            }
        }

        private static string BuildShortcutIssuesSignature(IEnumerable<KeyboardShortcutNormalizationIssue> issues)
        {
            // “提醒一次”口径：同一问题不重复弹。为此把问题集合归一化为稳定签名：
            // - 排序（避免顺序影响）
            // - 去重（避免重复记录导致签名变化）
            List<string> tokens = issues
                .Where(i => i is not null)
                .Select(i => $"{i.Kind}|{i.Slot}|{i.ConflictWithSlot ?? "-"}|{i.NewValue}")
                .Distinct(StringComparer.Ordinal)
                .OrderBy(t => t, StringComparer.Ordinal)
                .ToList();

            return "ShortcutsAutoFix:" + string.Join(";", tokens);
        }

        private static string BuildShortcutIssuesBody(IEnumerable<KeyboardShortcutNormalizationIssue> issues)
        {
            var lines = new List<string>();

            foreach (KeyboardShortcutNormalizationIssue issue in issues)
            {
                if (issue is null)
                {
                    continue;
                }

                string slotTitle = GetShortcutSlotTitle(issue.Slot);
                switch (issue.Kind)
                {
                    case KeyboardShortcutNormalizationIssueKind.InvalidRevertedToDefault:
                        lines.Add(L10n.Format("Reminder_Shortcuts_AutoFix_Line_Invalid_Fmt", slotTitle, issue.NewValue));
                        break;
                    case KeyboardShortcutNormalizationIssueKind.ConflictDisabled:
                    {
                        string conflictTitle = GetShortcutSlotTitle(issue.ConflictWithSlot ?? string.Empty);
                        lines.Add(L10n.Format("Reminder_Shortcuts_AutoFix_Line_Conflict_Fmt", slotTitle, conflictTitle));
                        break;
                    }
                }
            }

            // 兜底：如果没有生成任何行，也避免空提醒。
            if (lines.Count == 0)
            {
                lines.Add(L10n.Get("Reminder_Shortcuts_AutoFix_Line_Generic"));
            }

            lines.Add(string.Empty);
            lines.Add(L10n.Get("Reminder_Shortcuts_AutoFix_Footer"));
            return string.Join(Environment.NewLine, lines);
        }

        private static string GetShortcutSlotTitle(string slot)
        {
            return slot switch
            {
                "Undo" => L10n.Get("Settings_Shortcuts_Undo_Title"),
                "Redo" => L10n.Get("Settings_Shortcuts_Redo_Title"),
                _ => slot ?? string.Empty,
            };
        }
    }
}

