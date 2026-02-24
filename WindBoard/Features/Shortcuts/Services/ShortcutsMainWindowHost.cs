using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WindBoard.Features.Shortcuts.Services
{
    /// <summary>
    /// 主窗口快捷键所需的依赖集合：用于让 Shortcuts Feature 在不直接依赖 MainWindow 的情况下操作 UI 与触发动作。
    /// </summary>
    internal sealed class ShortcutsMainWindowHost
    {
        /// <summary>
        /// 主窗口实例：用于展示提醒（Toast/应用内弹条）等。
        /// </summary>
        public required Window Window { get; init; }

        /// <summary>
        /// 根容器：用于挂载 KeyboardAccelerator，保证在不同控件聚焦时仍可响应。
        /// </summary>
        public required Grid Root { get; init; }

        /// <summary>
        /// 是否有文本输入控件获得焦点：用于避免在文本编辑时误触画布撤销/重做。
        /// </summary>
        public required Func<bool> IsTextInputFocused { get; init; }

        public required Func<bool> CanUndo { get; init; }
        public required Action Undo { get; init; }

        public required Func<bool> CanRedo { get; init; }
        public required Action Redo { get; init; }
    }
}

