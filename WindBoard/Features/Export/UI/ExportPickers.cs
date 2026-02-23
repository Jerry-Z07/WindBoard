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
            if (xamlRoot is null)
            {
                throw new ArgumentNullException(nameof(xamlRoot));
            }

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
            if (xamlRoot is null)
            {
                throw new ArgumentNullException(nameof(xamlRoot));
            }

            while (true)
            {
                DateTimeOffset pickStarted = DateTimeOffset.Now;
                StorageFile? file = await PickSaveFileAsync(xamlRoot, hwnd, format);
                if (file is null)
                {
                    return null;
                }

                if (!File.Exists(file.Path))
                {
                    return file;
                }

                // WinUI 的 FileSavePicker 在某些实现下会“先创建一个空文件再返回 StorageFile”。
                // 这种情况下 File.Exists 会恒为 true；为避免每次保存都弹覆盖确认，这里用 DateCreated 做一个保守判断：
                // - 如果文件创建时间明显早于打开对话框的时间，则认为是“已存在文件”，需要二次确认；
                // - 否则认为是“刚创建的新文件”，直接继续导出。
                if (file.DateCreated >= pickStarted - TimeSpan.FromSeconds(2))
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
            if (xamlRoot is null)
            {
                throw new ArgumentNullException(nameof(xamlRoot));
            }

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

