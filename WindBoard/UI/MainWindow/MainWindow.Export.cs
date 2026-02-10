using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;
using WindBoard.Board.Persistence;
using WindBoard.Exporting;

namespace WindBoard
{
    /// <summary>
    /// 主窗口：导出相关代码。
    /// </summary>
    public sealed partial class MainWindow
    {
        private readonly IBoardExportService _exportService = new BoardExportService();

        private enum ExportFormat
        {
            Png,
            Pdf,
            Wbix,
        }

        private enum ExportPageScope
        {
            Current,
            All,
            Range,
        }

        private sealed record ExportDialogSelection(
            ExportFormat Format,
            ExportPageScope PageScope,
            string PageRangeText,
            int Dpi,
            float PaddingDip,
            BoardRasterFixedFrame? PngFixedFrame);

        private async Task StartExportAsync()
        {
            XamlRoot? xamlRoot = TryGetDialogXamlRoot();
            if (xamlRoot is null)
            {
                return;
            }

            ExportDialogSelection? selection = await ShowExportDialogAsync(xamlRoot);
            if (selection is null)
            {
                return;
            }

            // 导出建议基于快照进行：避免导出耗时期间用户继续编辑导致数据竞争或导出内容不一致。
            BoardWorkspaceSnapshot snapshot = BoardWorkspaceSnapshotConverter.CreateSnapshot(_workspace);

            Vector2 fallbackViewportSizeDip = GetFallbackViewportSizeDip();

            var rasterOptions = new BoardRasterExportOptions(
                Dpi: selection.Dpi,
                PaddingDip: selection.PaddingDip,
                BackgroundColor: BoardCanvas.CanvasBackgroundColor,
                FallbackViewportSizeDip: fallbackViewportSizeDip,
                FixedFrame: selection.Format == ExportFormat.Png ? selection.PngFixedFrame : null);

            try
            {
                // WBIX 固定导出整个工作区：忽略页范围选择。
                if (selection.Format == ExportFormat.Wbix)
                {
                    await ExportWbixAsync(xamlRoot, snapshot);
                    return;
                }

                List<int> pageIndices;
                if (!TryResolvePageIndices(selection, out pageIndices, out string pageError))
                {
                    await ShowMessageDialogAsync(xamlRoot, "页码范围错误", pageError);
                    return;
                }

                switch (selection.Format)
                {
                    case ExportFormat.Wbix:
                        await ExportWbixAsync(xamlRoot, snapshot);
                        return;

                    case ExportFormat.Pdf:
                        await ExportPdfAsync(xamlRoot, snapshot, pageIndices, rasterOptions);
                        return;

                    case ExportFormat.Png:
                        await ExportPngAsync(xamlRoot, snapshot, pageIndices, rasterOptions);
                        return;

                    default:
                        await ShowMessageDialogAsync(xamlRoot, "导出失败", "未知导出格式。");
                        return;
                }
            }
            catch (Exception ex)
            {
                await ShowMessageDialogAsync(xamlRoot, "导出失败", ex.Message);
            }
        }

        private Vector2 GetFallbackViewportSizeDip()
        {
            // 使用当前画布控件的实际尺寸作为“空页面导出尺寸”的兜底。
            // 注意：ActualWidth/ActualHeight 的单位是 DIP。
            float w = (float)Math.Max(1.0, BoardCanvas.ActualWidth);
            float h = (float)Math.Max(1.0, BoardCanvas.ActualHeight);
            return new Vector2(w, h);
        }

        private bool TryResolvePageIndices(ExportDialogSelection selection, out List<int> pageIndices, out string errorMessage)
        {
            pageIndices = new List<int>();
            errorMessage = string.Empty;

            int pageCount = _workspace.Pages.Count;
            if (pageCount <= 0)
            {
                errorMessage = "当前工作区没有可导出的页面。";
                return false;
            }

            switch (selection.PageScope)
            {
                case ExportPageScope.Current:
                    pageIndices.Add(_workspace.CurrentIndex);
                    return true;

                case ExportPageScope.All:
                    for (int i = 0; i < pageCount; i++)
                    {
                        pageIndices.Add(i);
                    }
                    return true;

                case ExportPageScope.Range:
                    return PageRangeParser.TryParse(selection.PageRangeText, pageCount, out pageIndices, out errorMessage);

                default:
                    errorMessage = "未知页范围。";
                    return false;
            }
        }

