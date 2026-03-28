using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;
using WindBoard.Localization;
using WindBoard.Logging;

namespace WindBoard.Settings.Pages
{
    public sealed partial class SettingsManagementPage : Page
    {
        private bool _isManagingSettings;

        public SettingsManagementPage()
        {
            InitializeComponent();
        }

        private async void OnExportSettingsClicked(object sender, RoutedEventArgs e)
        {
            if (!TryBeginSettingsManagementOperation())
            {
                return;
            }

            try
            {
                if (XamlRoot is null)
                {
                    ShowSettingsManagementFeedback(InfoBarSeverity.Error, L10n.Get("Common_WindowHandleFailed_Message"));
                    return;
                }

                IntPtr hwnd = TryGetHostWindowHandle();
                if (hwnd == IntPtr.Zero)
                {
                    ShowSettingsManagementFeedback(InfoBarSeverity.Error, L10n.Get("Common_WindowHandleFailed_Message"));
                    return;
                }

                StorageFile? file = await PickExportSettingsFileWithOverwriteConfirmAsync(hwnd).ConfigureAwait(true);
                if (file is null)
                {
                    return;
                }

                await AppSettingsService.Instance.ExportToFileAsync(file.Path, CancellationToken.None).ConfigureAwait(true);
                AppLog.Info("Settings", $"设置已导出：path='{file.Path}'");
                ShowSettingsManagementFeedback(InfoBarSeverity.Success, L10n.Format("Settings_About_SettingsManagement_Exported_Fmt", file.Path));
            }
            catch (Exception ex)
            {
                AppLog.Warn("Settings", "导出设置失败", ex);
                ShowSettingsManagementFeedback(InfoBarSeverity.Error, L10n.Format("Settings_About_SettingsManagement_ActionFailed_Fmt", ex.Message));
            }
            finally
            {
                EndSettingsManagementOperation();
            }
        }

        private async void OnImportSettingsClicked(object sender, RoutedEventArgs e)
        {
            if (!TryBeginSettingsManagementOperation())
            {
                return;
            }

            try
            {
                if (XamlRoot is null)
                {
                    ShowSettingsManagementFeedback(InfoBarSeverity.Error, L10n.Get("Common_WindowHandleFailed_Message"));
                    return;
                }

                IntPtr hwnd = TryGetHostWindowHandle();
                if (hwnd == IntPtr.Zero)
                {
                    ShowSettingsManagementFeedback(InfoBarSeverity.Error, L10n.Get("Common_WindowHandleFailed_Message"));
                    return;
                }

                StorageFile? file = await PickImportSettingsFileAsync(hwnd).ConfigureAwait(true);
                if (file is null)
                {
                    return;
                }

                bool confirmed = await ConfirmImportSettingsAsync(file.Path).ConfigureAwait(true);
                if (!confirmed)
                {
                    return;
                }

                await AppSettingsService.Instance.ImportFromFileAsync(file.Path, CancellationToken.None).ConfigureAwait(true);
                AppLog.Info("Settings", $"设置已导入：path='{file.Path}'");
                ShowSettingsManagementFeedback(InfoBarSeverity.Success, L10n.Format("Settings_About_SettingsManagement_Imported_Fmt", file.Path));
            }
            catch (JsonException ex)
            {
                AppLog.Warn("Settings", "导入设置失败：JSON 无效", ex);
                ShowSettingsManagementFeedback(InfoBarSeverity.Error, L10n.Get("Settings_About_SettingsManagement_ImportInvalid_Message"));
            }
            catch (Exception ex)
            {
                AppLog.Warn("Settings", "导入设置失败", ex);
                ShowSettingsManagementFeedback(InfoBarSeverity.Error, L10n.Format("Settings_About_SettingsManagement_ActionFailed_Fmt", ex.Message));
            }
            finally
            {
                EndSettingsManagementOperation();
            }
        }

        private async void OnResetSettingsClicked(object sender, RoutedEventArgs e)
        {
            if (!TryBeginSettingsManagementOperation())
            {
                return;
            }

            try
            {
                if (XamlRoot is null)
                {
                    ShowSettingsManagementFeedback(InfoBarSeverity.Error, L10n.Get("Common_WindowHandleFailed_Message"));
                    return;
                }

                bool confirmed = await ConfirmResetSettingsAsync().ConfigureAwait(true);
                if (!confirmed)
                {
                    return;
                }

                await AppSettingsService.Instance.ResetToDefaultsAsync(CancellationToken.None).ConfigureAwait(true);
                AppLog.Info("Settings", "已恢复默认设置");
                ShowSettingsManagementFeedback(InfoBarSeverity.Success, L10n.Get("Settings_About_SettingsManagement_ResetCompleted"));
            }
            catch (Exception ex)
            {
                AppLog.Warn("Settings", "恢复默认设置失败", ex);
                ShowSettingsManagementFeedback(InfoBarSeverity.Error, L10n.Format("Settings_About_SettingsManagement_ActionFailed_Fmt", ex.Message));
            }
            finally
            {
                EndSettingsManagementOperation();
            }
        }

