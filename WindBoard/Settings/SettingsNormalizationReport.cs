using System.Collections.Generic;
using WindBoard.Features.Shortcuts.Models;

namespace WindBoard.Settings
{
    /// <summary>
    /// 设置归一化报告：用于把“自动修复了什么”反馈给 UI 做一次性提醒。
    /// </summary>
    internal sealed class SettingsNormalizationReport
    {
        public List<KeyboardShortcutNormalizationIssue> KeyboardShortcutIssues { get; } = new();
    }
}

