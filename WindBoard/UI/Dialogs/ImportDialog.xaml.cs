using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using WindBoard.Board.Persistence.Wbix;
using WindBoard.Importing;
using WindBoard.Localization;
using WindBoard.Logging;

namespace WindBoard.UI.Dialogs
{
    public sealed partial class ImportDialog : ContentDialog
    {
        private readonly IntPtr _hwnd;

        private StorageFile? _selectedWbixFile;
        private WbixPreviewReader.WbixPreview? _selectedWbixPreview;

        public ObservableCollection<StorageFile> ImageFiles { get; } = new();

        public ObservableCollection<StorageFile> MediaFiles { get; } = new();

        public ObservableCollection<StorageFile> TextFiles { get; } = new();

        internal ImportElementsRequest? ElementsRequest { get; private set; }

        internal ImportWbixRequest? WbixRequest { get; private set; }

        public ImportDialog(IntPtr hwnd)
        {
            _hwnd = hwnd;
            InitializeComponent();

            IsPrimaryButtonEnabled = false;
            PrimaryButtonClick += OnPrimaryButtonClick;

            ImageFiles.CollectionChanged += (_, _) => UpdatePrimaryButtonState();
            MediaFiles.CollectionChanged += (_, _) => UpdatePrimaryButtonState();
            TextFiles.CollectionChanged += (_, _) => UpdatePrimaryButtonState();
        }

        private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            ElementsRequest = null;
            WbixRequest = null;
            DialogInfoBar.IsOpen = false;

            // WBIX 导入：与其它导入互斥（旧版体验也是“选了 WBIX 就只导入 WBIX”）。
            if (_selectedWbixFile is StorageFile wbix)
            {
                if (_selectedWbixPreview is null)
                {
                    args.Cancel = true;
                    ShowDialogWarning(L10n.Get("ImportDialog_Wbix_Invalid_Message"));
                    return;
                }

                ImportWbixMode mode = WbixReplaceCurrentPageRadioButton.IsChecked == true
                    ? ImportWbixMode.ReplaceCurrentPage
                    : ImportWbixMode.AppendAfterLastPage;

                WbixRequest = new ImportWbixRequest(wbix, mode);
                return;
            }

            string? textContent = string.IsNullOrWhiteSpace(TextContentTextBox.Text) ? null : TextContentTextBox.Text;
            string? linkLines = string.IsNullOrWhiteSpace(LinkLinesTextBox.Text) ? null : LinkLinesTextBox.Text;

            int linkCount = ImportUrlNormalizer.ParseAndNormalizeLinkLines(linkLines).Count;
            int count = ImageFiles.Count + MediaFiles.Count + TextFiles.Count + (string.IsNullOrWhiteSpace(textContent) ? 0 : 1) + linkCount;

            if (count <= 0)
            {
                args.Cancel = true;
                ShowDialogWarning(L10n.Get("ImportDialog_NothingToImport_Message"));
                return;
            }

            ElementsRequest = new ImportElementsRequest(
                ImageFiles.ToList(),
                MediaFiles.ToList(),
                TextFiles.ToList(),
                textContent,
                linkLines);
        }

        private void ShowDialogWarning(string message)
        {
            DialogInfoBar.Message = message;
            DialogInfoBar.Severity = InfoBarSeverity.Warning;
            DialogInfoBar.IsOpen = true;
        }

        private void UpdatePrimaryButtonState()
        {
            DialogInfoBar.IsOpen = false;

            bool hasWbix = _selectedWbixFile is not null && _selectedWbixPreview is not null;

            string? textContent = TextContentTextBox?.Text;
            string? linkLines = LinkLinesTextBox?.Text;
            bool hasLinks = ImportUrlNormalizer.ParseAndNormalizeLinkLines(linkLines).Count > 0;

            bool hasAny = ImageFiles.Count > 0
                || MediaFiles.Count > 0
                || TextFiles.Count > 0
                || !string.IsNullOrWhiteSpace(textContent)
                || hasLinks;

            IsPrimaryButtonEnabled = hasWbix || hasAny;
        }

