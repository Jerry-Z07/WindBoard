using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WindBoard.Features.Shortcuts;
using WindBoard.Features.Shortcuts.Services;
using WindBoard.Logging;
using WindBoard.Settings;

namespace WindBoard
{
    /// <summary>
    /// 主窗口：快捷键相关入口（具体逻辑在 Features/Shortcuts）。
    /// </summary>
    public sealed partial class MainWindow
    {
        private ShortcutsFlow? _shortcutsFlow;

        private void ApplyKeyboardShortcutsToUi()
        {
            // KeyboardAccelerator 绑定到根 Grid，确保在不同控件聚焦时仍可响应（但文本输入控件内会被显式拦截）。
            if (Content is not Grid root)
            {
                return;
            }

            try
            {
                // 延迟初始化：避免字段初始值设定项捕获实例成员导致编译错误。
                _shortcutsFlow ??= new ShortcutsFlow(
                    getKeyboardShortcutsSnapshot: () => AppSettingsService.Instance.GetKeyboardShortcutsSnapshot(),
                    consumeKeyboardShortcutIssues: () => AppSettingsService.Instance.ConsumeKeyboardShortcutIssues(),
                    getShortcutConflictReminderEnabled: () => AppSettingsService.Instance.GetShortcutConflictReminderEnabled());

                _shortcutsFlow.ApplyToMainWindow(CreateShortcutsHost(root));
            }
            catch (Exception ex)
            {
                // 兜底：避免异常冒泡到 UI 线程导致崩溃。
                AppLog.Error("Shortcuts", "应用快捷键设置异常。", ex);
            }
        }

        private ShortcutsMainWindowHost CreateShortcutsHost(Grid root)
        {
            return new ShortcutsMainWindowHost
            {
                Window = this,
                Root = root,
                IsTextInputFocused = IsTextInputFocused,
                CanUndo = () => BoardCanvas.CanUndo,
                Undo = () => BoardCanvas.Undo(),
                CanRedo = () => BoardCanvas.CanRedo,
                Redo = () => BoardCanvas.Redo(),
            };
        }

        private bool IsTextInputFocused()
        {
            if (Content is not FrameworkElement root || root.XamlRoot is null)
            {
                return false;
            }

            object? focused = FocusManager.GetFocusedElement(root.XamlRoot);
            return focused is TextBox or PasswordBox or RichEditBox;
        }
    }
}
