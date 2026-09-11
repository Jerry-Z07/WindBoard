using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;
using WindBoard.Features.Export.Models;
using WindBoard.Localization;
using WindBoard.UI.Common;

namespace WindBoard.Features.Export.UI
{
    /// <summary>
    /// 导出相关 Picker 与覆盖确认对话框。
    /// </summary>
    internal static class ExportPickers
    {
        public static async Task<StorageFile?> PickSaveFileAsync(XamlRoot xamlRoot, IntPtr hwnd, ExportFormat format)
        {
            ArgumentNullException.ThrowIfNull(xamlRoot);

            if (hwnd == IntPtr.Zero)
            {
                await DialogHelpers.ShowMessageAsync(xamlRoot, L10n.Get("Export_Failed_Title"), L10n.Get("Common_WindowHandleFailed_Message"));
                return null;
            }

            var picker = new FileSavePicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;

            DateTimeOffset now = DateTimeOffset.Now;
            string date = FormatDate(now);
            string time = FormatTimeHHmm(now);

            switch (format)
            {
                case ExportFormat.Png:
                    picker.FileTypeChoices.Add(L10n.Get("Export_FileType_Png"), new List<string> { ".png" });
                    picker.SuggestedFileName = $"WindBoard-{date}-{time}";
                    break;

                case ExportFormat.Pdf:
                    picker.FileTypeChoices.Add(L10n.Get("Export_FileType_Pdf"), new List<string> { ".pdf" });
                    picker.SuggestedFileName = $"WindBoard-{date}-{time}";
                    break;

                case ExportFormat.Wbix:
                    picker.FileTypeChoices.Add(L10n.Get("Export_FileType_Wbix"), new List<string> { ".wbix" });
                    picker.SuggestedFileName = $"{date}-{time}";
                    break;

                default:
                    picker.FileTypeChoices.Add(L10n.Get("Common_File"), new List<string> { "*" });
                    picker.SuggestedFileName = "windboard";
                    break;
            }

            return await picker.PickSaveFileAsync();
        }

        public static async Task<StorageFile?> PickSaveFileWithOverwriteConfirmAsync(XamlRoot xamlRoot, IntPtr hwnd, ExportFormat format)
        {
            ArgumentNullException.ThrowIfNull(xamlRoot);

            while (true)
            {
                // FileSavePicker 会先为用户输入的新文件名预创建 0 字节文件再返回，
                // 故需记录调用时刻，用于把该占位文件与用户选中的既有文件区分开（见 SaveFilePickerPlaceholder）。
                DateTimeOffset pickStarted = DateTimeOffset.Now;

                StorageFile? file = await PickSaveFileAsync(xamlRoot, hwnd, format);
                if (file is null)
                {
                    return null;
                }

                // 文件不存在（环境不预创建占位文件）或存在但属于本次 Picker 预创建的占位文件 → 新文件，直接返回；
                // 其余情况为用户选中的已存在文件 → 覆盖确认。
                if (!File.Exists(file.Path) || SaveFilePickerPlaceholder.IsCreatedByPicker(file, pickStarted))
                {
                    return file;
                }

                bool overwrite = await ConfirmOverwriteFileAsync(xamlRoot, file.Path);
                if (overwrite)
                {
                    return file;
                }
            }
        }

        public static async Task<StorageFolder?> PickFolderAsync(XamlRoot xamlRoot, IntPtr hwnd)
        {
            ArgumentNullException.ThrowIfNull(xamlRoot);

            if (hwnd == IntPtr.Zero)
            {
                await DialogHelpers.ShowMessageAsync(xamlRoot, L10n.Get("Export_Failed_Title"), L10n.Get("Common_WindowHandleFailed_Message"));
                return null;
            }

            var picker = new FolderPicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            // FolderPicker 也需要 FileTypeFilter（WinUI 3 桌面端约束）。
            picker.FileTypeFilter.Clear();
            picker.FileTypeFilter.Add("*");

            return await picker.PickSingleFolderAsync();
        }

        public static string FormatDate(DateTimeOffset now)
        {
            return now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        private static string FormatTimeHHmm(DateTimeOffset now)
        {
            return now.ToString("HHmm", CultureInfo.InvariantCulture);
        }

        private static async Task<bool> ConfirmOverwriteFileAsync(XamlRoot xamlRoot, string filePath)
        {
            var dialog = new ContentDialog
            {
                Title = L10n.Get("Common_ConfirmOverwrite_Title"),
                Content = L10n.Format("Export_OverwriteFile_Content_Fmt", filePath),
                PrimaryButtonText = L10n.Get("Common_Overwrite"),
                CloseButtonText = L10n.Get("Common_Cancel"),
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = xamlRoot,
            };

            ContentDialogResult result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary;
        }

        public static async Task<bool> ConfirmOverwriteFilesAsync(XamlRoot xamlRoot, string folderPath, List<string> conflictPaths)
        {
            if (conflictPaths is null || conflictPaths.Count <= 0)
            {
                return true;
            }

            int count = conflictPaths.Count;
            string preview = count <= 3
                ? string.Join("\n", conflictPaths)
                : string.Join("\n", conflictPaths.GetRange(0, 3)) + "\n" + L10n.Format("Export_OverwritePreview_More_Fmt", count);

            var dialog = new ContentDialog
            {
                Title = L10n.Get("Common_ConfirmOverwrite_Title"),
                Content = L10n.Format("Export_OverwriteFiles_Content_Fmt", folderPath, preview),
                PrimaryButtonText = L10n.Get("Common_Overwrite"),
                CloseButtonText = L10n.Get("Common_Cancel"),
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = xamlRoot,
            };

            ContentDialogResult result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary;
        }
    }
}

