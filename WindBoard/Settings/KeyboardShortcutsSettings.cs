namespace WindBoard.Settings
{
    /// <summary>
    /// 键盘快捷键设置（settings.json 的 keyboardShortcuts 节点）。
    /// </summary>
    internal sealed class KeyboardShortcutsSettings
    {
        /// <summary>
        /// 撤销快捷键（允许空字符串表示禁用）。
        /// </summary>
        public string Undo { get; set; } = KeyboardShortcutsDefaults.Undo;

        /// <summary>
        /// 重做快捷键（允许空字符串表示禁用）。
        /// </summary>
        public string Redo { get; set; } = KeyboardShortcutsDefaults.Redo;

        /// <summary>
        /// 重做快捷键（备用，允许空字符串表示禁用）。
        /// </summary>
        public string RedoAlternative { get; set; } = KeyboardShortcutsDefaults.RedoAlternative;
    }
}

