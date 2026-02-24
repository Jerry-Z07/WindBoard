namespace WindBoard.Features.Shortcuts.Models
{
    /// <summary>
    /// 键盘快捷键设置快照（用于避免直接暴露内部 Settings 引用）。
    /// </summary>
    internal sealed class KeyboardShortcutsSnapshot
    {
        public string Undo { get; init; } = string.Empty;

        public string Redo { get; init; } = string.Empty;
    }
}
