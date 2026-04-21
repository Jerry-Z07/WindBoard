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

            // 说明：ContentDialog 的命令按钮样式（主按钮/关闭按钮）在不同系统上可能呈现"直角/拉伸铺满"的旧观感，
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

            UpdateDraftActionButtonStates();
            RefreshQueueEmptyHintState();
        }

        #region TabView 切换

        private void OnTabSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 切换标签时收起提示，避免用户看到"上一页"的警告信息。
            DialogInfoBar.IsOpen = false;

            bool isFileTab = ImportTabView.SelectedIndex == 0;
            FileImportPanel.Visibility = isFileTab ? Visibility.Visible : Visibility.Collapsed;
            TextLinkImportPanel.Visibility = isFileTab ? Visibility.Collapsed : Visibility.Visible;
        }

        #endregion

        #region 文本/链接草稿按钮状态

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

        #endregion

        #region WindowedContentDialog 承载

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

        #endregion

        #region 提交与按钮状态

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

        #endregion

        #region 文件拖拽与选择

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

                // 先清理旧预览，避免异步预读时出现"残留上一份预览"的闪烁。
                ClearWorkspaceState();
                RebuildQueueList();

                // 警告优先显示：若随后预读失败，会被"文件无效"提示覆盖，避免误导用户。
                if (shouldWarn)
                {
                    ShowDialogWarning(L10n.Get("ImportDialog_WorkspaceExclusive_Message"));
                }

                await LoadWorkspacePreviewAsync(workspaceFile);
                return;
            }

            RebuildQueueList();
            UpdatePrimaryButtonState();
        }

        #endregion

        #region 文本/链接输入

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

            RebuildQueueList();
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

            RebuildQueueList();
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

        #endregion

        #region 队列操作

        private void OnClearQueueClicked(object sender, RoutedEventArgs e)
        {
            _queue.Clear();
            UpdatePrimaryButtonState();
            ClearWorkspaceState();
            RebuildQueueList();
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

            RebuildQueueList();
            UpdatePrimaryButtonState();
        }

        private void OnClearWbixClicked(object sender, RoutedEventArgs e)
        {
            // "从队列移除"语义：移除工作区队列项 + 清空预览状态。
            if (_queue.WorkspaceItemId is Guid workspaceId && workspaceId != Guid.Empty)
            {
                _ = _queue.TryRemove(workspaceId, out _);

                ClearWorkspaceState();
                RebuildQueueList();
                UpdatePrimaryButtonState();
                return;
            }

            ClearWorkspaceState();
            UpdatePrimaryButtonState();
        }

        #endregion

        #region 队列列表渲染

        private void RefreshQueueEmptyHintState()
        {
            bool isEmpty = _queue.Count == 0 && _workspacePreview is null;
            QueueEmptyHintTextBlock.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// 重建右侧队列卡片列表。
        /// </summary>
        private void RebuildQueueList()
        {
            QueueItemsPanel.Children.Clear();

            bool hasWorkspace = _queue.WorkspaceItemId is not null;

            // 工作区预览与元素队列互斥：有工作区时展示预览卡片，否则展示元素列表。
            WorkspacePreviewScrollViewer.Visibility = hasWorkspace ? Visibility.Visible : Visibility.Collapsed;
            QueueScrollViewer.Visibility = hasWorkspace ? Visibility.Collapsed : Visibility.Visible;

            if (hasWorkspace)
            {
                // 工作区预览卡片在 XAML 中已静态定义，此处无需动态创建。
                RefreshQueueEmptyHintState();
                return;
            }

            // 按分组顺序扁平渲染队列项。
            for (int gi = 0; gi < ImportQueueState.DisplayGroupOrder.Length; gi++)
            {
                ImportQueueGroup group = ImportQueueState.DisplayGroupOrder[gi];
                IReadOnlyList<ImportQueueItem> items = _queue.GetItemsByGroup(group);
                if (items.Count == 0)
                {
                    continue;
                }

                // 添加分组标签（仅在有内容时显示）。
                var groupLabel = new TextBlock
                {
                    Text = GetGroupTitle(group),
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Opacity = 0.8,
                    FontSize = 12,
                    Margin = new Thickness(0, gi > 0 ? 8 : 0, 0, 2),
                };
                QueueItemsPanel.Children.Add(groupLabel);

                for (int i = 0; i < items.Count; i++)
                {
                    ImportQueueItem item = items[i];
                    Border card = CreateQueueItemCard(item);
                    QueueItemsPanel.Children.Add(card);
                }
            }

            RefreshQueueEmptyHintState();
        }

        /// <summary>
        /// 创建单个队列项卡片行。
        /// </summary>
        private Border CreateQueueItemCard(ImportQueueItem item)
        {
            var card = new Border
            {
                Style = (Style)Resources["QueueItemBorderStyle"],
                Tag = item.Id,
            };

            var grid = new Grid
            {
                ColumnSpacing = 10,
            };

            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // 图标
            var icon = new SymbolIcon
            {
                Symbol = ResolveLeafIcon(item.Kind),
                Opacity = 0.75,
            };
            Grid.SetColumn(icon, 0);
            grid.Children.Add(icon);

            // 标题 + 副标题
            var textStack = new StackPanel { Spacing = 2 };

            var titleBlock = new TextBlock
            {
                Text = item.DisplayTitle,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            textStack.Children.Add(titleBlock);

            if (!string.IsNullOrWhiteSpace(item.DisplaySubtitle))
            {
                var subtitleBlock = new TextBlock
                {
                    Text = item.DisplaySubtitle,
                    Opacity = 0.65,
                    FontSize = 11,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };
                textStack.Children.Add(subtitleBlock);
            }

            Grid.SetColumn(textStack, 1);
            grid.Children.Add(textStack);

            // 删除按钮
            var removeIcon = new SymbolIcon { Symbol = Symbol.Delete };
            var removeButton = new Button
            {
                Style = (Style)Resources["QueueItemRemoveButtonStyle"],
                Tag = item.Id,
                Content = removeIcon,
            };
            ToolTipService.SetToolTip(removeButton, L10n.Get("ImportDialog_RemoveFromQueue_Tooltip"));
            removeButton.Click += OnQueueRemoveClicked;
            Grid.SetColumn(removeButton, 2);
            grid.Children.Add(removeButton);

            card.Child = grid;
            return card;
        }

        #endregion

        #region 工作区预览

        private void ClearWorkspaceState()
        {
            _selectedWorkspaceFile = null;
            _workspacePreview = null;
            WbixCoverImage.Source = null;
            WbixCoverImageBorder.Visibility = Visibility.Collapsed;
            WbixCoverFallbackBorder.Visibility = Visibility.Visible;
            WbixInfoTextBlock.Text = string.Empty;
        }

        private async Task LoadWorkspacePreviewAsync(StorageFile file)
        {
            _selectedWorkspaceFile = file;
            _workspacePreview = null;

            try
            {
                ImportWorkspacePreview? preview = await ImportWorkspacePreviewService.TryLoadAsync(file);
                if (preview is null)
                {
                    WorkspacePreviewScrollViewer.Visibility = Visibility.Collapsed;
                    QueueEmptyHintTextBlock.Visibility = Visibility.Visible;
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

                // 展示工作区预览卡片。
                WorkspacePreviewScrollViewer.Visibility = Visibility.Visible;
                QueueScrollViewer.Visibility = Visibility.Collapsed;
                UpdatePrimaryButtonState();
            }
            catch (Exception ex)
            {
                AppLog.Warn("Import", $"预览失败：'{file.Path}'", ex);
                WorkspacePreviewScrollViewer.Visibility = Visibility.Collapsed;
                QueueEmptyHintTextBlock.Visibility = Visibility.Visible;
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

        #endregion

        #region 工具方法

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

        private static Symbol ResolveAudioIconSymbol()
        {
            // 音频图标：优先使用更贴近语义的 MusicInfo（若目标 SDK 不存在该枚举值则降级）。
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

        #endregion
    }
}
