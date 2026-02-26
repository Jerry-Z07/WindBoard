using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using WindBoard.Board.Persistence.Wbix;
using WindBoard.Features.Import.Models;
using WindBoard.Features.Import.Services;
using WindBoard.Features.Import.Wbi;
using WindBoard.Localization;
using WindBoard.Logging;

namespace WindBoard.Features.Import.UI
{
    public sealed partial class ImportDialog : ContentDialog
    {
        private enum ImportQueueGroup
        {
            Workspace,
            Image,
            Video,
            Audio,
            Text,
            Link,
            File,
        }

        private enum ImportQueueItemKind
        {
            WorkspaceWbix,
            WorkspaceWbi,
            ImageFile,
            VideoFile,
            AudioFile,
            TextFile,
            InternetShortcutFile,
            GenericFile,
            TextContent,
            LinkUrl,
        }

        private sealed class ImportQueueItem
        {
            public required Guid Id { get; init; }

            public required ImportQueueItemKind Kind { get; init; }

            public required ImportQueueGroup Group { get; init; }

            public required string DisplayTitle { get; init; }

            public string? DisplaySubtitle { get; init; }

            public StorageFile? File { get; init; }

            public string? TextContent { get; init; }

            public string? Url { get; init; }

            public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
        }

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
        private WbixPreviewReader.WbixPreview? _selectedWbixPreview;
        private WbiPreviewReader.WbiPreview? _selectedWbiPreview;

        private readonly Dictionary<Guid, ImportQueueItem> _queueById = new();
        private readonly Dictionary<ImportQueueGroup, TreeViewNode> _groupNodes = new();
        private readonly Dictionary<Guid, TreeViewNode> _leafNodesByItemId = new();

        private readonly HashSet<string> _filePathSet = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _urlSet = new(StringComparer.OrdinalIgnoreCase);

        private Guid? _workspaceItemId;
        private Guid? _textContentItemId;

        internal ImportElementsRequest? ElementsRequest { get; private set; }

        internal ImportWbixRequest? WbixRequest { get; private set; }

        internal ImportWbiRequest? WbiRequest { get; private set; }

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

            RefreshQueueEmptyHintState();
        }

        private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            ElementsRequest = null;
            WbixRequest = null;
            WbiRequest = null;
            DialogInfoBar.IsOpen = false;

            // 工作区导入：与元素导入互斥。
            if (_workspaceItemId is Guid workspaceItemId && workspaceItemId != Guid.Empty)
            {
                if (!_queueById.TryGetValue(workspaceItemId, out ImportQueueItem? workspaceItem)
                    || workspaceItem.File is not StorageFile workspaceFile)
                {
                    args.Cancel = true;
                    ShowDialogWarning(L10n.Get("ImportDialog_Wbix_Invalid_Message"));
                    return;
                }

                string ext = Path.GetExtension(workspaceFile.Name);

                if (string.Equals(ext, ".wbix", StringComparison.OrdinalIgnoreCase))
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

                    WbixRequest = new ImportWbixRequest(workspaceFile, mode);
                    return;
                }

                if (string.Equals(ext, ".wbi", StringComparison.OrdinalIgnoreCase))
                {
                    if (_selectedWbiPreview is null)
                    {
                        args.Cancel = true;
                        ShowDialogWarning(L10n.Get("ImportDialog_Wbix_Invalid_Message"));
                        return;
                    }

                    ImportWbixMode mode = WbixReplaceCurrentPageRadioButton.IsChecked == true
                        ? ImportWbixMode.ReplaceCurrentPage
                        : ImportWbixMode.AppendAfterLastPage;

                    WbiRequest = new ImportWbiRequest(workspaceFile, mode);
                    return;
                }

