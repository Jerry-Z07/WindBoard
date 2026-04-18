using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using DevWinUI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using WindBoard.Features.Import.Models;
using WindBoard.Features.Import.Services;
using WindBoard.Localization;
using WindBoard.Logging;
using WindBoard.UI.Common;

namespace WindBoard.Features.Import.UI
{
    public sealed partial class ImportDialog : ContentDialog
    {
        /// <summary>
        /// TreeView 节点展示信息（作为 TreeViewNode.Content）。
        /// </summary>
        private sealed class ImportQueueNodeInfo
        {
            public required bool IsGroup { get; init; }

            public required ImportQueueGroup Group { get; init; }

            public required Symbol Icon { get; init; }

            public required string Title { get; init; }

            public string? Subtitle { get; init; }

            public Visibility SubtitleVisibility => string.IsNullOrWhiteSpace(Subtitle) ? Visibility.Collapsed : Visibility.Visible;

            public required Visibility RemoveButtonVisibility { get; init; }

            public required Guid ItemId { get; init; }
        }

        private readonly IntPtr _hwnd;

        private StorageFile? _selectedWorkspaceFile;
        private ImportWorkspacePreview? _workspacePreview;
        private WindowedContentDialog? _windowedHost;

        private readonly ImportQueueState _queue = new();

        internal ImportDialogSubmission? Submission { get; private set; }

        public ImportDialog(IntPtr hwnd)
        {
            _hwnd = hwnd;
            InitializeComponent();

            // 说明：ContentDialog 的命令按钮样式（主按钮/关闭按钮）在不同系统上可能呈现“直角/拉伸铺满”的旧观感，
            // 这里统一覆写为与应用其它区域一致的圆角按钮样式。
            try
            {
                PrimaryButtonStyle = (Style)Resources["ImportDialogPrimaryButtonStyle"];
                CloseButtonStyle = (Style)Resources["ImportDialogCloseButtonStyle"];
            }
            catch (Exception ex)
            {
                // 样式问题不应导致导入功能不可用：记录日志后回退到默认样式。
                AppLog.Warn("Import", $"应用导入弹窗按钮样式失败，将回退到默认样式：{ex.Message}");
            }

            IsPrimaryButtonEnabled = false;
            PrimaryButtonClick += OnPrimaryButtonClick;

            // 默认选中第一项（文件）。这里放到代码中设置，避免在 XAML 里直接写 IsSelected 触发异常情况。
            ImportNavView.SelectedItem = ImportNavView.MenuItems.OfType<NavigationViewItem>().FirstOrDefault();

            UpdateDraftActionButtonStates();
            RefreshQueueEmptyHintState();
        }

        private void OnTextDraftTextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateDraftActionButtonStates();
        }

