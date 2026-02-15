using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using WindBoard.Board.Commands;
using WindBoard.Board.Elements;
using WindBoard.Board.Editing;
using WindBoard.Board.Persistence;
using WindBoard.Board.Persistence.Wbix;
using WindBoard.Logging;
using WindBoard.Localization;
using WindBoard.Importing;
using WindBoard.Importing.Wbi;
using WbixPreview = WindBoard.Board.Persistence.Wbix.WbixPreviewReader.WbixPreview;

namespace WindBoard
{
    /// <summary>
    /// 主窗口：导入相关代码。
    /// </summary>
    public sealed partial class MainWindow
    {
        private enum ImportEntry
        {
            Files,
            Text,
            Link,
        }

        private enum WbixImportMode
        {
            ReplaceCurrentPage,
            AppendAfterLastPage,
        }

        private async Task StartImportAsync()
        {
            XamlRoot? xamlRoot = TryGetDialogXamlRoot();
            if (xamlRoot is null)
            {
                return;
            }

            try
            {
                IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                if (hwnd == IntPtr.Zero)
                {
                    await ShowMessageDialogAsync(xamlRoot, L10n.Get("Import_Failed_Title"), L10n.Get("Common_WindowHandleFailed_Message"));
                    return;
                }

                var dialog = new UI.Dialogs.ImportDialog(hwnd)
                {
                    XamlRoot = xamlRoot,
                };

                if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                {
                    return;
                }

                if (dialog.WbixRequest is ImportWbixRequest wbix)
                {
                    AppLog.Info("Import", $"开始导入 WBIX：path='{wbix.File.Path}', mode={wbix.Mode}");
                    await ImportWbixAsync(xamlRoot, wbix.File, wbix.Mode);
                    return;
                }

                if (dialog.WbiRequest is ImportWbiRequest wbi)
                {
                    AppLog.Info("Import", $"开始导入 WBI：path='{wbi.File.Path}', mode={wbi.Mode}");
                    await ImportWbiAsync(xamlRoot, wbi.File, wbi.Mode);
                    return;
                }

                if (dialog.ElementsRequest is not ImportElementsRequest request)
                {
                    return;
                }

                BoardCanvas.GetViewportState(out Vector2 cameraWorld, out float zoom);
                IReadOnlyList<BoardElement> created = await BoardImportService.ImportElementsAsync(_workspace, cameraWorld, zoom, request);

                if (created.Count > 0)
                {
                    // 复刻旧版体验：导入后自动进入选择并选中新对象。
                    ApplyToolSelection(Interaction.BoardTool.Select);
                    BoardCanvas.SetSelectedElement(created[^1]);
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("Import", "导入异常。", ex);
                await ShowMessageDialogAsync(xamlRoot, L10n.Get("Import_Failed_Title"), ex.Message);
            }
        }

        private static async Task<ImportEntry?> ShowImportEntryDialogAsync(XamlRoot xamlRoot)
        {
            static RadioButton CreateRadio(string text, Symbol symbol, bool isChecked = false)
            {
                var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                content.Children.Add(new SymbolIcon { Symbol = symbol });
                content.Children.Add(new TextBlock
                {
                    Text = text,
                    VerticalAlignment = VerticalAlignment.Center,
                });

                return new RadioButton
                {
                    Content = content,
                    IsChecked = isChecked,
                };
            }

            var rbFiles = CreateRadio(L10n.Get("Import_Entry_Files"), Symbol.OpenFile, isChecked: true);
            var rbText = CreateRadio(L10n.Get("Import_Entry_Text"), Symbol.Edit);
            var rbLink = CreateRadio(L10n.Get("Import_Entry_Link"), Symbol.Link);

            var panel = new StackPanel { Spacing = 8 };
            panel.Children.Add(new TextBlock
            {
                Text = L10n.Get("Import_EntryDialog_Prompt"),
                TextWrapping = TextWrapping.Wrap,
            });
            panel.Children.Add(rbFiles);
            panel.Children.Add(rbText);
            panel.Children.Add(rbLink);

            var dialog = new ContentDialog
            {
                Title = L10n.Get("Import_EntryDialog_Title"),
                Content = panel,
                PrimaryButtonText = L10n.Get("Common_Continue"),
                CloseButtonText = L10n.Get("Common_Cancel"),
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = xamlRoot,
            };

            ContentDialogResult result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return null;
            }

            if (rbText.IsChecked == true)
            {
                return ImportEntry.Text;
            }

            if (rbLink.IsChecked == true)
            {
                return ImportEntry.Link;
            }

            return ImportEntry.Files;
        }