        private async Task ExportWbixAsync(XamlRoot xamlRoot, BoardWorkspaceSnapshot snapshot)
        {
            StorageFile? file = await PickSaveFileWithOverwriteConfirmAsync(xamlRoot, ExportFormat.Wbix);
            if (file is null)
            {
                return;
            }

            await RunBusyDialogAsync(xamlRoot, "正在导出 WBIX…", async () =>
            {
                await _exportService.ExportWbixAsync(snapshot, file.Path);
            });

            await ShowMessageDialogAsync(xamlRoot, "导出完成", $"已导出：{file.Path}");
        }

        private async Task ExportPdfAsync(XamlRoot xamlRoot, BoardWorkspaceSnapshot snapshot, List<int> pageIndices, BoardRasterExportOptions rasterOptions)
        {
            StorageFile? file = await PickSaveFileWithOverwriteConfirmAsync(xamlRoot, ExportFormat.Pdf);
            if (file is null)
            {
                return;
            }

            var options = new BoardPdfExportOptions(rasterOptions);

            await RunBusyDialogAsync(xamlRoot, "正在导出 PDF…", async () =>
            {
                await _exportService.ExportPdfAsync(snapshot, pageIndices, file.Path, options);
            });

            await ShowMessageDialogAsync(xamlRoot, "导出完成", $"已导出：{file.Path}");
        }

        private async Task ExportPngAsync(XamlRoot xamlRoot, BoardWorkspaceSnapshot snapshot, List<int> pageIndices, BoardRasterExportOptions rasterOptions)
        {
            if (pageIndices.Count == 1)
            {
                StorageFile? file = await PickSaveFileWithOverwriteConfirmAsync(xamlRoot, ExportFormat.Png);
                if (file is null)
                {
                    return;
                }

                await RunBusyDialogAsync(xamlRoot, "正在导出 PNG…", async () =>
                {
                    await _exportService.ExportPngAsync(snapshot, pageIndices[0], file.Path, rasterOptions);
                });

                await ShowMessageDialogAsync(xamlRoot, "导出完成", $"已导出：{file.Path}");
                return;
            }

            StorageFolder? folder = await PickFolderAsync(xamlRoot);
            if (folder is null)
            {
                return;
            }

            // 多页 PNG：在用户选择的目录下创建“WindBoard-年-月-日”文件夹，内部文件名为“年-月-日-页码”。
            DateTimeOffset now = DateTimeOffset.Now;
            string date = FormatDate(now);
            string exportFolderPath = Path.Combine(folder.Path, $"WindBoard-{date}");

            if (TryGetPngBatchConflicts(snapshot, pageIndices, exportFolderPath, date, out List<string> conflicts)
                && conflicts.Count > 0)
            {
                bool overwrite = await ConfirmOverwriteFilesAsync(xamlRoot, exportFolderPath, conflicts);
                if (!overwrite)
                {
                    return;
                }
            }

            await RunBusyDialogAsync(xamlRoot, "正在导出 PNG…", async () =>
            {
                await _exportService.ExportPngPagesToFolderAsync(snapshot, pageIndices, exportFolderPath, date, rasterOptions);
            });

            await ShowMessageDialogAsync(xamlRoot, "导出完成", $"已导出到目录：{exportFolderPath}");
        }