                // 理论上不会到这里：工作区队列只允许 .wbix/.wbi。
                args.Cancel = true;
                ShowDialogWarning(L10n.Get("ImportDialog_Wbix_Invalid_Message"));
                return;
            }

            // 元素导入：从队列构建请求。
            var imageFiles = new List<StorageFile>();
            var mediaFiles = new List<StorageFile>();
            var textFiles = new List<StorageFile>();
            var otherFiles = new List<StorageFile>();
            string? textContent = null;
            var links = new List<string>();

            foreach (ImportQueueItem item in EnumerateQueueItemsInDisplayOrder())
            {
                switch (item.Kind)
                {
                    case ImportQueueItemKind.ImageFile:
                        if (item.File is not null)
                        {
                            imageFiles.Add(item.File);
                        }
                        break;
                    case ImportQueueItemKind.VideoFile:
                    case ImportQueueItemKind.AudioFile:
                        if (item.File is not null)
                        {
                            mediaFiles.Add(item.File);
                        }
                        break;
                    case ImportQueueItemKind.TextFile:
                    case ImportQueueItemKind.InternetShortcutFile:
                        if (item.File is not null)
                        {
                            textFiles.Add(item.File);
                        }
                        break;
                    case ImportQueueItemKind.GenericFile:
                        if (item.File is not null)
                        {
                            otherFiles.Add(item.File);
                        }
                        break;
                    case ImportQueueItemKind.TextContent:
                        if (!string.IsNullOrWhiteSpace(item.TextContent))
                        {
                            textContent = item.TextContent;
                        }
                        break;
                    case ImportQueueItemKind.LinkUrl:
                        if (!string.IsNullOrWhiteSpace(item.Url))
                        {
                            links.Add(item.Url);
                        }
                        break;
                }
            }

            string? linkLines = links.Count > 0 ? string.Join('\n', links) : null;
            int count = imageFiles.Count + mediaFiles.Count + textFiles.Count + otherFiles.Count + (string.IsNullOrWhiteSpace(textContent) ? 0 : 1) + links.Count;

            if (count <= 0)
            {
                args.Cancel = true;
                ShowDialogWarning(L10n.Get("ImportDialog_NothingToImport_Message"));
                return;
            }

            ElementsRequest = new ImportElementsRequest(
                imageFiles,
                mediaFiles,
                textFiles,
                otherFiles,
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

            bool hasWorkspace = _workspaceItemId is Guid workspaceItemId
                && workspaceItemId != Guid.Empty
                && (_selectedWbixPreview is not null || _selectedWbiPreview is not null);

            bool hasAnyElements = _workspaceItemId is null && _queueById.Count > 0;

            IsPrimaryButtonEnabled = hasWorkspace || hasAnyElements;
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

        private static readonly ImportQueueGroup[] GroupOrder =
        {
            ImportQueueGroup.Workspace,
            ImportQueueGroup.Image,
            ImportQueueGroup.Video,
            ImportQueueGroup.Audio,
            ImportQueueGroup.Text,
            ImportQueueGroup.Link,
            ImportQueueGroup.File,
        };

        private void RefreshQueueEmptyHintState()
        {
            QueueEmptyHintTextBlock.Visibility = _queueById.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private IEnumerable<ImportQueueItem> EnumerateQueueItemsInDisplayOrder()
        {
            // 以 TreeView 的展示顺序为准，避免 Dictionary 枚举导致顺序不稳定。
            for (int gi = 0; gi < GroupOrder.Length; gi++)
            {
                ImportQueueGroup group = GroupOrder[gi];
                if (!_groupNodes.TryGetValue(group, out TreeViewNode? groupNode))
                {
                    continue;
                }

                for (int i = 0; i < groupNode.Children.Count; i++)
                {
                    if (groupNode.Children[i].Content is not ImportQueueNodeInfo info)
                    {
                        continue;
                    }

                    if (info.ItemId == Guid.Empty)
                    {
                        continue;
                    }

                    if (_queueById.TryGetValue(info.ItemId, out ImportQueueItem? item))
                    {
                        yield return item;
                    }
                }
            }
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

            // 互斥规则：如果当前已选工作区文件，则禁止加入其它内容，避免“点击导入后行为不确定”。
            if (_workspaceItemId is not null)
            {
                bool containsWorkspace = files.Any(static f =>
                {
                    ImportFileContentKind kind = ImportFileTypeResolver.Resolve(f.Name);
                    return kind is ImportFileContentKind.Wbix or ImportFileContentKind.Wbi;
                });

                if (!containsWorkspace)
                {
                    AppLog.Warn("Import", $"已选择工作区文件，忽略添加其它文件：source={source}, count={files.Count}");
                    ShowDialogWarning(L10n.Get("ImportDialog_WorkspaceExclusive_Message"));
                    return;
                }
            }

            // 若本次选择包含工作区文件，则清空队列，仅保留第一个工作区文件。
            StorageFile? workspaceFile = null;
            ImportFileContentKind workspaceKind = ImportFileContentKind.Other;
            for (int i = 0; i < files.Count; i++)
            {
                StorageFile file = files[i];
                ImportFileContentKind kind = ImportFileTypeResolver.Resolve(file.Name);
                if (kind is ImportFileContentKind.Wbix or ImportFileContentKind.Wbi)
                {
                    workspaceFile = file;
                    workspaceKind = kind;
                    break;
                }
            }

            if (workspaceFile is not null)
            {
                bool shouldWarn = files.Count > 1 || _queueById.Count > 0;
                AppLog.Info("Import", $"添加工作区文件到队列：source={source}, file='{workspaceFile.Path}', warn={shouldWarn}");

                ClearQueueInternal();

                ImportQueueItemKind itemKind = workspaceKind == ImportFileContentKind.Wbix
                    ? ImportQueueItemKind.WorkspaceWbix
                    : ImportQueueItemKind.WorkspaceWbi;

                var item = new ImportQueueItem
                {
                    Id = Guid.NewGuid(),
                    Kind = itemKind,
                    Group = ImportQueueGroup.Workspace,
                    DisplayTitle = workspaceFile.Name,
                    DisplaySubtitle = workspaceFile.Path,
                    File = workspaceFile,
                };

                AddQueueItem(item);
                _workspaceItemId = item.Id;

                await LoadWorkspacePreviewAsync(workspaceFile);

                // 注意：LoadWorkspacePreviewAsync 内部会触发 UpdatePrimaryButtonState。
                if (shouldWarn)
                {
                    ShowDialogWarning(L10n.Get("ImportDialog_WorkspaceExclusive_Message"));
                }

                return;
            }

            int added = 0;
            int skippedDuplicate = 0;
            int skippedInvalid = 0;

            for (int i = 0; i < files.Count; i++)
            {
                StorageFile file = files[i];
                if (string.IsNullOrWhiteSpace(file.Path))
                {
                    skippedInvalid++;
                    continue;
                }

                if (!_filePathSet.Add(file.Path))
                {
                    skippedDuplicate++;
                    continue;
                }

                ImportFileContentKind kind = ImportFileTypeResolver.Resolve(file.Name);
                (ImportQueueItemKind itemKind, ImportQueueGroup group) = kind switch
                {
                    ImportFileContentKind.Image => (ImportQueueItemKind.ImageFile, ImportQueueGroup.Image),
                    ImportFileContentKind.Video => (ImportQueueItemKind.VideoFile, ImportQueueGroup.Video),
                    ImportFileContentKind.Audio => (ImportQueueItemKind.AudioFile, ImportQueueGroup.Audio),
                    ImportFileContentKind.Text => (ImportQueueItemKind.TextFile, ImportQueueGroup.Text),
                    ImportFileContentKind.UrlShortcut => (ImportQueueItemKind.InternetShortcutFile, ImportQueueGroup.Link),
                    _ => (ImportQueueItemKind.GenericFile, ImportQueueGroup.File),
                };

                var item = new ImportQueueItem
                {
                    Id = Guid.NewGuid(),
                    Kind = itemKind,
                    Group = group,
                    DisplayTitle = file.Name,
                    DisplaySubtitle = file.Path,
                    File = file,
                };

                AddQueueItem(item);
                added++;
            }

            AppLog.Info("Import", $"添加文件到队列：source={source}, selected={files.Count}, added={added}, skippedDup={skippedDuplicate}, skippedInvalid={skippedInvalid}");
            UpdatePrimaryButtonState();
        }

        private void OnAddTextToQueueClicked(object sender, RoutedEventArgs e)
        {
            if (_workspaceItemId is not null)
            {
                ShowDialogWarning(L10n.Get("ImportDialog_WorkspaceExclusive_Message"));
                return;
            }

            string raw = TextDraftTextBox.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(raw))
            {
                ShowDialogWarning(L10n.Get("Import_Text_Empty_Message"));
                return;
            }

            string content = raw.TrimEnd();
            if (_textContentItemId is Guid existingId && existingId != Guid.Empty)
            {
                RemoveQueueItem(existingId, updateState: false);
            }

            string title = BuildTextSummaryTitle(content);
            string subtitle = L10n.Format("ImportDialog_TextContent_Subtitle_Fmt", content.Length);

            var item = new ImportQueueItem
            {
                Id = Guid.NewGuid(),
                Kind = ImportQueueItemKind.TextContent,
                Group = ImportQueueGroup.Text,
                DisplayTitle = title,
                DisplaySubtitle = subtitle,
                TextContent = content,
            };

            AddQueueItem(item);
            _textContentItemId = item.Id;

            TextDraftTextBox.Text = string.Empty;

            AppLog.Info("Import", $"添加文本到队列：length={content.Length}");
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
            if (_workspaceItemId is not null)
            {
                ShowDialogWarning(L10n.Get("ImportDialog_WorkspaceExclusive_Message"));
                return;
            }

            string raw = LinkDraftTextBox.Text ?? string.Empty;
            IReadOnlyList<string> urls = ImportUrlNormalizer.ParseAndNormalizeLinkLines(raw);
            if (urls.Count == 0)
            {
                AppLog.Warn("Import", "添加链接到队列失败：未发现有效链接。");
                ShowDialogWarning(L10n.Get("ImportDialog_NoValidLinks_Message"));
                return;
            }

            int added = 0;
            int skippedDuplicate = 0;

            for (int i = 0; i < urls.Count; i++)
            {
                string url = urls[i];
                if (!_urlSet.Add(url))
                {
                    skippedDuplicate++;
                    continue;
                }

                var item = new ImportQueueItem
                {
                    Id = Guid.NewGuid(),
                    Kind = ImportQueueItemKind.LinkUrl,
                    Group = ImportQueueGroup.Link,
                    DisplayTitle = url,
                    Url = url,
                };

                AddQueueItem(item);
                added++;
            }

            if (added > 0)
            {
                LinkDraftTextBox.Text = string.Empty;
            }

            AppLog.Info("Import", $"添加链接到队列：parsed={urls.Count}, added={added}, skippedDup={skippedDuplicate}");
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
            AppLog.Info("Import", $"清空导入队列：count={_queueById.Count}");
            ClearQueueInternal();
            UpdatePrimaryButtonState();
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

            RemoveQueueItem(itemId, updateState: true);
        }

        private void OnClearWbixClicked(object sender, RoutedEventArgs e)
        {
            // “从队列移除”语义：移除工作区队列项 + 清空预览状态。
            if (_workspaceItemId is Guid workspaceId && workspaceId != Guid.Empty)
            {
                RemoveQueueItem(workspaceId, updateState: true);
                return;
            }

            ClearWorkspaceState();
            UpdatePrimaryButtonState();
        }

        private void ClearQueueInternal()
        {
            _queueById.Clear();
            _groupNodes.Clear();
            _leafNodesByItemId.Clear();
            _filePathSet.Clear();
            _urlSet.Clear();
            _workspaceItemId = null;
            _textContentItemId = null;

            ImportQueueTreeView.RootNodes.Clear();
            ClearWorkspaceState();
            RefreshQueueEmptyHintState();
        }

        private void ClearWorkspaceState()
        {
            _selectedWorkspaceFile = null;
            _selectedWbixPreview = null;
            _selectedWbiPreview = null;
            ClearWbixButton.Visibility = Visibility.Collapsed;
            WbixPreviewBorder.Visibility = Visibility.Collapsed;
            WbixCoverImage.Source = null;
            WbixCoverImageBorder.Visibility = Visibility.Collapsed;
            WbixCoverFallbackBorder.Visibility = Visibility.Visible;
            WbixInfoTextBlock.Text = string.Empty;
        }

        private void AddQueueItem(ImportQueueItem item)
        {
            _queueById[item.Id] = item;

            TreeViewNode groupNode = EnsureGroupNode(item.Group);
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

            var leafNode = new TreeViewNode { Content = nodeInfo };
            groupNode.Children.Add(leafNode);
            _leafNodesByItemId[item.Id] = leafNode;
        }

        private void RemoveQueueItem(Guid itemId, bool updateState)
        {
            if (!_queueById.TryGetValue(itemId, out ImportQueueItem? item))
            {
                return;
            }

            _queueById.Remove(itemId);

            // 去重集合回收：保证移除后可再次加入相同文件/链接。
            if (item.File is StorageFile file && !string.IsNullOrWhiteSpace(file.Path))
            {
                _filePathSet.Remove(file.Path);
            }

            if (!string.IsNullOrWhiteSpace(item.Url))
            {
                _urlSet.Remove(item.Url);
            }

            if (_leafNodesByItemId.TryGetValue(itemId, out TreeViewNode? leafNode))
            {
                _leafNodesByItemId.Remove(itemId);

                if (_groupNodes.TryGetValue(item.Group, out TreeViewNode? groupNode))
                {
                    groupNode.Children.Remove(leafNode);

                    // 分组为空时移除根节点，保持 TreeView 简洁。
                    if (groupNode.Children.Count == 0)
                    {
                        ImportQueueTreeView.RootNodes.Remove(groupNode);
                        _groupNodes.Remove(item.Group);
                    }
                }
            }

            if (item.Kind is ImportQueueItemKind.WorkspaceWbix or ImportQueueItemKind.WorkspaceWbi)
            {
                _workspaceItemId = null;
                ClearWorkspaceState();
            }

            if (item.Kind == ImportQueueItemKind.TextContent)
            {
                _textContentItemId = null;
            }

            AppLog.Info("Import", $"移除队列项：kind={item.Kind}, title='{item.DisplayTitle}'");

            if (updateState)
            {
                UpdatePrimaryButtonState();
            }
        }

        private TreeViewNode EnsureGroupNode(ImportQueueGroup group)
        {
            if (_groupNodes.TryGetValue(group, out TreeViewNode? existing))
            {
                return existing;
            }

            var nodeInfo = new ImportQueueNodeInfo
            {
                IsGroup = true,
                Group = group,
                Icon = ResolveGroupIcon(group),
                Title = GetGroupTitle(group),
                Subtitle = null,
                RemoveButtonVisibility = Visibility.Collapsed,
                ItemId = Guid.Empty,
            };

            var node = new TreeViewNode { Content = nodeInfo, IsExpanded = true };

            int insertIndex = 0;
            int groupOrder = GetGroupOrderIndex(group);

            for (; insertIndex < ImportQueueTreeView.RootNodes.Count; insertIndex++)
            {
                TreeViewNode root = ImportQueueTreeView.RootNodes[insertIndex];
                if (root.Content is not ImportQueueNodeInfo rootInfo)
                {
                    continue;
                }

                int rootOrder = GetGroupOrderIndex(rootInfo.Group);
                if (rootOrder > groupOrder)
                {
                    break;
                }
            }

            ImportQueueTreeView.RootNodes.Insert(insertIndex, node);
            _groupNodes.Add(group, node);
            return node;
        }

        private static int GetGroupOrderIndex(ImportQueueGroup group)
        {
            return group switch
            {
                ImportQueueGroup.Workspace => 0,
                ImportQueueGroup.Image => 1,
                ImportQueueGroup.Video => 2,
                ImportQueueGroup.Audio => 3,
                ImportQueueGroup.Text => 4,
                ImportQueueGroup.Link => 5,
                ImportQueueGroup.File => 6,
                _ => 100,
            };
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

        private string BuildTextSummaryTitle(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return L10n.Get("ImportDialog_Tab_Text");
            }

            // 取首个非空行作为摘要标题。
            string[] lines = content.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                const int maxLen = 60;
                return line.Length <= maxLen ? line : line.Substring(0, maxLen) + "…";
            }

            return L10n.Get("ImportDialog_Tab_Text");
        }


        private async Task LoadWorkspacePreviewAsync(StorageFile file)
        {
            _selectedWorkspaceFile = file;
            _selectedWbixPreview = null;
            _selectedWbiPreview = null;

            ClearWbixButton.Visibility = Visibility.Visible;

            try
            {
                string ext = Path.GetExtension(file.Name);

                if (string.Equals(ext, ".wbix", StringComparison.OrdinalIgnoreCase))
                {
                    WbixPreviewReader.WbixPreview? preview = await WbixPreviewReader.TryReadAsync(file.Path);
                    if (preview is null)
                    {
                        WbixPreviewBorder.Visibility = Visibility.Collapsed;
                        UpdatePrimaryButtonState();
                        ShowDialogWarning(L10n.Get("ImportDialog_Wbix_Invalid_Message"));
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
                    return;
                }

                if (string.Equals(ext, ".wbi", StringComparison.OrdinalIgnoreCase))
                {
                    WbiPreviewReader.WbiPreview? preview = await WbiPreviewReader.TryReadAsync(file.Path);
                    if (preview is null)
                    {
                        WbixPreviewBorder.Visibility = Visibility.Collapsed;
                        UpdatePrimaryButtonState();
                        ShowDialogWarning(L10n.Get("ImportDialog_Wbix_Invalid_Message"));
                        return;
                    }

                    _selectedWbiPreview = preview;

                    int pageCount = preview.Manifest.Pages?.Count ?? preview.Manifest.PageCount;
                    DateTime createdUtc = preview.Manifest.CreatedAt.Kind == DateTimeKind.Unspecified
                        ? DateTime.SpecifyKind(preview.Manifest.CreatedAt, DateTimeKind.Utc)
                        : preview.Manifest.CreatedAt;

                    string created = createdUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
                    string version = preview.Manifest.Version ?? "1.0";
                    string info = L10n.Format("Import_Wbix_Info_Fmt", file.Name, pageCount, version, created);
                    WbixInfoTextBlock.Text = info;

                    // WBI 没有封面：强制回退到占位卡片，避免遗留上一张预览的封面。
                    await ApplyWbixCoverAsync(null);

                    WbixPreviewBorder.Visibility = Visibility.Visible;
                    UpdatePrimaryButtonState();
                    return;
                }

                WbixPreviewBorder.Visibility = Visibility.Collapsed;
                UpdatePrimaryButtonState();
                ShowDialogWarning(L10n.Get("ImportDialog_Wbix_Invalid_Message"));
            }
            catch (Exception ex)
            {
                AppLog.Warn("Import", $"预览失败：'{file.Path}'", ex);
                WbixPreviewBorder.Visibility = Visibility.Collapsed;
                _selectedWbixPreview = null;
                _selectedWbiPreview = null;
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
