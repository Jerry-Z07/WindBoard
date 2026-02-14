namespace WindBoard.Settings
{
    /// <summary>
    /// 键盘快捷键设置（settings.json 的 keyboardShortcuts 节点）。
    /// </summary>
    internal sealed class KeyboardShortcutsSettings
    {
        /// <summary>
        /// 是否启用“快捷键冲突/非法自动修复”提醒。
        /// </summary>
        public bool ConflictReminderEnabled { get; set; } = true;

        /// <summary>
        /// 撤销快捷键（允许空字符串表示禁用）。
        /// </summary>
        public string Undo { get; set; } = KeyboardShortcutsDefaults.Undo;

        /// <summary>
        /// 重做快捷键（允许空字符串表示禁用）。
        /// </summary>
        public string Redo { get; set; } = KeyboardShortcutsDefaults.Redo;
    }
}
