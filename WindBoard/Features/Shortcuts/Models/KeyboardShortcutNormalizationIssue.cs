namespace WindBoard.Features.Shortcuts.Models
{
    /// <summary>
    /// 快捷键归一化问题类型。
    /// </summary>
    internal enum KeyboardShortcutNormalizationIssueKind
    {
        /// <summary>
        /// 快捷键非法：已回退为默认值。
        /// </summary>
        InvalidRevertedToDefault,

        /// <summary>
        /// 快捷键冲突：已禁用（清空）冲突项。
        /// </summary>
        ConflictDisabled,
    }

    /// <summary>
    /// 快捷键归一化过程中发现的问题（用于启动/设置变更时的统一提醒）。
    /// </summary>
    internal sealed record KeyboardShortcutNormalizationIssue
    {
        /// <summary>
        /// 槽位标识（例如 Undo/Redo）。
        /// </summary>
        public string Slot { get; init; } = string.Empty;

        /// <summary>
        /// 归一化前值（已做 Trim）。
        /// </summary>
        public string OldValue { get; init; } = string.Empty;

        /// <summary>
        /// 归一化后值（已是规范字符串；空字符串表示禁用）。
        /// </summary>
        public string NewValue { get; init; } = string.Empty;

        /// <summary>
        /// 问题类型。
        /// </summary>
        public KeyboardShortcutNormalizationIssueKind Kind { get; init; }

        /// <summary>
        /// 冲突对象槽位（仅在 ConflictDisabled 时使用，例如 Undo）。
        /// </summary>
        public string? ConflictWithSlot { get; init; }
    }
}