        private void OnLinkDraftTextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateDraftActionButtonStates();
        }

        private void UpdateDraftActionButtonStates()
        {
            AddTextToQueueButton.IsEnabled = !string.IsNullOrWhiteSpace(TextDraftTextBox.Text);
            AddLinksToQueueButton.IsEnabled = !string.IsNullOrWhiteSpace(LinkDraftTextBox.Text);
        }

        internal void AttachWindowedHost(WindowedContentDialog host, WindowedDialogPresentationPlan presentationPlan)
        {
            _windowedHost = host ?? throw new ArgumentNullException(nameof(host));
            DialogRootGrid.Width = presentationPlan.InitialWidth;
            SyncWindowedHostState();
        }

        internal object? DetachContentForWindowedHost()
        {
            object? content = Content;
            Content = null;
            return content;
        }

        internal void DetachWindowedHost()
        {
            _windowedHost = null;
            DialogRootGrid.Width = double.NaN;
        }

        internal void OnWindowedPrimaryButtonClick(WindowedContentDialog sender, CancelEventArgs args)
        {
            if (!TryCaptureSubmission())
            {
                args.Cancel = true;
            }
        }

        private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            if (!TryCaptureSubmission())
            {
                args.Cancel = true;
            }
        }

        private bool TryCaptureSubmission()
        {
            Submission = null;
            DialogInfoBar.IsOpen = false;

            ImportWbixMode mode = WbixReplaceCurrentPageRadioButton.IsChecked == true
                ? ImportWbixMode.ReplaceCurrentPage
                : ImportWbixMode.AppendAfterLastPage;

            ImportQueueBuildResult result = _queue.TryBuildSubmission(mode, hasValidWorkspacePreview: _workspacePreview is not null);
            if (!result.Success || result.Submission is null)
            {
                ShowDialogWarning(result.Error == ImportQueueBuildErrorKind.InvalidWorkspace
                    ? L10n.Get("ImportDialog_Wbix_Invalid_Message")
                    : L10n.Get("ImportDialog_NothingToImport_Message"));
                return false;
            }

            Submission = result.Submission;
            return true;
        }

        private void SyncWindowedHostState()
        {
            if (_windowedHost is null)
            {
                return;
            }

            _windowedHost.IsPrimaryButtonEnabled = IsPrimaryButtonEnabled;
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

            bool hasWorkspace = _queue.WorkspaceItemId is Guid workspaceItemId
                && workspaceItemId != Guid.Empty
                && _workspacePreview is not null;

            bool hasAnyElements = _queue.WorkspaceItemId is null && _queue.Count > 0;

            IsPrimaryButtonEnabled = hasWorkspace || hasAnyElements;
            SyncWindowedHostState();
            RefreshQueueEmptyHintState();
        }

        private void OnImportNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.SelectedItem is not NavigationViewItem item || item.Tag is not string tag)
            {
                return;
            }

            // 切换导入类型时收起提示，避免用户看到“上一页”的警告信息。
            DialogInfoBar.IsOpen = false;

            FileImportPanel.Visibility = tag == "file" ? Visibility.Visible : Visibility.Collapsed;
            TextLinkImportPanel.Visibility = tag == "textLink" ? Visibility.Visible : Visibility.Collapsed;
        }

        private void RefreshQueueEmptyHintState()
        {
            QueueEmptyHintTextBlock.Visibility = _queue.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void RebuildQueueTreeView()
        {
            ImportQueueTreeView.RootNodes.Clear();

            for (int gi = 0; gi < ImportQueueState.DisplayGroupOrder.Length; gi++)
            {
                ImportQueueGroup group = ImportQueueState.DisplayGroupOrder[gi];
                IReadOnlyList<ImportQueueItem> items = _queue.GetItemsByGroup(group);
                if (items.Count == 0)
                {
                    continue;
                }

                var groupNodeInfo = new ImportQueueNodeInfo
                {
                    IsGroup = true,
                    Group = group,
                    Icon = ResolveGroupIcon(group),
                    Title = GetGroupTitle(group),
                    Subtitle = null,
                    RemoveButtonVisibility = Visibility.Collapsed,
                    ItemId = Guid.Empty,
                };

                var groupNode = new TreeViewNode { Content = groupNodeInfo, IsExpanded = true };

                for (int i = 0; i < items.Count; i++)
                {
                    ImportQueueItem item = items[i];

                    var nodeInfo = new ImportQueueNodeInfo
                    {
                        IsGroup = false,
                        Group = item.Group,
                        Icon = ResolveLeafIcon(item.Kind),
                        Title = item.DisplayTitle,
                        Subtitle = item.DisplaySubtitle,
                        RemoveButtonVisibility = Visibility.Visible,
                        ItemId = item.Id,
                    };

                    groupNode.Children.Add(new TreeViewNode { Content = nodeInfo });
                }

                ImportQueueTreeView.RootNodes.Add(groupNode);
            }

            RefreshQueueEmptyHintState();
        }

        private async void OnPickFilesClicked(object sender, TappedRoutedEventArgs e)
        {
            e.Handled = true;

            IReadOnlyList<StorageFile>? files = await PickMultipleFilesAsync("*");
            await AddFilesToQueueAsync(files, source: "picker");
        }

        private void OnFileDropZoneDragOver(object sender, DragEventArgs e)
        {
            e.Handled = true;

            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                e.AcceptedOperation = DataPackageOperation.Copy;
                return;
            }

            e.AcceptedOperation = DataPackageOperation.None;
        }

        private async void OnFileDropZoneDrop(object sender, DragEventArgs e)
        {
            e.Handled = true;

            try
            {
                if (!e.DataView.Contains(StandardDataFormats.StorageItems))
                {
                    return;
                }

                IReadOnlyList<IStorageItem> items = await e.DataView.GetStorageItemsAsync();
                IReadOnlyList<StorageFile> files = items.OfType<StorageFile>().ToList();
                await AddFilesToQueueAsync(files, source: "drop");
            }
            catch (Exception ex)
            {
                AppLog.Warn("Import", "处理拖拽导入失败。", ex);
                ShowDialogWarning(L10n.Get("ImportDialog_FileDrop_Failed_Message"));
            }
        }

        private async Task AddFilesToQueueAsync(IReadOnlyList<StorageFile>? files, string source)
        {
            if (files is null || files.Count == 0)
            {
                return;
            }

            ImportQueueAddFilesResult result = _queue.AddFiles(files);
            if (!result.Success && result.Error == ImportQueueAddFilesErrorKind.WorkspaceExclusive)
            {
                AppLog.Warn("Import", $"已选择工作区文件，忽略添加其它文件：source={source}, count={files.Count}");
                ShowDialogWarning(L10n.Get("ImportDialog_WorkspaceExclusive_Message"));
                return;
            }

            if (result.WorkspaceFile is StorageFile workspaceFile)
            {
                bool shouldWarn = result.WorkspaceExclusiveWarning;

                // 先清理旧预览，避免异步预读时出现“残留上一份预览”的闪烁。
                ClearWorkspaceState();
                RebuildQueueTreeView();

                // 警告优先显示：若随后预读失败，会被“文件无效”提示覆盖，避免误导用户。
                if (shouldWarn)
                {
                    ShowDialogWarning(L10n.Get("ImportDialog_WorkspaceExclusive_Message"));
                }

                await LoadWorkspacePreviewAsync(workspaceFile);
                // 注意：LoadWorkspacePreviewAsync 内部会触发 UpdatePrimaryButtonState。
                return;
            }

            RebuildQueueTreeView();
            UpdatePrimaryButtonState();
        }

        private void OnAddTextToQueueClicked(object sender, RoutedEventArgs e)
        {
            ImportQueueAddTextResult result = _queue.AddText(TextDraftTextBox.Text);
            if (!result.Success)
            {
                if (result.Error == ImportQueueAddTextErrorKind.WorkspaceExclusive)
                {
                    ShowDialogWarning(L10n.Get("ImportDialog_WorkspaceExclusive_Message"));
                    return;
                }

                ShowDialogWarning(L10n.Get("Import_Text_Empty_Message"));
                return;
            }

            TextDraftTextBox.Text = string.Empty;

            RebuildQueueTreeView();
            UpdatePrimaryButtonState();
        }

        private void OnClearTextDraftClicked(object sender, RoutedEventArgs e)
        {
            TextDraftTextBox.Text = string.Empty;
        }

        private async void OnPasteTextClicked(object sender, RoutedEventArgs e)
        {
            string? text = await TryGetClipboardTextAsync();
            if (text is null)
            {
                return;
            }

            TextDraftTextBox.Text = text;
        }

        private void OnAddLinksToQueueClicked(object sender, RoutedEventArgs e)
        {
            ImportQueueAddLinksResult result = _queue.AddLinks(LinkDraftTextBox.Text);
            if (!result.Success)
            {
                if (result.Error == ImportQueueAddLinksErrorKind.WorkspaceExclusive)
                {
                    ShowDialogWarning(L10n.Get("ImportDialog_WorkspaceExclusive_Message"));
                    return;
                }

                ShowDialogWarning(L10n.Get("ImportDialog_NoValidLinks_Message"));
                return;
            }

            if (result.Added > 0)
            {
                LinkDraftTextBox.Text = string.Empty;
            }

            RebuildQueueTreeView();
            UpdatePrimaryButtonState();
        }

        private void OnClearLinksDraftClicked(object sender, RoutedEventArgs e)
        {
            LinkDraftTextBox.Text = string.Empty;
        }

        private async void OnPasteLinksClicked(object sender, RoutedEventArgs e)
        {
            string? text = await TryGetClipboardTextAsync();
            if (text is null)
            {
                return;
            }

            LinkDraftTextBox.Text = text;
        }

        private void OnClearQueueClicked(object sender, RoutedEventArgs e)
        {
            _queue.Clear();
            UpdatePrimaryButtonState();
            ClearWorkspaceState();
            RebuildQueueTreeView();
        }

        private void OnQueueRemoveClicked(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button)
            {
                return;
            }

            Guid itemId = button.Tag switch
            {
                Guid g => g,
                string s when Guid.TryParse(s, out Guid parsed) => parsed,
                _ => Guid.Empty,
            };

            if (itemId == Guid.Empty)
            {
                return;
            }

            if (!_queue.TryRemove(itemId, out ImportQueueItem? removed) || removed is null)
            {
                return;
            }

            if (removed.Kind is ImportQueueItemKind.WorkspaceWbix or ImportQueueItemKind.WorkspaceWbi)
            {
                ClearWorkspaceState();
            }

            RebuildQueueTreeView();
            UpdatePrimaryButtonState();
        }

        private void OnClearWbixClicked(object sender, RoutedEventArgs e)
        {
            // “从队列移除”语义：移除工作区队列项 + 清空预览状态。
            if (_queue.WorkspaceItemId is Guid workspaceId && workspaceId != Guid.Empty)
            {
                _ = _queue.TryRemove(workspaceId, out _);

                ClearWorkspaceState();
                RebuildQueueTreeView();
                UpdatePrimaryButtonState();
                return;
            }

            ClearWorkspaceState();
            UpdatePrimaryButtonState();
        }

        private void ClearWorkspaceState()
        {
            _selectedWorkspaceFile = null;
            _workspacePreview = null;
            ClearWbixButton.Visibility = Visibility.Collapsed;
            WbixPreviewBorder.Visibility = Visibility.Collapsed;
            WbixCoverImage.Source = null;
            WbixCoverImageBorder.Visibility = Visibility.Collapsed;
            WbixCoverFallbackBorder.Visibility = Visibility.Visible;
            WbixInfoTextBlock.Text = string.Empty;
        }

        private string GetGroupTitle(ImportQueueGroup group)
        {
            return group switch
            {
                ImportQueueGroup.Workspace => L10n.Get("ImportDialog_Group_Workspace"),
                ImportQueueGroup.Image => L10n.Get("ImportDialog_Group_Image"),
                ImportQueueGroup.Video => L10n.Get("ImportDialog_Group_Video"),
                ImportQueueGroup.Audio => L10n.Get("ImportDialog_Group_Audio"),
                ImportQueueGroup.Text => L10n.Get("ImportDialog_Group_Text"),
                ImportQueueGroup.Link => L10n.Get("ImportDialog_Group_Link"),
                ImportQueueGroup.File => L10n.Get("ImportDialog_Group_File"),
                _ => L10n.Get("ImportDialog_Group_File"),
            };
        }

        private static Symbol ResolveGroupIcon(ImportQueueGroup group)
        {
            // 尽量使用项目内已使用过的 Symbol，避免不同 SDK 版本下出现不存在的枚举值。
            return group switch
            {
                ImportQueueGroup.Workspace => Symbol.OpenFile,
                ImportQueueGroup.Image => Symbol.Pictures,
                ImportQueueGroup.Video => Symbol.Video,
                ImportQueueGroup.Audio => ResolveAudioIconSymbol(),
                ImportQueueGroup.Text => Symbol.Edit,
                ImportQueueGroup.Link => Symbol.Link,
                ImportQueueGroup.File => Symbol.OpenFile,
                _ => Symbol.OpenFile,
            };
        }

        private static Symbol ResolveAudioIconSymbol()
        {
            // 音频图标：优先使用更贴近语义的 MusicInfo（若目标 SDK 不存在该枚举值则降级）。
            // 说明：使用 TryParse 避免在不同 SDK/WinUI 版本下直接引用不存在的 Symbol 成员导致编译失败。
            if (Enum.TryParse("MusicInfo", ignoreCase: true, out Symbol musicInfo))
            {
                return musicInfo;
            }

            if (Enum.TryParse("Volume", ignoreCase: true, out Symbol volume))
            {
                return volume;
            }

            return Symbol.Video;
        }

        private static Symbol ResolveLeafIcon(ImportQueueItemKind kind)
        {
            return kind switch
            {
                ImportQueueItemKind.WorkspaceWbix => Symbol.OpenFile,
                ImportQueueItemKind.WorkspaceWbi => Symbol.OpenFile,
                ImportQueueItemKind.ImageFile => Symbol.Pictures,
                ImportQueueItemKind.VideoFile => Symbol.Video,
                ImportQueueItemKind.AudioFile => ResolveAudioIconSymbol(),
                ImportQueueItemKind.TextFile => Symbol.Edit,
                ImportQueueItemKind.InternetShortcutFile => Symbol.Link,
                ImportQueueItemKind.GenericFile => Symbol.OpenFile,
                ImportQueueItemKind.TextContent => Symbol.Edit,
                ImportQueueItemKind.LinkUrl => Symbol.Link,
                _ => Symbol.OpenFile,
            };
        }

        private async Task LoadWorkspacePreviewAsync(StorageFile file)
        {
            _selectedWorkspaceFile = file;
            _workspacePreview = null;

            ClearWbixButton.Visibility = Visibility.Visible;

            try
            {
                ImportWorkspacePreview? preview = await ImportWorkspacePreviewService.TryLoadAsync(file);
                if (preview is null)
                {
                    WbixPreviewBorder.Visibility = Visibility.Collapsed;
                    UpdatePrimaryButtonState();
                    ShowDialogWarning(L10n.Get("ImportDialog_Wbix_Invalid_Message"));
                    return;
                }

                _workspacePreview = preview;

                string created = preview.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
                string info = L10n.Format("Import_Wbix_Info_Fmt", file.Name, preview.PageCount, preview.Version, created);
                WbixInfoTextBlock.Text = info;

                if (preview.Kind == ImportWorkspacePreviewKind.Wbix)
                {
                    await ApplyWbixCoverAsync(preview.CoverPngBytes);
                }
                else
                {
                    // WBI 没有封面：强制回退到占位卡片，避免遗留上一张预览的封面。
                    await ApplyWbixCoverAsync(null);
                }

                WbixPreviewBorder.Visibility = Visibility.Visible;
                UpdatePrimaryButtonState();
            }
            catch (Exception ex)
            {
                AppLog.Warn("Import", $"预览失败：'{file.Path}'", ex);
                WbixPreviewBorder.Visibility = Visibility.Collapsed;
                _workspacePreview = null;
                UpdatePrimaryButtonState();
                ShowDialogWarning(L10n.Get("ImportDialog_Wbix_Invalid_Message"));
            }
        }

        private async Task ApplyWbixCoverAsync(byte[]? pngBytes)
        {
            if (pngBytes is not { Length: > 0 })
            {
                WbixCoverImage.Source = null;
                WbixCoverImageBorder.Visibility = Visibility.Collapsed;
                WbixCoverFallbackBorder.Visibility = Visibility.Visible;
                return;
            }

            BitmapImage? bitmap = await TryCreateBitmapImageAsync(pngBytes);
            if (bitmap is null)
            {
                WbixCoverImage.Source = null;
                WbixCoverImageBorder.Visibility = Visibility.Collapsed;
                WbixCoverFallbackBorder.Visibility = Visibility.Visible;
                return;
            }

            WbixCoverImage.Source = bitmap;
            WbixCoverImageBorder.Visibility = Visibility.Visible;
            WbixCoverFallbackBorder.Visibility = Visibility.Collapsed;
        }

        private async Task<IReadOnlyList<StorageFile>?> PickMultipleFilesAsync(params string[] extensions)
        {
            IntPtr ownerHwnd = _windowedHost is not null
                ? Microsoft.UI.Win32Interop.GetWindowFromWindowId(DialogRootGrid.XamlRoot.ContentIslandEnvironment.AppWindowId)
                : _hwnd;

            if (ownerHwnd == IntPtr.Zero)
            {
                ShowDialogWarning(L10n.Get("Common_WindowHandleFailed_Message"));
                AppLog.Warn("Import", "无法打开文件选择器：窗口句柄不可用。");
                return null;
            }

            try
            {
                var picker = new FileOpenPicker();
                WinRT.Interop.InitializeWithWindow.Initialize(picker, ownerHwnd);

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