        private bool TryBeginSettingsManagementOperation()
        {
            if (_isManagingSettings)
            {
                return false;
            }

            _isManagingSettings = true;
            SetSettingsManagementUiState(isBusy: true);
            return true;
        }

        private void EndSettingsManagementOperation()
        {
            _isManagingSettings = false;
            SetSettingsManagementUiState(isBusy: false);
        }

        private void SetSettingsManagementUiState(bool isBusy)
        {
            if (ExportSettingsCard is not null)
            {
                ExportSettingsCard.IsEnabled = !isBusy;
            }

            if (ImportSettingsCard is not null)
            {
                ImportSettingsCard.IsEnabled = !isBusy;
            }

            if (ResetSettingsCard is not null)
            {
                ResetSettingsCard.IsEnabled = !isBusy;
            }
        }

        private void ShowSettingsManagementFeedback(InfoBarSeverity severity, string message)
        {
            if (SettingsManagementFeedbackBar is null)
            {
                return;
            }

            SettingsManagementFeedbackBar.Severity = severity;
            SettingsManagementFeedbackBar.Message = message ?? string.Empty;
            SettingsManagementFeedbackBar.IsOpen = true;
        }

        private async Task<StorageFile?> PickExportSettingsFileWithOverwriteConfirmAsync(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
            {
                return null;
            }

            var picker = new FileSavePicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeChoices.Add(L10n.Get("Settings_About_SettingsManagement_FileType"), new List<string> { ".json" });
            picker.SuggestedFileName = BuildSuggestedSettingsFileName(DateTimeOffset.Now);

            while (true)
            {
                DateTimeOffset pickStarted = DateTimeOffset.Now;
                StorageFile? file = await picker.PickSaveFileAsync();
                if (file is null)
                {
                    return null;
                }

                if (!File.Exists(file.Path))
                {
                    return file;
                }

                // WinUI 的 FileSavePicker 可能先创建一个空文件再返回，这里沿用时间窗口做保守判断，避免每次都误弹“覆盖确认”。
                if (file.DateCreated >= pickStarted - TimeSpan.FromSeconds(2))
                {
                    return file;
                }

                bool overwrite = await ConfirmOverwriteExportFileAsync(file.Path).ConfigureAwait(true);
                if (overwrite)
                {
                    return file;
                }
            }
        }

        private async Task<StorageFile?> PickImportSettingsFileAsync(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
            {
                return null;
            }

            var picker = new FileOpenPicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Clear();
            picker.FileTypeFilter.Add(".json");
            picker.FileTypeFilter.Add("*");

            return await picker.PickSingleFileAsync();
        }

        private async Task<bool> ConfirmOverwriteExportFileAsync(string filePath)
        {
            if (XamlRoot is null)
            {
                return false;
            }

            var dialog = new ContentDialog
            {
                Title = L10n.Get("Common_ConfirmOverwrite_Title"),
                Content = L10n.Format("Settings_About_SettingsManagement_OverwriteExport_Content_Fmt", filePath),
                PrimaryButtonText = L10n.Get("Common_Overwrite"),
                CloseButtonText = L10n.Get("Common_Cancel"),
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot,
            };

            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }

        private async Task<bool> ConfirmImportSettingsAsync(string filePath)
        {
            if (XamlRoot is null)
            {
                return false;
            }

            var dialog = new ContentDialog
            {
                Title = L10n.Get("Settings_About_SettingsManagement_ImportConfirm_Title"),
                Content = L10n.Format("Settings_About_SettingsManagement_ImportConfirm_Content_Fmt", filePath),
                PrimaryButtonText = L10n.Get("Common_Import"),
                CloseButtonText = L10n.Get("Common_Cancel"),
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot,
            };

            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }

        private async Task<bool> ConfirmResetSettingsAsync()
        {
            if (XamlRoot is null)
            {
                return false;
            }

            var dialog = new ContentDialog
            {
                Title = L10n.Get("Settings_About_SettingsManagement_ResetConfirm_Title"),
                Content = L10n.Get("Settings_About_SettingsManagement_ResetConfirm_Content"),
                PrimaryButtonText = L10n.Get("Common_ResetToDefault"),
                CloseButtonText = L10n.Get("Common_Cancel"),
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot,
            };

            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }

        private static string BuildSuggestedSettingsFileName(DateTimeOffset now)
        {
            string date = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string time = now.ToString("HHmm", CultureInfo.InvariantCulture);
            return $"WindBoard-settings-{date}-{time}";
        }

        private static IntPtr TryGetHostWindowHandle()
        {
            try
            {
                // Page 不能直接拿宿主 Window，这里继续复用 SettingsWindow 的静态活动实例。
                if (SettingsWindow.Active is not null)
                {
                    return SettingsWindow.Active.Hwnd;
                }
            }
            catch
            {
                return IntPtr.Zero;
            }

            return IntPtr.Zero;
        }
    }
}