        private async Task<ExportDialogSelection?> ShowExportDialogAsync(XamlRoot xamlRoot)
        {
            var formatCombo = new ComboBox
            {
                SelectedIndex = 0,
                Items =
                {
                    "PNG 图片",
                    "PDF 文档",
                    "WBIX 源文件 (.wbix)",
                }
            };

            var scopeCombo = new ComboBox
            {
                SelectedIndex = 0,
                Items =
                {
                    "当前页",
                    "全部页",
                    "指定范围（例如：1,3-5）",
                }
            };

            var rangeBox = new TextBox
            {
                PlaceholderText = "例如：1,3-5",
                IsEnabled = false,
            };

            var pngAspectCombo = new ComboBox
            {
                SelectedIndex = 0,
                Items =
                {
                    "1:1（方形）",
                    "4:3",
                    "16:9",
                }
            };

            var pngSizeCombo = new ComboBox
            {
                SelectedIndex = 1,
            };

            var dpiCombo = new ComboBox
            {
                SelectedIndex = 1,
                Items =
                {
                    "标准（96 DPI）",
                    "高清（192 DPI）",
                    "超清（288 DPI）",
                }
            };

            var paddingBox = new TextBox
            {
                Text = "24",
                PlaceholderText = "边距（DIP）",
            };

            var pngAspectLabel = new TextBlock { Text = "画面比例（PNG）" };
            var pngSizeLabel = new TextBlock { Text = "输出分辨率（PNG）" };
            var dpiLabel = new TextBlock { Text = "清晰度（PDF）" };
            var paddingLabel = new TextBlock { Text = "内容留白（DIP）" };

            var hintText = new TextBlock
            {
                Text = "提示：PNG 会按固定比例/分辨率导出（内容等比缩放并居中）；PNG 多页会导出到文件夹；PDF 会生成多页文档；WBIX 会包含全部页面与后续资源扩展位。",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.85,
            };

            void UpdateRangeEnabled()
            {
                rangeBox.IsEnabled = scopeCombo.SelectedIndex == (int)ExportPageScope.Range;
            }

            void UpdatePngSizeItems()
            {
                int oldIndex = Math.Clamp(pngSizeCombo.SelectedIndex, 0, 2);
                pngSizeCombo.Items.Clear();

                bool square = pngAspectCombo.SelectedIndex == 0;
                bool fourThree = pngAspectCombo.SelectedIndex == 1;

                (int w, int h) size720 = ComputePngSize(square, fourThree, baseHeight: 720);
                (int w, int h) size1080 = ComputePngSize(square, fourThree, baseHeight: 1080);
                (int w, int h) size4k = ComputePngSize(square, fourThree, baseHeight: 2160);

                pngSizeCombo.Items.Add($"720P（{size720.w}×{size720.h}）");
                pngSizeCombo.Items.Add($"1080P（{size1080.w}×{size1080.h}）");
                pngSizeCombo.Items.Add($"4K（{size4k.w}×{size4k.h}）");

                pngSizeCombo.SelectedIndex = oldIndex;
            }

            void UpdateRasterOptionsVisibility()
            {
                bool isPng = formatCombo.SelectedIndex == (int)ExportFormat.Png;
                bool isPdf = formatCombo.SelectedIndex == (int)ExportFormat.Pdf;
                bool showRasterOptions = isPng || isPdf;

                pngAspectLabel.Visibility = isPng ? Visibility.Visible : Visibility.Collapsed;
                pngAspectCombo.Visibility = isPng ? Visibility.Visible : Visibility.Collapsed;
                pngSizeLabel.Visibility = isPng ? Visibility.Visible : Visibility.Collapsed;
                pngSizeCombo.Visibility = isPng ? Visibility.Visible : Visibility.Collapsed;

                dpiLabel.Visibility = isPdf ? Visibility.Visible : Visibility.Collapsed;
                dpiCombo.Visibility = isPdf ? Visibility.Visible : Visibility.Collapsed;

                paddingLabel.Visibility = showRasterOptions ? Visibility.Visible : Visibility.Collapsed;
                paddingBox.Visibility = showRasterOptions ? Visibility.Visible : Visibility.Collapsed;
            }

            scopeCombo.SelectionChanged += (_, _) => UpdateRangeEnabled();
            formatCombo.SelectionChanged += (_, _) => UpdateRasterOptionsVisibility();
            pngAspectCombo.SelectionChanged += (_, _) => UpdatePngSizeItems();

            UpdateRangeEnabled();
            UpdatePngSizeItems();
            UpdateRasterOptionsVisibility();

            var panel = new StackPanel { Spacing = 10 };

            panel.Children.Add(new TextBlock { Text = "导出格式" });
            panel.Children.Add(formatCombo);
            panel.Children.Add(new TextBlock { Text = "导出页面" });
            panel.Children.Add(scopeCombo);
            panel.Children.Add(rangeBox);
            panel.Children.Add(pngAspectLabel);
            panel.Children.Add(pngAspectCombo);
            panel.Children.Add(pngSizeLabel);
            panel.Children.Add(pngSizeCombo);
            panel.Children.Add(dpiLabel);
            panel.Children.Add(dpiCombo);
            panel.Children.Add(paddingLabel);
            panel.Children.Add(paddingBox);
            panel.Children.Add(hintText);

            var dialog = new ContentDialog
            {
                Title = "导出",
                Content = panel,
                PrimaryButtonText = "下一步",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = xamlRoot,
            };

            ContentDialogResult result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return null;
            }

