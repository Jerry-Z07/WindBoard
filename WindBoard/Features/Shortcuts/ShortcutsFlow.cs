using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WindBoard.Features.Shortcuts.Models;
using WindBoard.Features.Shortcuts.Services;
using WindBoard.Logging;

namespace WindBoard.Features.Shortcuts
{
    /// <summary>
    /// Shortcuts 功能编排：
    /// - 读取快捷键设置快照
    /// - 应用到主窗口（KeyboardAccelerator 绑定）
    /// - 触发一次性提醒（快捷键非法/冲突自动修复）
    /// </summary>
    internal sealed class ShortcutsFlow
    {
        private readonly Func<KeyboardShortcutsSnapshot> _getKeyboardShortcutsSnapshot;
        private readonly Func<IReadOnlyList<KeyboardShortcutNormalizationIssue>> _consumeKeyboardShortcutIssues;
        private readonly Func<bool> _getShortcutConflictReminderEnabled;
        private readonly KeyboardShortcutIssuesReminder _issuesReminder = new();

        internal ShortcutsFlow(
            Func<KeyboardShortcutsSnapshot> getKeyboardShortcutsSnapshot,
            Func<IReadOnlyList<KeyboardShortcutNormalizationIssue>> consumeKeyboardShortcutIssues,
            Func<bool> getShortcutConflictReminderEnabled)
        {
            _getKeyboardShortcutsSnapshot = getKeyboardShortcutsSnapshot ?? throw new ArgumentNullException(nameof(getKeyboardShortcutsSnapshot));
            _consumeKeyboardShortcutIssues = consumeKeyboardShortcutIssues ?? throw new ArgumentNullException(nameof(consumeKeyboardShortcutIssues));
            _getShortcutConflictReminderEnabled = getShortcutConflictReminderEnabled ?? throw new ArgumentNullException(nameof(getShortcutConflictReminderEnabled));
        }

        internal void ApplyToMainWindow(ShortcutsMainWindowHost host)
        {
            if (host is null)
            {
                throw new ArgumentNullException(nameof(host));
            }

            try
            {
                KeyboardShortcutsSnapshot shortcuts = _getKeyboardShortcutsSnapshot();
                ApplyKeyboardAccelerators(host, shortcuts);
            }
            catch (Exception ex)
            {
                // 兜底：避免异常冒泡到 UI 线程导致崩溃。
                AppLog.Error("Shortcuts", "应用快捷键到主窗口失败。", ex);
            }

            // 快捷键冲突/非法值在加载/更新时会被自动归一化（例如回退默认或禁用冲突项）。
            // 这里统一做一次提醒（可在“设置-快捷键”中关闭提醒）。
            TryRemindKeyboardShortcutIssuesIfNeeded(host);
        }

        private void ApplyKeyboardAccelerators(ShortcutsMainWindowHost host, KeyboardShortcutsSnapshot shortcuts)
        {
            // KeyboardAccelerator 绑定到根 Grid，确保在不同控件聚焦时仍可响应（但文本输入控件内会被显式拦截）。
            if (host.Root is null)
            {
                return;
            }

            host.Root.KeyboardAccelerators.Clear();

            // 防御：再次做去重，避免异常设置导致同一组合键绑定多个动作。
            var used = new HashSet<string>(StringComparer.Ordinal);

            TryAddKeyboardAccelerator(
                host,
                used,
                slot: "Undo",
                shortcuts.Undo,
                args => OnUndoKeyboardAcceleratorInvoked(host, args));

            TryAddKeyboardAccelerator(
                host,
                used,
                slot: "Redo",
                shortcuts.Redo,
                args => OnRedoKeyboardAcceleratorInvoked(host, args));
        }

        private static void TryAddKeyboardAccelerator(
            ShortcutsMainWindowHost host,
            HashSet<string> used,
            string slot,
            string value,
            Action<KeyboardAcceleratorInvokedEventArgs> invoked)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            if (!KeyboardShortcutGesture.TryParse(value, out KeyboardShortcutGesture gesture) || !gesture.IsValidForApp())
            {
                AppLog.Warn("Shortcuts", $"快捷键无效，已忽略：slot={slot}, value='{value}'");
                return;
            }

            string canonical = gesture.ToSettingString();
            if (!used.Add(canonical))
            {
                AppLog.Warn("Shortcuts", $"快捷键重复，已忽略：slot={slot}, value='{canonical}'");
                return;
            }

            var accelerator = new KeyboardAccelerator
            {
                Key = gesture.Key,
                Modifiers = gesture.Modifiers,
            };

            accelerator.Invoked += (_, args) =>
            {
                try
                {
                    invoked(args);
                }
                catch (Exception ex)
                {
                    // 兜底：快捷键执行失败不应导致 UI 线程崩溃。
                    AppLog.Error("Shortcuts", $"处理快捷键失败：slot={slot}, value='{canonical}'", ex);
                }
            };

            host.Root.KeyboardAccelerators.Add(accelerator);
        }

        private static void OnUndoKeyboardAcceleratorInvoked(ShortcutsMainWindowHost host, KeyboardAcceleratorInvokedEventArgs args)
        {
            // 文本输入控件内优先由控件自身处理 Ctrl+Z（例如导入文字对话框），避免误触撤销画布操作。
            if (host.IsTextInputFocused())
            {
                return;
            }

            if (!host.CanUndo())
            {
                return;
            }

            host.Undo();
            args.Handled = true;
        }

        private static void OnRedoKeyboardAcceleratorInvoked(ShortcutsMainWindowHost host, KeyboardAcceleratorInvokedEventArgs args)
        {
            if (host.IsTextInputFocused())
            {
                return;
            }

            if (!host.CanRedo())
            {
                return;
            }

            host.Redo();
            args.Handled = true;
        }

        private void TryRemindKeyboardShortcutIssuesIfNeeded(ShortcutsMainWindowHost host)
        {
            IReadOnlyList<KeyboardShortcutNormalizationIssue> issues;
            try
            {
                issues = _consumeKeyboardShortcutIssues();
            }
            catch (Exception ex)
            {
                AppLog.Warn("Shortcuts", "读取快捷键归一化问题失败", ex);
                return;
            }

            if (issues.Count == 0)
            {
                return;
            }

            bool enabled;
            try
            {
                enabled = _getShortcutConflictReminderEnabled();
            }
            catch (Exception ex)
            {
                // 兜底：开关读取失败时按“启用”处理，避免吞掉重要提醒。
                AppLog.Warn("Shortcuts", "读取快捷键提醒开关失败，已按启用处理", ex);
                enabled = true;
            }

            _issuesReminder.TryRemindIfNeeded(host.Window, issues, enabled);
        }
    }
}