        private async void OnPickImagesClicked(object sender, RoutedEventArgs e)
        {
            IReadOnlyList<StorageFile>? files = await PickMultipleFilesAsync(
                ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp");
            AddFilesUnique(ImageFiles, files);
        }

        private void OnClearImagesClicked(object sender, RoutedEventArgs e)
        {
            ImageFiles.Clear();
        }

        private async void OnPickMediaClicked(object sender, RoutedEventArgs e)
        {
            IReadOnlyList<StorageFile>? files = await PickMultipleFilesAsync(
                ".mp4", ".mov", ".mkv", ".wmv", ".avi", ".webm",
                ".mp3", ".wav", ".m4a", ".aac", ".flac", ".ogg");
            AddFilesUnique(MediaFiles, files);
        }

        private void OnClearMediaClicked(object sender, RoutedEventArgs e)
        {
            MediaFiles.Clear();
        }

        private async void OnPickTextFilesClicked(object sender, RoutedEventArgs e)
        {
            IReadOnlyList<StorageFile>? files = await PickMultipleFilesAsync(
                ".txt", ".md", ".log", ".json", ".url");
            AddFilesUnique(TextFiles, files);
        }

        private void OnClearTextClicked(object sender, RoutedEventArgs e)
        {
            TextFiles.Clear();
            TextContentTextBox.Text = string.Empty;
        }

        private void OnTextContentChanged(object sender, TextChangedEventArgs e)
        {
            UpdatePrimaryButtonState();
        }

        private async void OnPasteTextClicked(object sender, RoutedEventArgs e)
        {
            string? text = await TryGetClipboardTextAsync();
            if (text is null)
            {
                return;
            }

            TextContentTextBox.Text = text;
        }

        private void OnLinkLinesChanged(object sender, TextChangedEventArgs e)
        {
            UpdatePrimaryButtonState();
        }

        private async void OnPasteLinksClicked(object sender, RoutedEventArgs e)
        {
            string? text = await TryGetClipboardTextAsync();
            if (text is null)
            {
                return;
            }

            LinkLinesTextBox.Text = text;
        }

        private void OnClearLinksClicked(object sender, RoutedEventArgs e)
        {
            LinkLinesTextBox.Text = string.Empty;
        }

        private async void OnPickWbixClicked(object sender, RoutedEventArgs e)
        {
            StorageFile? file = await PickSingleFileAsync(".wbix");
            if (file is null)
            {
                return;
            }

            await LoadWbixPreviewAsync(file);
        }

        private void OnClearWbixClicked(object sender, RoutedEventArgs e)
        {
            _selectedWbixFile = null;
            _selectedWbixPreview = null;
            ClearWbixButton.Visibility = Visibility.Collapsed;
            WbixPreviewBorder.Visibility = Visibility.Collapsed;
            WbixCoverImage.Source = null;
            WbixCoverImage.Visibility = Visibility.Collapsed;
            WbixCoverFallbackBorder.Visibility = Visibility.Visible;
            WbixInfoTextBlock.Text = string.Empty;
            UpdatePrimaryButtonState();
        }

        private async Task LoadWbixPreviewAsync(StorageFile file)
        {
            _selectedWbixFile = file;
            _selectedWbixPreview = null;

            ClearWbixButton.Visibility = Visibility.Visible;

            try
            {
                WbixPreviewReader.WbixPreview? preview = await WbixPreviewReader.TryReadAsync(file.Path);
                if (preview is null)
                {
                    ShowDialogWarning(L10n.Get("ImportDialog_Wbix_Invalid_Message"));
                    WbixPreviewBorder.Visibility = Visibility.Collapsed;
                    UpdatePrimaryButtonState();
                    return;
                }

                _selectedWbixPreview = preview;

                int pageCount = preview.Manifest.Pages?.Count ?? 0;
                string created = preview.Manifest.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
                string info = L10n.Format("Import_Wbix_Info_Fmt", file.Name, pageCount, preview.Manifest.Version, created);
                WbixInfoTextBlock.Text = info;

                await ApplyWbixCoverAsync(preview.CoverPngBytes);

                WbixPreviewBorder.Visibility = Visibility.Visible;
                UpdatePrimaryButtonState();
            }
            catch (Exception ex)
            {
                AppLog.Warn("WBIX", $"预览失败：'{file.Path}'", ex);
                ShowDialogWarning(L10n.Get("ImportDialog_Wbix_Invalid_Message"));
                WbixPreviewBorder.Visibility = Visibility.Collapsed;
                _selectedWbixPreview = null;
                UpdatePrimaryButtonState();
            }
        }

        private async Task ApplyWbixCoverAsync(byte[]? pngBytes)
        {
            if (pngBytes is not { Length: > 0 })
            {
                WbixCoverImage.Source = null;
                WbixCoverImage.Visibility = Visibility.Collapsed;
                WbixCoverFallbackBorder.Visibility = Visibility.Visible;
                return;
            }

            BitmapImage? bitmap = await TryCreateBitmapImageAsync(pngBytes);
            if (bitmap is null)
            {
                WbixCoverImage.Source = null;
                WbixCoverImage.Visibility = Visibility.Collapsed;
                WbixCoverFallbackBorder.Visibility = Visibility.Visible;
                return;
            }

            WbixCoverImage.Source = bitmap;
            WbixCoverImage.Visibility = Visibility.Visible;
            WbixCoverFallbackBorder.Visibility = Visibility.Collapsed;
        }

        private async Task<IReadOnlyList<StorageFile>?> PickMultipleFilesAsync(params string[] extensions)
        {
            if (_hwnd == IntPtr.Zero)
            {
                ShowDialogWarning(L10n.Get("Common_WindowHandleFailed_Message"));
                AppLog.Warn("Import", "无法打开文件选择器：窗口句柄不可用。");
                return null;
            }

            try
            {
                var picker = new FileOpenPicker();
                WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);

                picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
                picker.FileTypeFilter.Clear();
                foreach (string ext in extensions)
                {
                    picker.FileTypeFilter.Add(ext);
                }

                return await picker.PickMultipleFilesAsync();
            }
            catch (Exception ex)
            {
                AppLog.Warn("Import", $"打开文件选择器失败：extensions='{string.Join(",", extensions)}'", ex);
                ShowDialogWarning(L10n.Format("ImportDialog_FilePicker_Failed_Fmt", ex.Message));
                return null;
            }
        }