        private async Task ImportTextAsync(XamlRoot xamlRoot)
        {
            var input = new TextBox
            {
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 160,
                PlaceholderText = L10n.Get("Import_Text_Placeholder"),
            };

            var dialog = new ContentDialog
            {
                Title = L10n.Get("Import_TextDialog_Title"),
                Content = input,
                PrimaryButtonText = L10n.Get("Common_Import"),
                CloseButtonText = L10n.Get("Common_Cancel"),
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = xamlRoot,
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            string text = input.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                await ShowMessageDialogAsync(xamlRoot, L10n.Get("Import_Tip_Title"), L10n.Get("Import_Text_Empty_Message"));
                return;
            }

            var element = new BoardTextElement { Text = text.TrimEnd() };
            PlaceElementAtViewportCenter(element, sizeDip: new Vector2(360.0f, 200.0f), offsetIndex: 0);
            _workspace.CurrentPage.Session.Execute(new AddElementCommand(element, aboveInk: false));
        }

        private async Task ImportLinkAsync(XamlRoot xamlRoot)
        {
            var urlBox = new TextBox
            {
                PlaceholderText = L10n.Get("Common_UrlPlaceholder"),
            };

            var titleBox = new TextBox
            {
                PlaceholderText = L10n.Get("Import_Link_TitlePlaceholder"),
            };

            var panel = new StackPanel { Spacing = 8 };
            panel.Children.Add(new TextBlock { Text = L10n.Get("Import_Link_UrlLabel") });
            panel.Children.Add(urlBox);
            panel.Children.Add(new TextBlock { Text = L10n.Get("Import_Link_TitleLabel") });
            panel.Children.Add(titleBox);

            var dialog = new ContentDialog
            {
                Title = L10n.Get("Import_LinkDialog_Title"),
                Content = panel,
                PrimaryButtonText = L10n.Get("Common_Import"),
                CloseButtonText = L10n.Get("Common_Cancel"),
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = xamlRoot,
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            string url = (urlBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(url))
            {
                await ShowMessageDialogAsync(xamlRoot, L10n.Get("Import_Tip_Title"), L10n.Get("Import_Link_Empty_Message"));
                return;
            }

            var element = new BoardLinkElement
            {
                Url = url,
                Title = string.IsNullOrWhiteSpace(titleBox.Text) ? null : titleBox.Text.Trim(),
            };

            PlaceElementAtViewportCenter(element, sizeDip: new Vector2(360.0f, 160.0f), offsetIndex: 0);
            _workspace.CurrentPage.Session.Execute(new AddElementCommand(element, aboveInk: false));
        }

        private async Task ImportFilesAsync(XamlRoot xamlRoot)
        {
            IReadOnlyList<StorageFile>? files = await PickImportFilesAsync(xamlRoot);
            if (files is null || files.Count == 0)
            {
                return;
            }

            AppLog.Info("Import", $"导入文件：count={files.Count}");

            StorageFile? workspaceFile = null;
            foreach (StorageFile f in files)
            {
                string ext = Path.GetExtension(f.Name);
                if (string.Equals(ext, ".wbix", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(ext, ".wbi", StringComparison.OrdinalIgnoreCase))
                {
                    workspaceFile = f;
                    break;
                }
            }

            if (workspaceFile is not null)
            {
                AppLog.Info("Import", $"识别为工作区文件：'{workspaceFile.Path}'");

                if (files.Count > 1)
                {
                    await ShowMessageDialogAsync(xamlRoot, L10n.Get("Import_Tip_Title"), L10n.Get("Import_Wbix_MultipleFiles_Message"));
                    return;
                }

                string ext = Path.GetExtension(workspaceFile.Name);
                if (string.Equals(ext, ".wbix", StringComparison.OrdinalIgnoreCase))
                {
                    await ImportWbixAsync(xamlRoot, workspaceFile);
                    return;
                }

                if (string.Equals(ext, ".wbi", StringComparison.OrdinalIgnoreCase))
                {
                    await ImportWbiAsync(xamlRoot, workspaceFile);
                    return;
                }

                await ShowMessageDialogAsync(xamlRoot, L10n.Get("Import_Failed_Title"), L10n.Get("Import_Wbix_ParseFailed_Message"));
                return;
            }

            for (int i = 0; i < files.Count; i++)
            {
                StorageFile file = files[i];
                await ImportSingleFileAsync(xamlRoot, file, offsetIndex: i);
            }
        }

        private async Task ImportSingleFileAsync(XamlRoot xamlRoot, StorageFile file, int offsetIndex)
        {
            string ext = Path.GetExtension(file.Name).ToLowerInvariant();

            if (IsImageExtension(ext))
            {
                await ImportImageFileAsync(file, offsetIndex);
                return;
            }

            if (IsAudioExtension(ext))
            {
                ImportMediaPlaceholder(file, BoardMediaKind.Audio, offsetIndex);
                return;
            }

            if (IsVideoExtension(ext))
            {
                ImportMediaPlaceholder(file, BoardMediaKind.Video, offsetIndex);
                return;
            }

            if (string.Equals(ext, ".url", StringComparison.OrdinalIgnoreCase))
            {
                string content = await TextImportReader.ReadTextFileWithLimitAsync(file.Path, maxChars: 16_384);
                if (TryParseInternetShortcutUrl(content, out string url))
                {
                    var link = new BoardLinkElement { Url = url };
                    PlaceElementAtViewportCenter(link, sizeDip: new Vector2(360.0f, 160.0f), offsetIndex: offsetIndex);
                    _workspace.CurrentPage.Session.Execute(new AddElementCommand(link, aboveInk: false));
                    return;
                }

                // 解析失败：按文本导入兜底，避免用户“什么都没发生”的体验。
                var text = new BoardTextElement { Text = content };
                PlaceElementAtViewportCenter(text, sizeDip: new Vector2(360.0f, 200.0f), offsetIndex: offsetIndex);
                _workspace.CurrentPage.Session.Execute(new AddElementCommand(text, aboveInk: false));
                return;
            }

            if (IsTextExtension(ext))
            {
                string content = await TextImportReader.ReadTextFileWithLimitAsync(file.Path, maxChars: 64_000);
                var text = new BoardTextElement { Text = content };
                PlaceElementAtViewportCenter(text, sizeDip: new Vector2(420.0f, 260.0f), offsetIndex: offsetIndex);
                _workspace.CurrentPage.Session.Execute(new AddElementCommand(text, aboveInk: false));
                return;
            }

            // 其它文件：统一以“文件占位卡片”导入，并支持双击外部打开。
            // 说明：常见文档（PDF/Office 等）与未知格式都走这一分支，避免“导入后什么都没发生”。
            AppLog.Debug("Import", $"文件占位卡片导入：'{file.Path}'");
            ImportFilePlaceholder(file, offsetIndex);
        }

        private async Task ImportImageFileAsync(StorageFile file, int offsetIndex)
        {
            (byte[] pixels, int w, int h)? decoded = await ImageImportDecoder.TryDecodeToBgra8PremulAsync(file, maxPixelEdge: 2048);

            var element = new BoardMediaElement
            {
                Kind = BoardMediaKind.Image,
                SourcePath = file.Path,
                DisplayName = file.Name,
                PixelWidth = decoded?.w ?? 0,
                PixelHeight = decoded?.h ?? 0,
                Bgra8PremulPixels = decoded?.pixels,
            };

            Vector2 sizeDip = decoded is { w: > 0, h: > 0 } d
                ? ComputeImageCardSizeDip(d.w, d.h, maxWidthDip: 520.0f, maxHeightDip: 360.0f)
                : new Vector2(360.0f, 220.0f);

            PlaceElementAtViewportCenter(element, sizeDip, offsetIndex);
            _workspace.CurrentPage.Session.Execute(new AddElementCommand(element, aboveInk: false));
        }

        private void ImportMediaPlaceholder(StorageFile file, BoardMediaKind kind, int offsetIndex)
        {
            var element = new BoardMediaElement
            {
                Kind = kind,
                SourcePath = file.Path,
                DisplayName = file.Name,
            };

            PlaceElementAtViewportCenter(element, sizeDip: new Vector2(360.0f, 160.0f), offsetIndex: offsetIndex);
            _workspace.CurrentPage.Session.Execute(new AddElementCommand(element, aboveInk: false));
        }

        private void ImportFilePlaceholder(StorageFile file, int offsetIndex)
        {
            var element = new BoardFileElement
            {
                SourcePath = file.Path,
                DisplayName = file.Name,
            };

            PlaceElementAtViewportCenter(element, sizeDip: new Vector2(360.0f, 160.0f), offsetIndex: offsetIndex);
            _workspace.CurrentPage.Session.Execute(new AddElementCommand(element, aboveInk: false));
        }

        private void PlaceElementAtViewportCenter(BoardElement element, Vector2 sizeDip, int offsetIndex)
        {
            // 放置策略：
            // - 以当前视口中心为基准；
            // - 尺寸按当前缩放换算到世界坐标，使导入时在屏幕上的初始大小相对稳定；
            // - 多个文件导入时做轻微偏移，避免完全重叠。
            BoardCanvas.GetViewportState(out Vector2 cameraWorld, out float zoom);
            float z = Math.Max(0.0001f, zoom);

            Vector2 sizeWorld = sizeDip / z;
            Vector2 offsetWorld = new Vector2(24.0f, 24.0f) * offsetIndex / z;

            element.SizeWorld = sizeWorld;
            element.PositionWorld = cameraWorld - sizeWorld / 2.0f + offsetWorld;
        }

        private async Task<IReadOnlyList<StorageFile>?> PickImportFilesAsync(XamlRoot xamlRoot)
        {
            IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            if (hwnd == IntPtr.Zero)
            {
                await ShowMessageDialogAsync(xamlRoot, L10n.Get("Import_Failed_Title"), L10n.Get("Common_WindowHandleFailed_Message"));
                return null;
            }

            var picker = new FileOpenPicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Clear();
            picker.FileTypeFilter.Add("*");

            return await picker.PickMultipleFilesAsync();
        }

        private async Task ImportWbixAsync(XamlRoot xamlRoot, StorageFile file)
        {
            WbixPreview? preview = await WbixPreviewReader.TryReadAsync(file.Path);
            if (preview is null)
            {
                await ShowMessageDialogAsync(xamlRoot, L10n.Get("Import_Failed_Title"), L10n.Get("Import_Wbix_ParseFailed_Message"));
                return;
            }

            WbixImportMode? mode = await ShowWbixImportConfirmDialogAsync(xamlRoot, file.Name, preview);
            if (mode is null)
            {
                return;
            }

            ImportWbixMode normalizedMode = mode == WbixImportMode.ReplaceCurrentPage
                ? ImportWbixMode.ReplaceCurrentPage
                : ImportWbixMode.AppendAfterLastPage;

            await ImportWbixAsync(xamlRoot, file, normalizedMode);
        }

        private async Task ImportWbiAsync(XamlRoot xamlRoot, StorageFile file)
        {
            WbiPreviewReader.WbiPreview? preview = await WbiPreviewReader.TryReadAsync(file.Path);
            if (preview is null)
            {
                await ShowMessageDialogAsync(xamlRoot, L10n.Get("Import_Failed_Title"), L10n.Get("Import_Wbix_ParseFailed_Message"));
                return;
            }

            WbixImportMode? mode = await ShowWbiImportConfirmDialogAsync(xamlRoot, file.Name, preview);
            if (mode is null)
            {
                return;
            }

            ImportWbixMode normalizedMode = mode == WbixImportMode.ReplaceCurrentPage
                ? ImportWbixMode.ReplaceCurrentPage
                : ImportWbixMode.AppendAfterLastPage;

            await ImportWbiAsync(xamlRoot, file, normalizedMode);
        }

        private async Task ImportWbixAsync(XamlRoot xamlRoot, StorageFile file, ImportWbixMode mode)
        {
            if (mode == ImportWbixMode.ReplaceCurrentPage)
            {
                bool confirmed = await ConfirmWbixReplaceCurrentPageRiskAsync(xamlRoot);
                if (!confirmed)
                {
                    return;
                }
            }

            var serializer = new WbixWorkspaceSerializer();

            try
            {
                await RunBusyDialogAsync(xamlRoot, L10n.Get("Import_Wbix_Busy_Title"), async () =>
                {
                    BoardWorkspaceSnapshot snapshot = await Task.Run(async () =>
                    {
                        await using var stream = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
                        return await serializer.LoadAsync(stream);
                    });

                    List<BoardPage> pages = BoardWorkspaceSnapshotApplier.CreatePages(snapshot);

                    if (mode == ImportWbixMode.ReplaceCurrentPage)
                    {
                        AppLog.Info("WBIX", $"替换工作区：pages={pages.Count}, currentIndex={snapshot.CurrentIndex}");
                        if (pages.Count == 0)
                        {
                            AppLog.Warn("WBIX", "pages=0，忽略导入。");
                            return;
                        }

                        int insertIndex = _workspace.CurrentIndex;
                        int replaceImportCurrent = Math.Clamp(snapshot.CurrentIndex, 0, Math.Max(0, pages.Count - 1));

                        AppLog.Info("WBIX", $"覆盖当前页并插入：workspaceCurrent={insertIndex}, importPages={pages.Count}, importCurrent={replaceImportCurrent}");

                        // 覆盖当前页：用导入文件的第 1 页替换当前页，然后把剩余页插入到其后。
                        _workspace.ReplacePageAt(insertIndex, pages[0]);

                        if (pages.Count > 1)
                        {
                            _workspace.InsertPages(insertIndex + 1, pages.GetRange(1, pages.Count - 1), switchToFirstInsertedPage: false);
                        }

                        int replaceTargetIndex = Math.Clamp(insertIndex + replaceImportCurrent, 0, Math.Max(0, _workspace.Pages.Count - 1));
                        AppLog.Info("WBIX", $"覆盖导入完成：switchTo={replaceTargetIndex}, pagesAfter={_workspace.Pages.Count}");
                        _workspace.SetCurrentIndex(replaceTargetIndex);
                        return;
                    }

                    int startIndex = _workspace.AppendPages(pages, switchToFirstAppendedPage: false);
                    int importCurrent = Math.Clamp(snapshot.CurrentIndex, 0, Math.Max(0, pages.Count - 1));
                    int targetIndex = Math.Clamp(startIndex + importCurrent, 0, Math.Max(0, _workspace.Pages.Count - 1));
                    AppLog.Info("WBIX", $"追加页面：startIndex={startIndex}, pages={pages.Count}, switchTo={targetIndex}");
                    _workspace.SetCurrentIndex(targetIndex);
                }, message: L10n.Get("Import_Wbix_Busy_Message"));
            }
            catch (Exception ex)
            {
                AppLog.Error("WBIX", $"导入失败：'{file.Path}'", ex);
                await ShowMessageDialogAsync(xamlRoot, L10n.Get("Import_Failed_Title"), L10n.Get("Import_Wbix_ParseFailed_Message"));
            }
        }

        private async Task ImportWbiAsync(XamlRoot xamlRoot, StorageFile file, ImportWbixMode mode)
        {
            if (mode == ImportWbixMode.ReplaceCurrentPage)
            {
                bool confirmed = await ConfirmWbixReplaceCurrentPageRiskAsync(xamlRoot);
                if (!confirmed)
                {
                    return;
                }
            }

            var importer = new WbiWorkspaceImporter();
            List<string>? missingResources = null;

            try
            {
                await RunBusyDialogAsync(xamlRoot, L10n.Get("Import_Wbix_Busy_Title"), async () =>
                {
                    WbiWorkspaceImportResult importResult = await Task.Run(() => importer.ImportAsync(file.Path));
                    if (!importResult.Success)
                    {
                        throw new InvalidDataException(importResult.ErrorMessage ?? L10n.Get("Import_Wbix_ParseFailed_Message"));
                    }

                    missingResources = importResult.MissingResources;
                    List<BoardPage> pages = importResult.Pages;

                    // WBI 不记录 currentIndex：按旧版行为默认视为 0。
                    const int importCurrent = 0;

                    if (mode == ImportWbixMode.ReplaceCurrentPage)
                    {
                        AppLog.Info("WBI", $"替换工作区：pages={pages.Count}");
                        if (pages.Count == 0)
                        {
                            AppLog.Warn("WBI", "pages=0，忽略导入。");
                            return;
                        }

                        int insertIndex = _workspace.CurrentIndex;
                        AppLog.Info("WBI", $"覆盖当前页并插入：workspaceCurrent={insertIndex}, importPages={pages.Count}");

                        // 覆盖当前页：用导入文件的第 1 页替换当前页，然后把剩余页插入到其后。
                        _workspace.ReplacePageAt(insertIndex, pages[0]);

                        if (pages.Count > 1)
                        {
                            _workspace.InsertPages(insertIndex + 1, pages.GetRange(1, pages.Count - 1), switchToFirstInsertedPage: false);
                        }

                        int replaceTargetIndex = Math.Clamp(insertIndex + importCurrent, 0, Math.Max(0, _workspace.Pages.Count - 1));
                        AppLog.Info("WBI", $"覆盖导入完成：switchTo={replaceTargetIndex}, pagesAfter={_workspace.Pages.Count}");
                        _workspace.SetCurrentIndex(replaceTargetIndex);
                        return;
                    }

                    int startIndex = _workspace.AppendPages(pages, switchToFirstAppendedPage: false);
                    int targetIndex = Math.Clamp(startIndex + importCurrent, 0, Math.Max(0, _workspace.Pages.Count - 1));
                    AppLog.Info("WBI", $"追加页面：startIndex={startIndex}, pages={pages.Count}, switchTo={targetIndex}");
                    _workspace.SetCurrentIndex(targetIndex);
                }, message: L10n.Get("Import_Wbix_Busy_Message"));
            }
            catch (Exception ex)
            {
                AppLog.Error("WBI", $"导入失败：'{file.Path}'", ex);
                await ShowMessageDialogAsync(xamlRoot, L10n.Get("Import_Failed_Title"), L10n.Get("Import_Wbix_ParseFailed_Message"));
                return;
            }

            if (missingResources is { Count: > 0 })
            {
                AppLog.Warn("WBI", $"导入存在缺失资源：count={missingResources.Count}");

                int take = Math.Min(8, missingResources.Count);
                string detail = string.Join(Environment.NewLine, missingResources.GetRange(0, take));
                string more = missingResources.Count > take ? $"{Environment.NewLine}…（还有 {missingResources.Count - take} 项，详见日志）" : string.Empty;

                await ShowMessageDialogAsync(
                    xamlRoot,
                    L10n.Get("Import_Tip_Title"),
                    $"导入完成，但有部分资源未找到或无法读取：{Environment.NewLine}{detail}{more}");
            }
        }

        private static async Task<bool> ConfirmWbixReplaceRiskAsync(XamlRoot xamlRoot)
        {
            var dialog = new ContentDialog
            {
                Title = L10n.Get("Import_Risk_Title"),
                Content = L10n.Get("Import_Risk_ReplaceWorkspace_Content"),
                PrimaryButtonText = L10n.Get("Import_Risk_ContinueOverwrite"),
                CloseButtonText = L10n.Get("Common_Cancel"),
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = xamlRoot,
            };

            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }

        private static async Task<bool> ConfirmWbixReplaceCurrentPageRiskAsync(XamlRoot xamlRoot)
        {
            var dialog = new ContentDialog
            {
                Title = L10n.Get("Import_Risk_Title"),
                Content = L10n.Get("Import_Risk_ReplaceCurrentPage_Content"),
                PrimaryButtonText = L10n.Get("Import_Risk_ContinueOverwrite"),
                CloseButtonText = L10n.Get("Common_Cancel"),
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = xamlRoot,
            };

            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }

        private static async Task<WbixImportMode?> ShowWbixImportConfirmDialogAsync(XamlRoot xamlRoot, string fileName, WbixPreview preview)
        {
            Image? coverImage = null;
            if (preview.CoverPngBytes is byte[] bytes && bytes.Length > 0)
            {
                ImageSource? source = await TryCreateBitmapImageAsync(bytes);
                if (source is not null)
                {
                    coverImage = new Image
                    {
                        Source = source,
                        Width = 240,
                        Height = 180,
                        Stretch = Stretch.UniformToFill,
                    };
                }
            }

            UIElement cover;
            if (coverImage is not null)
            {
                cover = coverImage;
            }
            else
            {
                cover = new Border
                {
                    Width = 240,
                    Height = 180,
                    CornerRadius = new CornerRadius(8),
                    Background = new SolidColorBrush(Microsoft.UI.Colors.LightGray),
                    Child = new TextBlock
                    {
                        Text = L10n.Get("Import_Wbix_NoCover"),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                };
            }

            int pageCount = preview.Manifest.Pages?.Count ?? 0;
            string created = preview.Manifest.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            string info = L10n.Format("Import_Wbix_Info_Fmt", fileName, pageCount, preview.Manifest.Version, created);

            var rbAppend = new RadioButton { Content = L10n.Get("Import_Wbix_Mode_Append"), IsChecked = true };
            var rbReplace = new RadioButton { Content = L10n.Get("Import_Wbix_Mode_ReplaceCurrentPage") };

            var panel = new StackPanel { Spacing = 12 };
            panel.Children.Add(cover);
            panel.Children.Add(new TextBlock { Text = info, TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(new TextBlock { Text = L10n.Get("Import_Wbix_InsertMethod_Label"), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            panel.Children.Add(rbAppend);
            panel.Children.Add(rbReplace);

            var dialog = new ContentDialog
            {
                Title = L10n.Get("Import_Wbix_Dialog_Title"),
                Content = panel,
                PrimaryButtonText = L10n.Get("Common_Import"),
                CloseButtonText = L10n.Get("Common_Cancel"),
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = xamlRoot,
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return null;
            }

            return rbReplace.IsChecked == true ? WbixImportMode.ReplaceCurrentPage : WbixImportMode.AppendAfterLastPage;
        }

        private static async Task<WbixImportMode?> ShowWbiImportConfirmDialogAsync(XamlRoot xamlRoot, string fileName, WbiPreviewReader.WbiPreview preview)
        {
            // WBI 没有封面：直接使用占位卡片，保持与 WBIX 对话框一致的布局。
            var cover = new Border
            {
                Width = 240,
                Height = 180,
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Microsoft.UI.Colors.LightGray),
                Child = new TextBlock
                {
                    Text = L10n.Get("Import_Wbix_NoCover"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };

            int pageCount = preview.Manifest.Pages?.Count ?? preview.Manifest.PageCount;
            DateTime createdUtc = preview.Manifest.CreatedAt.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(preview.Manifest.CreatedAt, DateTimeKind.Utc)
                : preview.Manifest.CreatedAt;

            string created = createdUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            string version = preview.Manifest.Version ?? "1.0";
            string info = L10n.Format("Import_Wbix_Info_Fmt", fileName, pageCount, version, created);

            var rbAppend = new RadioButton { Content = L10n.Get("Import_Wbix_Mode_Append"), IsChecked = true };
            var rbReplace = new RadioButton { Content = L10n.Get("Import_Wbix_Mode_ReplaceCurrentPage") };

            var panel = new StackPanel { Spacing = 12 };
            panel.Children.Add(cover);
            panel.Children.Add(new TextBlock { Text = info, TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(new TextBlock { Text = L10n.Get("Import_Wbix_InsertMethod_Label"), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            panel.Children.Add(rbAppend);
            panel.Children.Add(rbReplace);

            var dialog = new ContentDialog
            {
                Title = L10n.Get("Import_Wbix_Dialog_Title"),
                Content = panel,
                PrimaryButtonText = L10n.Get("Common_Import"),
                CloseButtonText = L10n.Get("Common_Cancel"),
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = xamlRoot,
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return null;
            }

            return rbReplace.IsChecked == true ? WbixImportMode.ReplaceCurrentPage : WbixImportMode.AppendAfterLastPage;
        }

        private static async Task<ImageSource?> TryCreateBitmapImageAsync(byte[] pngBytes)
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

        private static Vector2 ComputeImageCardSizeDip(int pixelWidth, int pixelHeight, float maxWidthDip, float maxHeightDip)
        {
            float iw = Math.Max(1.0f, pixelWidth);
            float ih = Math.Max(1.0f, pixelHeight);

            float scale = Math.Min(maxWidthDip / iw, maxHeightDip / ih);
            float w = Math.Clamp(iw * scale, 160.0f, maxWidthDip);
            float h = Math.Clamp(ih * scale, 120.0f, maxHeightDip);
            return new Vector2(w, h);
        }

        private static bool TryParseInternetShortcutUrl(string content, out string url)
        {
            url = string.Empty;
            if (string.IsNullOrWhiteSpace(content))
            {
                return false;
            }

            string[] lines = content.Replace("\r\n", "\n").Split('\n');
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
                {
                    url = line.Substring(4).Trim();
                    return !string.IsNullOrWhiteSpace(url);
                }
            }

            return false;
        }

        private static bool IsImageExtension(string ext)
        {
            return ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".tif" or ".tiff" or ".webp";
        }

        private static bool IsAudioExtension(string ext)
        {
            return ext is ".mp3" or ".wav" or ".m4a" or ".aac" or ".flac" or ".ogg";
        }

        private static bool IsVideoExtension(string ext)
        {
            return ext is ".mp4" or ".mov" or ".mkv" or ".wmv" or ".avi" or ".webm";
        }

        private static bool IsTextExtension(string ext)
        {
            return ext is ".txt" or ".md" or ".log" or ".json";
        }
    }
}