            var format = (ExportFormat)Math.Clamp(formatCombo.SelectedIndex, 0, 2);
            var scope = (ExportPageScope)Math.Clamp(scopeCombo.SelectedIndex, 0, 2);
            string range = rangeBox.Text ?? string.Empty;
            int dpi = format == ExportFormat.Pdf
                ? dpiCombo.SelectedIndex switch
                {
                    0 => 96,
                    2 => 288,
                    _ => 192,
                }
                : 96;

            float paddingDip = 24.0f;
            if (float.TryParse(paddingBox.Text, out float parsedPadding))
            {
                paddingDip = Math.Max(0.0f, parsedPadding);
            }

            BoardRasterFixedFrame? pngFrame = null;
            if (format == ExportFormat.Png)
            {
                bool square = pngAspectCombo.SelectedIndex == 0;
                bool fourThree = pngAspectCombo.SelectedIndex == 1;

                int baseHeight = pngSizeCombo.SelectedIndex switch
                {
                    0 => 720,
                    2 => 2160,
                    _ => 1080,
                };

                (int w, int h) size = ComputePngSize(square, fourThree, baseHeight);
                pngFrame = new BoardRasterFixedFrame(size.w, size.h);
            }

            return new ExportDialogSelection(format, scope, range, dpi, paddingDip, pngFrame);
        }

        private static (int w, int h) ComputePngSize(bool square, bool fourThree, int baseHeight)
        {
            int h = Math.Max(1, baseHeight);

            if (square)
            {
                return (h, h);
            }

            if (fourThree)
            {
                int w43 = (int)Math.Round(h * 4.0 / 3.0);
                return (Math.Max(1, w43), h);
            }

            // 16:9
            int w169 = (int)Math.Round(h * 16.0 / 9.0);
            return (Math.Max(1, w169), h);
        }

        private async Task RunBusyDialogAsync(XamlRoot xamlRoot, string title, Func<Task> action, string message = "正在导出，请稍候…")
        {
            var ring = new ProgressRing
            {
                IsActive = true,
                Width = 32,
                Height = 32,
                HorizontalAlignment = HorizontalAlignment.Center,
            };

            var text = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center,
            };

            var content = new StackPanel { Spacing = 12 };
            content.Children.Add(ring);
            content.Children.Add(text);

            var dialog = new ContentDialog
            {
                Title = title,
                Content = content,
                XamlRoot = xamlRoot,
            };

