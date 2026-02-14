using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WindBoard.Localization;
using WindBoard.Logging;
using WindBoard.Reminders;
using WindBoard.Settings;

namespace WindBoard
{
    public sealed partial class MainWindow
    {
        private const int MaxBannerCount = 3;

        internal void ShowInAppBanner(AppReminderMessage message)
        {
            if (message is null)
            {
                return;
            }

            // UI 安全：统一切回 UI 线程操作视觉树。
            if (!DispatcherQueue.TryEnqueue(() => ShowInAppBannerOnUiThread(message)))
            {
                ShowInAppBannerOnUiThread(message);
            }
        }

        private void ShowInAppBannerOnUiThread(AppReminderMessage message)
        {
            if (ReminderBannerStackPanel is null)
            {
                return;
            }

            var bar = new InfoBar
            {
                Title = message.Title ?? string.Empty,
                Message = message.Body ?? string.Empty,
                Severity = MapInfoBarSeverity(message.Severity),
                IsOpen = true,
                IsClosable = true,
                IsIconVisible = true,
            };

            // 新消息放到最上方，避免重要提醒被旧消息压住。
            ReminderBannerStackPanel.Children.Insert(0, bar);

            // 限制最多展示数量，避免极端情况下堆叠占满视野。
            while (ReminderBannerStackPanel.Children.Count > MaxBannerCount)
            {
                ReminderBannerStackPanel.Children.RemoveAt(ReminderBannerStackPanel.Children.Count - 1);
            }

            TimeSpan autoDismissDelay = message.Severity switch
            {
                AppReminderSeverity.Error => TimeSpan.FromSeconds(10),
                AppReminderSeverity.Warning => TimeSpan.FromSeconds(8),
                _ => TimeSpan.FromSeconds(6),
            };

            _ = AutoDismissBannerAsync(bar, autoDismissDelay);
        }

        private async Task AutoDismissBannerAsync(InfoBar bar, TimeSpan delay)
        {
            try
            {
                await Task.Delay(delay).ConfigureAwait(false);

                // 延迟后再切回 UI 线程移除控件。
                _ = DispatcherQueue.TryEnqueue(() =>
                {
                    try
                    {
                        bar.IsOpen = false;
                        ReminderBannerStackPanel?.Children.Remove(bar);
                    }
                    catch (Exception ex)
                    {
                        AppLog.Warn("Reminders", "移除应用内弹条失败", ex);
                    }
                });
            }
            catch (Exception ex)
            {
                // 延迟任务本身失败不影响主流程；记录日志便于排查。
                AppLog.Warn("Reminders", "应用内弹条自动关闭任务失败", ex);
            }
        }

        private static InfoBarSeverity MapInfoBarSeverity(AppReminderSeverity severity)
        {
            return severity switch
            {
                AppReminderSeverity.Warning => InfoBarSeverity.Warning,
                AppReminderSeverity.Error => InfoBarSeverity.Error,
                _ => InfoBarSeverity.Informational,
            };
        }

        private void TryRemindKeyboardShortcutIssuesIfNeeded()
        {
            IReadOnlyList<KeyboardShortcutNormalizationIssue> issues = AppSettingsService.Instance.ConsumeKeyboardShortcutIssues();
            if (issues.Count == 0)
            {
                return;
            }

            if (!AppSettingsService.Instance.GetShortcutConflictReminderEnabled())
            {
                AppLog.Debug("Shortcuts", $"检测到快捷键归一化问题，但已关闭提醒：count={issues.Count}");
                return;
            }

            string signature = BuildShortcutIssuesSignature(issues);
            string title = L10n.Get("Reminder_Shortcuts_AutoFix_Title");
            string body = BuildShortcutIssuesBody(issues);

            AppReminderService.Instance.RemindOncePerSignature(
                this,
                signature,
                new AppReminderMessage
                {
                    Title = title,
                    Body = body,
                    Severity = AppReminderSeverity.Warning,
                });
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

