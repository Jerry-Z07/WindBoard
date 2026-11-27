using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;
using WindBoard.Features.Dock.Models;
using WindBoard.Features.Dock.Services;
using WindBoard.Localization;

namespace WindBoard.Features.Dock.UI
{
    /// <summary>
    /// 快捷入口的新增/编辑对话框：路径、显示名、图标与启动参数。
    /// </summary>
    public sealed partial class ShortcutDockEditDialog : ContentDialog
    {
        private readonly IntPtr _hostWindowHandle;

        public ShortcutDockItemEditorViewModel Editor { get; }

        public ShortcutDockEditDialog(ShortcutDockItemEditorViewModel editor, IntPtr hostWindowHandle)
        {
            Editor = editor ?? throw new ArgumentNullException(nameof(editor));
            _hostWindowHandle = hostWindowHandle;

            // x:Bind 在 InitializeComponent 时读取 Editor，必须先赋值。
            InitializeComponent();
        }

        private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            string path = Editor.Path?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(path))
            {
                args.Cancel = true;
                ShowValidation(L10n.Get("Dock_Edit_ShortcutPathRequired_Message"));
                return;
            }

            if (string.Equals(Editor.Type, ShortcutDockItemTypes.Link, StringComparison.Ordinal)
                && !ShortcutDockLaunchHelper.TryNormalizeLinkUri(path, out _))
            {
                args.Cancel = true;
                ShowValidation(L10n.Get("ShortcutDock_InvalidLink_Message"));
                return;
            }

            ValidationBar.IsOpen = false;
        }

        private async void OnBrowsePathClicked(object sender, RoutedEventArgs e)
        {
            if (!TryGetHostWindowHandle(out IntPtr hwnd))
            {
                return;
            }

            try
            {
                var picker = new FileOpenPicker();
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
                picker.FileTypeFilter.Clear();

                if (string.Equals(Editor.Type, ShortcutDockItemTypes.Program, StringComparison.Ordinal))
                {
                    picker.FileTypeFilter.Add(".exe");
                    picker.FileTypeFilter.Add(".bat");
                    picker.FileTypeFilter.Add(".cmd");
                    picker.FileTypeFilter.Add(".lnk");
                }
                else
                {
                    picker.FileTypeFilter.Add("*");
                }

                StorageFile? file = await picker.PickSingleFileAsync();
                if (file is not null)
                {
                    Editor.Path = file.Path;
                }
            }
            catch (Exception ex)
            {
                ShowValidation(ex.Message);
            }
        }

        private async void OnBrowseIconClicked(object sender, RoutedEventArgs e)
        {
            if (!TryGetHostWindowHandle(out IntPtr hwnd))
            {
                return;
            }

            try
            {
                var picker = new FileOpenPicker();
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
                picker.FileTypeFilter.Clear();
                picker.FileTypeFilter.Add(".png");
                picker.FileTypeFilter.Add(".jpg");
                picker.FileTypeFilter.Add(".jpeg");
                picker.FileTypeFilter.Add(".bmp");
                picker.FileTypeFilter.Add(".gif");

                StorageFile? file = await picker.PickSingleFileAsync();
                if (file is not null)
                {
                    Editor.IconPath = file.Path;
                }
            }
            catch (Exception ex)
            {
                ShowValidation(ex.Message);
            }
        }

        private bool TryGetHostWindowHandle(out IntPtr hwnd)
        {
            hwnd = _hostWindowHandle;
            if (hwnd != IntPtr.Zero)
            {
                return true;
            }

            ShowValidation(L10n.Get("Common_WindowHandleFailed_Message"));
            return false;
        }

        private void ShowValidation(string message)
        {
            ValidationBar.Message = message;
            ValidationBar.IsOpen = true;
        }
    }
}