            var _ = dialog.ShowAsync();
            try
            {
                await action();
            }
            finally
            {
                try
                {
                    dialog.Hide();
                }
                catch
                {
                    // 忽略关闭失败：导出流程不应因弹窗状态异常而中断
                }
            }
        }

        private static async Task ShowMessageDialogAsync(XamlRoot xamlRoot, string title, string message)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = "关闭",
                XamlRoot = xamlRoot,
            };

            await dialog.ShowAsync();
        }

        private async Task<StorageFile?> PickSaveFileAsync(XamlRoot xamlRoot, ExportFormat format)
        {
            IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            if (hwnd == IntPtr.Zero)
            {
                await ShowMessageDialogAsync(xamlRoot, "导出失败", "无法获取窗口句柄。");
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
                    picker.FileTypeChoices.Add("PNG 图片", new List<string> { ".png" });
                    picker.SuggestedFileName = $"WindBoard-{date}-{time}";
                    break;

                case ExportFormat.Pdf:
                    picker.FileTypeChoices.Add("PDF 文档", new List<string> { ".pdf" });
                    picker.SuggestedFileName = $"WindBoard-{date}-{time}";
                    break;

                case ExportFormat.Wbix:
                    picker.FileTypeChoices.Add("WBIX 源文件", new List<string> { ".wbix" });
                    picker.SuggestedFileName = $"{date}-{time}";
                    break;

                default:
                    picker.FileTypeChoices.Add("文件", new List<string> { "*" });
                    picker.SuggestedFileName = "windboard";
                    break;
            }

            return await picker.PickSaveFileAsync();
        }

        private async Task<StorageFile?> PickSaveFileWithOverwriteConfirmAsync(XamlRoot xamlRoot, ExportFormat format)
        {
            while (true)
            {
                DateTimeOffset pickStarted = DateTimeOffset.Now;
                StorageFile? file = await PickSaveFileAsync(xamlRoot, format);
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

        private static string FormatDate(DateTimeOffset now)
        {
            return now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        private static string FormatTimeHHmm(DateTimeOffset now)
        {
            return now.ToString("HHmm", CultureInfo.InvariantCulture);
        }

        private static bool TryGetPngBatchConflicts(
            BoardWorkspaceSnapshot snapshot,
            List<int> pageIndices,
            string folderPath,
            string datePrefix,
            out List<string> conflictPaths)
        {
            conflictPaths = new List<string>();

            if (snapshot is null)
            {
                return false;
            }

            if (pageIndices is null)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(folderPath))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(datePrefix))
            {
                return false;
            }

            foreach (int index in pageIndices)
            {
                if ((uint)index >= (uint)snapshot.Pages.Count)
                {
                    continue;
                }

                string fileName = $"{datePrefix}-{index + 1:000}.png";
                string path = Path.Combine(folderPath, fileName);
                if (File.Exists(path))
                {
                    conflictPaths.Add(path);
                }
            }

            return true;
        }

        private static async Task<bool> ConfirmOverwriteFileAsync(XamlRoot xamlRoot, string filePath)
        {
            var dialog = new ContentDialog
            {
                Title = "确认覆盖",
                Content = $"文件已存在：{filePath}\n是否覆盖？",
                PrimaryButtonText = "覆盖",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = xamlRoot,
            };

            ContentDialogResult result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary;
        }

        private static async Task<bool> ConfirmOverwriteFilesAsync(XamlRoot xamlRoot, string folderPath, List<string> conflictPaths)
        {
            if (conflictPaths is null || conflictPaths.Count <= 0)
            {
                return true;
            }

            int count = conflictPaths.Count;
            string preview = count <= 3
                ? string.Join("\n", conflictPaths)
                : string.Join("\n", conflictPaths.GetRange(0, 3)) + $"\n……（共 {count} 个）";

            var dialog = new ContentDialog
            {
                Title = "确认覆盖",
                Content = $"目标目录中存在同名文件：{folderPath}\n\n将覆盖以下文件：\n{preview}\n\n是否继续？",
                PrimaryButtonText = "覆盖",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = xamlRoot,
            };

            ContentDialogResult result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary;
        }

        private async Task<StorageFolder?> PickFolderAsync(XamlRoot xamlRoot)
        {
            IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            if (hwnd == IntPtr.Zero)
            {
                await ShowMessageDialogAsync(xamlRoot, "导出失败", "无法获取窗口句柄。");
                return null;
            }

            var picker = new FolderPicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            // FolderPicker 也需要 FileTypeFilter（WinUI 3 桌面端约束）。
            picker.FileTypeFilter.Clear();
            picker.FileTypeFilter.Add("*");

            return await picker.PickSingleFolderAsync();
        }
    }
}
