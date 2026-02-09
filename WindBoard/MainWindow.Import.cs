using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Numerics;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using WindBoard.Board.Commands;
using WindBoard.Board.Elements;
using WindBoard.Board.Editing;
using WindBoard.Board.Persistence;
using WindBoard.Board.Persistence.Wbix;

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

        private sealed record WbixPreview(
            WbixManifest Manifest,
            byte[]? CoverPngBytes);

        private async Task StartImportAsync()
        {
            XamlRoot? xamlRoot = TryGetDialogXamlRoot();
            if (xamlRoot is null)
            {
                return;
            }

            ImportEntry? entry = await ShowImportEntryDialogAsync(xamlRoot);
            if (entry is null)
            {
                return;
            }

            try
            {
                switch (entry.Value)
                {
                    case ImportEntry.Files:
                        await ImportFilesAsync(xamlRoot);
                        return;

                    case ImportEntry.Text:
                        await ImportTextAsync(xamlRoot);
                        return;

                    case ImportEntry.Link:
                        await ImportLinkAsync(xamlRoot);
                        return;

                    default:
                        await ShowMessageDialogAsync(xamlRoot, "导入失败", "未知导入类型。");
                        return;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Import] 导入异常：{ex}");
                await ShowMessageDialogAsync(xamlRoot, "导入失败", ex.Message);
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

            var rbFiles = CreateRadio("导入文件", Symbol.OpenFile, isChecked: true);
            var rbText = CreateRadio("导入文字", Symbol.Edit);
            var rbLink = CreateRadio("导入链接", Symbol.Link);

            var panel = new StackPanel { Spacing = 8 };
            panel.Children.Add(new TextBlock
            {
                Text = "请选择导入内容类型：",
                TextWrapping = TextWrapping.Wrap,
            });
            panel.Children.Add(rbFiles);
            panel.Children.Add(rbText);
            panel.Children.Add(rbLink);

            var dialog = new ContentDialog
            {
                Title = "导入",
                Content = panel,
                PrimaryButtonText = "继续",
                CloseButtonText = "取消",
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
                PlaceholderText = "在此输入要导入的文字…",
            };

            var dialog = new ContentDialog
            {
                Title = "导入文字",
                Content = input,
                PrimaryButtonText = "导入",
                CloseButtonText = "取消",
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
                await ShowMessageDialogAsync(xamlRoot, "导入提示", "文字内容为空。");
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
                PlaceholderText = "https://example.com",
            };

            var titleBox = new TextBox
            {
                PlaceholderText = "标题（可选）",
            };

            var panel = new StackPanel { Spacing = 8 };
            panel.Children.Add(new TextBlock { Text = "链接：" });
            panel.Children.Add(urlBox);
            panel.Children.Add(new TextBlock { Text = "标题（可选）：" });
            panel.Children.Add(titleBox);

            var dialog = new ContentDialog
            {
                Title = "导入链接",
                Content = panel,
                PrimaryButtonText = "导入",
                CloseButtonText = "取消",
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
                await ShowMessageDialogAsync(xamlRoot, "导入提示", "链接不能为空。");
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

            StorageFile? wbix = null;
            foreach (StorageFile f in files)
            {
                if (string.Equals(Path.GetExtension(f.Name), ".wbix", StringComparison.OrdinalIgnoreCase))
                {
                    wbix = f;
                    break;
                }
            }

            if (wbix is not null)
            {
                if (files.Count > 1)
                {
                    await ShowMessageDialogAsync(xamlRoot, "导入提示", "检测到 WBIX 文件，请单独导入 .wbix。");
                    return;
                }

                await ImportWbixAsync(xamlRoot, wbix);
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
                string content = await ReadTextFileWithLimitAsync(file.Path, maxChars: 16_384);
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
                string content = await ReadTextFileWithLimitAsync(file.Path, maxChars: 64_000);
                var text = new BoardTextElement { Text = content };
                PlaceElementAtViewportCenter(text, sizeDip: new Vector2(420.0f, 260.0f), offsetIndex: offsetIndex);
                _workspace.CurrentPage.Session.Execute(new AddElementCommand(text, aboveInk: false));
                return;
            }

            // 其它文件：统一以“文件占位卡片”导入，并支持双击外部打开。
            // 说明：常见文档（PDF/Office 等）与未知格式都走这一分支，避免“导入后什么都没发生”。
            Debug.WriteLine($"[Import] 文件占位卡片导入：'{file.Path}'");
            ImportFilePlaceholder(file, offsetIndex);
        }

        private async Task ImportImageFileAsync(StorageFile file, int offsetIndex)
        {
            (byte[] pixels, int w, int h)? decoded = await TryDecodeImageToBgra8PremulAsync(file, maxPixelEdge: 2048);

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
                await ShowMessageDialogAsync(xamlRoot, "导入失败", "无法获取窗口句柄。");
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
            WbixPreview? preview = await TryReadWbixPreviewAsync(file.Path);
            if (preview is null)
            {
                await ShowMessageDialogAsync(xamlRoot, "导入失败", "WBIX 文件解析失败。");
                return;
            }

            WbixImportMode? mode = await ShowWbixImportConfirmDialogAsync(xamlRoot, file.Name, preview);
            if (mode is null)
            {
                return;
            }

            if (mode == WbixImportMode.ReplaceCurrentPage)
            {
                bool confirmed = await ConfirmWbixReplaceCurrentPageRiskAsync(xamlRoot);
                if (!confirmed)
                {
                    return;
                }
            }

            var serializer = new WbixWorkspaceSerializer();

            await RunBusyDialogAsync(xamlRoot, "正在导入 WBIX…", async () =>
            {
                BoardWorkspaceSnapshot snapshot = await Task.Run(async () =>
                {
                    await using var stream = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    return await serializer.LoadAsync(stream);
                });

                List<BoardPage> pages = BoardWorkspaceSnapshotApplier.CreatePages(snapshot);

                if (mode == WbixImportMode.ReplaceCurrentPage)
                {
                    Debug.WriteLine($"[Import/WBIX] 替换工作区：pages={pages.Count}, currentIndex={snapshot.CurrentIndex}");
                    if (pages.Count == 0)
                    {
                        Debug.WriteLine("[Import/WBIX] pages=0，忽略导入。");
                        return;
                    }

                    int insertIndex = _workspace.CurrentIndex;
                    int replaceImportCurrent = Math.Clamp(snapshot.CurrentIndex, 0, Math.Max(0, pages.Count - 1));

                    Debug.WriteLine($"[Import/WBIX] 覆盖当前页并插入：workspaceCurrent={insertIndex}, importPages={pages.Count}, importCurrent={replaceImportCurrent}");

                    // 覆盖当前页：用导入文件的第 1 页替换当前页，然后把剩余页插入到其后。
                    _workspace.ReplacePageAt(insertIndex, pages[0]);

                    if (pages.Count > 1)
                    {
                        _workspace.InsertPages(insertIndex + 1, pages.GetRange(1, pages.Count - 1), switchToFirstInsertedPage: false);
                    }

                    int replaceTargetIndex = Math.Clamp(insertIndex + replaceImportCurrent, 0, Math.Max(0, _workspace.Pages.Count - 1));
                    Debug.WriteLine($"[Import/WBIX] 覆盖导入完成：switchTo={replaceTargetIndex}, pagesAfter={_workspace.Pages.Count}");
                    _workspace.SetCurrentIndex(replaceTargetIndex);
                    return;
                }

                int startIndex = _workspace.AppendPages(pages, switchToFirstAppendedPage: false);
                int importCurrent = Math.Clamp(snapshot.CurrentIndex, 0, Math.Max(0, pages.Count - 1));
                int targetIndex = Math.Clamp(startIndex + importCurrent, 0, Math.Max(0, _workspace.Pages.Count - 1));
                Debug.WriteLine($"[Import/WBIX] 追加页面：startIndex={startIndex}, pages={pages.Count}, switchTo={targetIndex}");
                _workspace.SetCurrentIndex(targetIndex);
            }, message: "正在导入，请稍候…");
        }

        private static async Task<bool> ConfirmWbixReplaceRiskAsync(XamlRoot xamlRoot)
        {
            var dialog = new ContentDialog
            {
                Title = "风险提示",
                Content = "选择“覆盖整个工作区”将替换当前所有页面内容。\n\n建议在覆盖前先导出备份（WBIX）。\n\n是否继续？",
                PrimaryButtonText = "继续覆盖",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = xamlRoot,
            };

            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }

        private static async Task<bool> ConfirmWbixReplaceCurrentPageRiskAsync(XamlRoot xamlRoot)
        {
            var dialog = new ContentDialog
            {
                Title = "风险提示",
                Content = "选择“覆盖当前页”将替换当前页内容，并在其后插入导入文件的其余页面。\n\n当前页内容将丢失，且该操作无法撤销。\n\n建议覆盖前先导出备份（WBIX）。\n\n是否继续？",
                PrimaryButtonText = "继续覆盖",
                CloseButtonText = "取消",
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
                        Text = "（无封面）",
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                };
            }

            int pageCount = preview.Manifest.Pages?.Count ?? 0;
            string created = preview.Manifest.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            string info = $"文件：{fileName}\n页数：{pageCount}\n版本：{preview.Manifest.Version}\n创建：{created}";

            var rbAppend = new RadioButton { Content = "新增在最后一页之后", IsChecked = true };
            var rbReplace = new RadioButton { Content = "覆盖当前页（有风险）" };

            var panel = new StackPanel { Spacing = 12 };
            panel.Children.Add(cover);
            panel.Children.Add(new TextBlock { Text = info, TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(new TextBlock { Text = "插入方式：", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            panel.Children.Add(rbAppend);
            panel.Children.Add(rbReplace);

            var dialog = new ContentDialog
            {
                Title = "导入 WBIX",
                Content = panel,
                PrimaryButtonText = "导入",
                CloseButtonText = "取消",
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

        private static async Task<WbixPreview?> TryReadWbixPreviewAsync(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return null;
            }

            try
            {
                await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);

                ZipArchiveEntry? manifestEntry = archive.GetEntry("manifest.json");
                if (manifestEntry is null)
                {
                    return null;
                }

                WbixManifest manifest;
                await using (Stream ms = manifestEntry.Open())
                using (var reader = new StreamReader(ms, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: false))
                {
                    string json = await reader.ReadToEndAsync();
                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        AllowTrailingCommas = true,
                        ReadCommentHandling = JsonCommentHandling.Skip,
                    };

                    manifest = JsonSerializer.Deserialize<WbixManifest>(json, options)
                        ?? throw new InvalidDataException("manifest.json 解析失败。");
                }

                string? coverPath = TryResolveCoverPathFromManifest(manifest);
                byte[]? coverBytes = null;

                // 封面属于可选资源：缺失时允许降级。
                if (!string.IsNullOrWhiteSpace(coverPath))
                {
                    coverBytes = TryReadZipEntryBytes(archive, coverPath!, maxBytes: 8 * 1024 * 1024);
                }

                coverBytes ??= TryReadZipEntryBytes(archive, "assets/cover.png", maxBytes: 8 * 1024 * 1024);

                return new WbixPreview(manifest, coverBytes);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Import/WBIX] 预读失败：'{filePath}', ex={ex}");
                return null;
            }
        }

        private static string? TryResolveCoverPathFromManifest(WbixManifest manifest)
        {
            if (manifest.Resources is null)
            {
                return null;
            }

            foreach (WbixResourceEntry r in manifest.Resources)
            {
                if (string.Equals(r.Id, "cover", StringComparison.OrdinalIgnoreCase))
                {
                    return r.Path;
                }

                if (r.Meta is not null
                    && r.Meta.TryGetValue("role", out string? role)
                    && string.Equals(role, "cover", StringComparison.OrdinalIgnoreCase))
                {
                    return r.Path;
                }
            }

            return null;
        }

        private static byte[]? TryReadZipEntryBytes(ZipArchive archive, string entryName, int maxBytes)
        {
            try
            {
                ZipArchiveEntry? entry = archive.GetEntry(entryName);
                if (entry is null)
                {
                    return null;
                }

                if (entry.Length <= 0 || entry.Length > maxBytes)
                {
                    return null;
                }

                using Stream s = entry.Open();
                using var ms = new MemoryStream((int)Math.Min(int.MaxValue, entry.Length));
                s.CopyTo(ms);
                return ms.ToArray();
            }
            catch
            {
                return null;
            }
        }

        private static async Task<(byte[] pixels, int w, int h)?> TryDecodeImageToBgra8PremulAsync(StorageFile file, int maxPixelEdge)
        {
            try
            {
                using IRandomAccessStream stream = await file.OpenReadAsync();
                BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);

                uint w = decoder.PixelWidth;
                uint h = decoder.PixelHeight;
                uint maxEdge = Math.Max(w, h);

                double scale = 1.0;
                if (maxEdge > (uint)Math.Max(1, maxPixelEdge))
                {
                    scale = (double)maxPixelEdge / maxEdge;
                }

                uint sw = (uint)Math.Max(1.0, Math.Round(w * scale));
                uint sh = (uint)Math.Max(1.0, Math.Round(h * scale));

                var transform = new BitmapTransform
                {
                    ScaledWidth = sw,
                    ScaledHeight = sh,
                    InterpolationMode = BitmapInterpolationMode.Fant,
                };

                PixelDataProvider provider = await decoder.GetPixelDataAsync(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Premultiplied,
                    transform,
                    ExifOrientationMode.RespectExifOrientation,
                    ColorManagementMode.DoNotColorManage);

                byte[] pixels = provider.DetachPixelData();
                return (pixels, (int)sw, (int)sh);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Import] 图片解码失败：'{file.Path}', ex={ex}");
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

        private static async Task<string> ReadTextFileWithLimitAsync(string path, int maxChars)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

                var sb = new StringBuilder(Math.Min(maxChars, 4096));
                char[] buffer = new char[2048];

                int remaining = Math.Max(0, maxChars);
                while (remaining > 0)
                {
                    int read = await reader.ReadAsync(buffer, 0, Math.Min(buffer.Length, remaining));
                    if (read <= 0)
                    {
                        break;
                    }

                    sb.Append(buffer, 0, read);
                    remaining -= read;
                }

                if (!reader.EndOfStream)
                {
                    sb.Append("\n\n（内容过长，已截断）");
                }

                return sb.ToString();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Import] 读取文本失败：'{path}', ex={ex}");
                return "（读取失败）";
            }
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
