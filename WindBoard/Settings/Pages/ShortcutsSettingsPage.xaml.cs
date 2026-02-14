using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WindBoard.Localization;
using WindBoard.Logging;
using WindBoard.Settings;

namespace WindBoard.Settings.Pages
{
    public sealed partial class ShortcutsSettingsPage : Page
    {
        private enum ShortcutSlot
        {
            Undo,
            Redo,
            RedoAlternative,
        }

        private ShortcutSlot? _editingSlot;
        private string _originalGestureText = string.Empty;
        private bool _clearedByUser;

        public ShortcutsSettingsPage()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            AppSettingsService.Instance.Changed += OnAppSettingsChanged;
            RefreshUiFromSettings();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            AppSettingsService.Instance.Changed -= OnAppSettingsChanged;
        }

        private void OnAppSettingsChanged(object? sender, EventArgs e)
        {
            // 设置变更可能来自其它线程：统一切回 UI 线程刷新控件。
            if (!DispatcherQueue.TryEnqueue(RefreshUiFromSettings))
            {
                RefreshUiFromSettings();
            }
        }

        private void RefreshUiFromSettings()
        {
            KeyboardShortcutsSnapshot snapshot = AppSettingsService.Instance.GetKeyboardShortcutsSnapshot();
            SetShortcutText(UndoShortcutTextBlock, snapshot.Undo);
            SetShortcutText(RedoShortcutTextBlock, snapshot.Redo);
            SetShortcutText(RedoAlternativeShortcutTextBlock, snapshot.RedoAlternative);
        }

        private static void SetShortcutText(TextBlock target, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                target.Text = L10n.Get("Settings_Shortcuts_NotSet");
                target.Opacity = 0.60;
                return;
            }