        private async Task<StorageFile?> PickSingleFileAsync(string extension)
        {
            if (_hwnd == IntPtr.Zero)
            {
                ShowDialogWarning(L10n.Get("Common_WindowHandleFailed_Message"));
                AppLog.Warn("Import", "无法打开文件选择器：窗口句柄不可用。");
                return null;
            }

            try
            {
                var picker = new FileOpenPicker();
                WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);

                picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
                picker.FileTypeFilter.Clear();
                picker.FileTypeFilter.Add(extension);

                return await picker.PickSingleFileAsync();
            }
            catch (Exception ex)
            {
                AppLog.Warn("Import", $"打开文件选择器失败：extension='{extension}'", ex);
                ShowDialogWarning(L10n.Format("ImportDialog_FilePicker_Failed_Fmt", ex.Message));
                return null;
            }
        }

        private static void AddFilesUnique(ObservableCollection<StorageFile> target, IReadOnlyList<StorageFile>? files)
        {
            if (files is null || files.Count == 0)
            {
                return;
            }

            var existing = target
                .Where(f => !string.IsNullOrWhiteSpace(f.Path))
                .Select(f => f.Path)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < files.Count; i++)
            {
                StorageFile file = files[i];
                if (string.IsNullOrWhiteSpace(file.Path))
                {
                    continue;
                }

                if (existing.Add(file.Path))
                {
                    target.Add(file);
                }
            }
        }

        private static async Task<string?> TryGetClipboardTextAsync()
        {
            try
            {
                DataPackageView view = Clipboard.GetContent();
                if (!view.Contains(StandardDataFormats.Text))
                {
                    return null;
                }

                return await view.GetTextAsync();
            }
            catch
            {
                return null;
            }
        }

        private static async Task<BitmapImage?> TryCreateBitmapImageAsync(byte[] pngBytes)
        {
            try
            {
                using var stream = new InMemoryRandomAccessStream();
                await stream.WriteAsync(pngBytes.AsBuffer());
                stream.Seek(0);

                var bitmap = new BitmapImage();
                await bitmap.SetSourceAsync(stream);
                return bitmap;
            }
            catch
            {
                return null;
            }
        }
    }
}