            target.Text = value;
            target.Opacity = 0.75;
        }

        private async void OnUndoShortcutClicked(object sender, RoutedEventArgs e)
        {
            await ShowEditDialogAsync(ShortcutSlot.Undo);
        }

        private async void OnRedoShortcutClicked(object sender, RoutedEventArgs e)
        {
            await ShowEditDialogAsync(ShortcutSlot.Redo);
        }

        private async void OnRedoAlternativeShortcutClicked(object sender, RoutedEventArgs e)
        {
            await ShowEditDialogAsync(ShortcutSlot.RedoAlternative);
        }

        private async System.Threading.Tasks.Task ShowEditDialogAsync(ShortcutSlot slot)
        {
            _editingSlot = slot;
            _clearedByUser = false;

            KeyboardShortcutsSnapshot snapshot = AppSettingsService.Instance.GetKeyboardShortcutsSnapshot();
            _originalGestureText = slot switch
            {
                ShortcutSlot.Undo => snapshot.Undo,
                ShortcutSlot.Redo => snapshot.Redo,
                ShortcutSlot.RedoAlternative => snapshot.RedoAlternative,
                _ => string.Empty,
            };

            // 对话框默认展示当前值；用户按下新的组合键后覆盖。
            GestureCaptureTextBox.Text = _originalGestureText;

            EditShortcutErrorBar.IsOpen = false;
            EditShortcutErrorBar.Message = string.Empty;

            string slotTitle = GetSlotTitle(slot);
            EditShortcutDialog.Title = L10n.Format("Settings_Shortcuts_EditDialog_Title_Fmt", slotTitle);

            EditShortcutDialog.XamlRoot = XamlRoot;
            await EditShortcutDialog.ShowAsync();
        }

        private static string GetSlotTitle(ShortcutSlot slot)
        {
            return slot switch
            {
                ShortcutSlot.Undo => L10n.Get("Settings_Shortcuts_Undo_Title"),
                ShortcutSlot.Redo => L10n.Get("Settings_Shortcuts_Redo_Title"),
                ShortcutSlot.RedoAlternative => L10n.Get("Settings_Shortcuts_RedoAlternative_Title"),
                _ => string.Empty,
            };
        }

        private void OnEditShortcutDialogOpened(ContentDialog sender, ContentDialogOpenedEventArgs args)
        {
            // 自动聚焦到捕获输入框，让用户可以直接按快捷键，无需点击。
            GestureCaptureTextBox.Focus(FocusState.Programmatic);
        }

        private void OnGestureCaptureTextBoxKeyDown(object sender, KeyRoutedEventArgs e)
        {
            // Backspace/Delete：清除（禁用）
            if (e.Key is Windows.System.VirtualKey.Back or Windows.System.VirtualKey.Delete)
            {
                GestureCaptureTextBox.Text = string.Empty;
                _clearedByUser = true;
                EditShortcutErrorBar.IsOpen = false;
                e.Handled = true;
                return;
            }

            // 只按修饰键时不更新预览，避免产生“空 Key”。
            if (KeyboardShortcutGesture.IsModifierKey(e.Key))
            {
                e.Handled = true;
                return;
            }

            Windows.System.VirtualKeyModifiers modifiers = GetCurrentModifierState();
            var gesture = new KeyboardShortcutGesture(e.Key, modifiers);

            GestureCaptureTextBox.Text = gesture.ToSettingString();
            _clearedByUser = false;

            EditShortcutErrorBar.IsOpen = false;
            EditShortcutErrorBar.Message = string.Empty;

            e.Handled = true;
        }

        private void OnEditShortcutDialogPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            if (_editingSlot is not ShortcutSlot slot)
            {
                return;
            }

            string candidate = (GestureCaptureTextBox.Text ?? string.Empty).Trim();

            // 未设置：需要用户明确清除（Backspace/Delete）才允许保存为空，避免误触导致禁用。
            if (string.IsNullOrEmpty(candidate))
            {
                if (_clearedByUser || string.IsNullOrEmpty(_originalGestureText))
                {
                    ApplyShortcutSetting(slot, string.Empty);
                    return;
                }

                ShowDialogError(L10n.Get("Settings_Shortcuts_Error_NotSet"));
                args.Cancel = true;
                return;
            }

            if (!KeyboardShortcutGesture.TryParse(candidate, out KeyboardShortcutGesture gesture) || !gesture.IsValidForApp())
            {
                ShowDialogError(L10n.Get("Settings_Shortcuts_Error_Invalid"));
                args.Cancel = true;
                return;
            }

            candidate = gesture.ToSettingString();

            if (TryGetConflict(slot, candidate, out ShortcutSlot conflictSlot))
            {
                string conflictTitle = GetSlotTitle(conflictSlot);
                ShowDialogError(L10n.Format("Settings_Shortcuts_Error_Conflict_Fmt", conflictTitle));
                args.Cancel = true;
                return;
            }

            ApplyShortcutSetting(slot, candidate);
        }

        private bool TryGetConflict(ShortcutSlot currentSlot, string candidate, out ShortcutSlot conflictSlot)
        {
            conflictSlot = default;

            if (string.IsNullOrEmpty(candidate))
            {
                return false;
            }

            KeyboardShortcutsSnapshot snapshot = AppSettingsService.Instance.GetKeyboardShortcutsSnapshot();

            bool IsConflict(ShortcutSlot slot, string value)
            {
                return slot != currentSlot
                    && !string.IsNullOrEmpty(value)
                    && string.Equals(value, candidate, StringComparison.Ordinal);
            }

            if (IsConflict(ShortcutSlot.Undo, snapshot.Undo))
            {
                conflictSlot = ShortcutSlot.Undo;
                return true;
            }

            if (IsConflict(ShortcutSlot.Redo, snapshot.Redo))
            {
                conflictSlot = ShortcutSlot.Redo;
                return true;
            }

            if (IsConflict(ShortcutSlot.RedoAlternative, snapshot.RedoAlternative))
            {
                conflictSlot = ShortcutSlot.RedoAlternative;
                return true;
            }

            return false;
        }

        private static void ShowDialogError(InfoBar errorBar, string message)
        {
            errorBar.Message = message;
            errorBar.IsOpen = true;
        }

        private void ShowDialogError(string message)
        {
            ShowDialogError(EditShortcutErrorBar, message);
        }

        private void ApplyShortcutSetting(ShortcutSlot slot, string value)
        {
            try
            {
                AppSettingsService.Instance.Update(s =>
                {
                    s.KeyboardShortcuts ??= new KeyboardShortcutsSettings();

                    switch (slot)
                    {
                        case ShortcutSlot.Undo:
                            s.KeyboardShortcuts.Undo = value;
                            break;
                        case ShortcutSlot.Redo:
                            s.KeyboardShortcuts.Redo = value;
                            break;
                        case ShortcutSlot.RedoAlternative:
                            s.KeyboardShortcuts.RedoAlternative = value;
                            break;
                    }
                });

                AppLog.Info("Shortcuts", $"快捷键已更新：slot={slot}, value='{value}'");
            }
            catch (Exception ex)
            {
                // 更新设置失败不应崩溃；记录日志便于排查（例如序列化/磁盘权限等）。
                AppLog.Error("Shortcuts", $"快捷键更新失败：slot={slot}, value='{value}'", ex);
            }
        }

        private void OnResetToDefaultClicked(object sender, RoutedEventArgs e)
        {
            try
            {
                AppSettingsService.Instance.Update(s =>
                {
                    s.KeyboardShortcuts ??= new KeyboardShortcutsSettings();
                    s.KeyboardShortcuts.Undo = KeyboardShortcutsDefaults.Undo;
                    s.KeyboardShortcuts.Redo = KeyboardShortcutsDefaults.Redo;
                    s.KeyboardShortcuts.RedoAlternative = KeyboardShortcutsDefaults.RedoAlternative;
                });

                AppLog.Info("Shortcuts", "快捷键已恢复默认值");
            }
            catch (Exception ex)
            {
                AppLog.Error("Shortcuts", "恢复默认快捷键失败", ex);
            }
        }

        // --- 修饰键状态读取：使用 user32 GetKeyState，避免依赖 WinUI 特定线程状态 API ---
        [DllImport("user32.dll")]
        private static extern short GetKeyState(int nVirtKey);

        private static Windows.System.VirtualKeyModifiers GetCurrentModifierState()
        {
            Windows.System.VirtualKeyModifiers mods = Windows.System.VirtualKeyModifiers.None;

            if (IsKeyDown(0x11)) // VK_CONTROL
            {
                mods |= Windows.System.VirtualKeyModifiers.Control;
            }

            if (IsKeyDown(0x12)) // VK_MENU (Alt)
            {
                mods |= Windows.System.VirtualKeyModifiers.Menu;
            }

            if (IsKeyDown(0x10)) // VK_SHIFT
            {
                mods |= Windows.System.VirtualKeyModifiers.Shift;
            }

            return mods;
        }

        private static bool IsKeyDown(int vk)
        {
            // GetKeyState 高位为 1 表示按下。
            return (GetKeyState(vk) & 0x8000) != 0;
        }
    }
}

