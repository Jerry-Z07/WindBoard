using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Ink;
using System.Windows.Media.Imaging;
using MaterialDesignThemes.Wpf;
using WindBoard.Services.Export;
using WindBoard.Views.Dialogs;

namespace WindBoard
{
    public partial class MainWindow
    {
        private async void BtnImport_Click(object sender, RoutedEventArgs e)
        {
            var result = await DialogHost.Show(new ImportDialog(), "MainDialogHost");

            // 处理 WBI 导入
            if (result is WbiImportRequest wbiReq)
            {
                await ImportWbiAsync(wbiReq);
                return;
            }

            // 处理普通导入
            if (result is not ImportRequest req) return;
            await ImportAttachmentsAsync(req);
        }

        private async Task ImportWbiAsync(WbiImportRequest request)
        {
            try
            {
                var importer = new WbiImporter();
                var progress = new Progress<Models.Export.ExportProgress>(p =>
                {
                    // 可以在这里更新进度 UI
                });

                var importResult = await importer.ImportAsync(request.FilePath, null, progress);

                if (!importResult.Success)
                {
                    MessageBox.Show($"导入失败: {importResult.ErrorMessage}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (importResult.Pages.Count == 0)
                {
                    MessageBox.Show("WBI 文件中没有可导入的页面。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // 执行导入
                if (request.ReplaceExistingPages)
                {
                    // 替换所有页面
                    await ReplaceAllPagesWithWbiAsync(importResult.Pages);
                }
                else
                {
                    // 追加页面
                    await AppendWbiPagesAsync(importResult.Pages);
                }

                // 显示缺失资源警告
                if (importResult.MissingResources.Count > 0)
                {
                    string missingList = string.Join("\n", importResult.MissingResources.Take(10));
                    if (importResult.MissingResources.Count > 10)
                    {
                        missingList += $"\n...共 {importResult.MissingResources.Count} 个资源缺失";
                    }

                    MessageBox.Show(
                        $"导入完成，但以下资源文件未找到:\n\n{missingList}\n\n这些附件将显示为占位符。",
                        "部分资源缺失",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                else
                {
                    MessageBox.Show(
                        $"成功导入 {importResult.Pages.Count} 个页面！",
                        "导入完成",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"导入过程中发生错误: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task ReplaceAllPagesWithWbiAsync(List<BoardPage> newPages)
        {
            // 保存当前页状态
            _pageService?.SaveCurrentPage();

            // 清空现有页面
            var pages = _pageService?.Pages;
            if (pages == null) return;

            pages.Clear();

            // 添加新页面
            foreach (var page in newPages)
            {
                pages.Add(page);
            }

            // 切换到第一页
            if (pages.Count > 0)
            {
                // 重新编号
                for (int i = 0; i < pages.Count; i++)
                {
                    pages[i].Number = i + 1;
                }

                // 加载第一页到画布
                LoadPageIntoCanvas(pages[0]);
            }

            // 异步加载所有图片附件
            foreach (var page in newPages)
            {
                foreach (var att in page.Attachments.Where(a => a.Type == BoardAttachmentType.Image && !string.IsNullOrEmpty(a.FilePath)))
                {
                    _ = LoadImageIntoAttachmentAsync(att, att.FilePath!);
                }
            }

            await Task.CompletedTask;
        }

        private async Task AppendWbiPagesAsync(List<BoardPage> newPages)
        {
            // 保存当前页状态
            _pageService?.SaveCurrentPage();

            var pages = _pageService?.Pages;
            if (pages == null) return;

            int startNumber = pages.Count + 1;

            // 添加新页面
            foreach (var page in newPages)
            {
                page.Number = startNumber++;
                pages.Add(page);
            }

            // 切换到第一个新导入的页面
            int newPageIndex = pages.Count - newPages.Count;
            if (newPageIndex >= 0 && newPageIndex < pages.Count)
            {
                _pageService?.SwitchToPage(newPageIndex);
            }

            // 异步加载所有图片附件
            foreach (var page in newPages)
            {
                foreach (var att in page.Attachments.Where(a => a.Type == BoardAttachmentType.Image && !string.IsNullOrEmpty(a.FilePath)))
                {
                    _ = LoadImageIntoAttachmentAsync(att, att.FilePath!);
                }
            }

            await Task.CompletedTask;
        }

        private void LoadPageIntoCanvas(BoardPage page)
        {
            // 设置画布尺寸
            MyCanvas.Width = page.CanvasWidth;
            MyCanvas.Height = page.CanvasHeight;

            // 设置笔迹
            MyCanvas.Strokes = page.Strokes ?? new StrokeCollection();

            // 设置视图状态
            _zoomPanService?.SetViewDirect(page.Zoom, page.PanX, page.PanY);

            // 重新绑定笔迹事件
            _pageService?.AttachStrokeEvents();

            // 更新附件显示
            OnPropertyChanged(nameof(CurrentAttachments));
        }

        private async Task ImportAttachmentsAsync(ImportRequest req)
        {
            var page = _pageService.CurrentPage;
            if (page == null) return;

            var center = GetViewportCenterCanvasPoint();
            int baseZ = GetNextAttachmentZIndex(page, pinnedTop: false);

            var pendingAdds = new List<BoardAttachment>();

            async Task AddAttachmentAsync(BoardAttachment a)
            {
                pendingAdds.Add(a);
                if (pendingAdds.Count >= 16)
                {
                    foreach (var it in pendingAdds) page.Attachments.Add(it);
                    pendingAdds.Clear();
                    await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
                }
            }

            double cellW = 420;
            double cellH = 280;
            double gap = 24;
            int colCount = 4;
            int index = 0;

            Point NextPos()
            {
                int col = index % colCount;
                int row = index / colCount;
                index++;
                return new Point(center.X + col * (cellW + gap), center.Y + row * (cellH + gap));
            }

            var imageAttachments = new List<(BoardAttachment attachment, string path)>();

            foreach (var path in req.ImagePaths.Where(File.Exists))
            {
                var pos = NextPos();
                var att = new BoardAttachment
                {
                    Type = BoardAttachmentType.Image,
                    FilePath = path,
                    X = pos.X,
                    Y = pos.Y,
                    Width = 480,
                    Height = 320,
                    ZIndex = baseZ++
                };
                await AddAttachmentAsync(att);
                imageAttachments.Add((att, path));
            }

            foreach (var path in req.VideoPaths.Where(File.Exists))
            {
                var pos = NextPos();
                await AddAttachmentAsync(new BoardAttachment
                {
                    Type = BoardAttachmentType.Video,
                    FilePath = path,
                    X = pos.X,
                    Y = pos.Y,
                    Width = 480,
                    Height = 270,
                    ZIndex = baseZ++
                });
            }

            foreach (var path in req.TextFilePaths.Where(File.Exists))
            {
                string? content = await ReadTextFileAsync(path);
                if (string.IsNullOrWhiteSpace(content)) continue;

                var pos = NextPos();
                await AddAttachmentAsync(new BoardAttachment
                {
                    Type = BoardAttachmentType.Text,
                    FilePath = path,
                    Text = content,
                    X = pos.X,
                    Y = pos.Y,
                    Width = 420,
                    Height = 260,
                    ZIndex = baseZ++
                });
            }

            if (!string.IsNullOrWhiteSpace(req.TextContent))
            {
                var pos = NextPos();
                await AddAttachmentAsync(new BoardAttachment
                {
                    Type = BoardAttachmentType.Text,
                    Text = req.TextContent,
                    X = pos.X,
                    Y = pos.Y,
                    Width = 420,
                    Height = 260,
                    ZIndex = baseZ++
                });
            }

            foreach (var url in req.Urls)
            {
                if (!TryNormalizeHttpUrl(url, out var normalized)) continue;

                var pos = NextPos();
                await AddAttachmentAsync(new BoardAttachment
                {
                    Type = BoardAttachmentType.Link,
                    Url = normalized,
                    X = pos.X,
                    Y = pos.Y,
                    Width = 360,
                    Height = 120,
                    ZIndex = baseZ++
                });
            }

            foreach (var it in pendingAdds) page.Attachments.Add(it);
            pendingAdds.Clear();

            // 确保 ItemsSource 已绑定到当前页附件集合（防止资源/绑定初始化早于架构初始化时的空引用导致不刷新）
            OnPropertyChanged(nameof(CurrentAttachments));

            // 图片异步解码（避免 UI 卡顿；解码完成后再回到 UI 线程赋值）
            foreach (var (attachment, path) in imageAttachments)
            {
                _ = LoadImageIntoAttachmentAsync(attachment, path);
            }

            if (page.Attachments.Count > 0)
            {
                var newest = page.Attachments.LastOrDefault();
                if (newest != null)
                {
                    SelectAttachment(newest);
                    RadioSelect.IsChecked = true;
                }
            }
        }

        private async Task LoadImageIntoAttachmentAsync(BoardAttachment attachment, string path)
        {
            try
            {
                int decodeWidth = (int)Math.Clamp(attachment.Width * 2.0, 400, 2048);
                var image = await _bitmapLoader.LoadAsync(path, decodeWidth).ConfigureAwait(false);

                await Dispatcher.InvokeAsync(() =>
                {
                    attachment.Image = image;

                    if (image is BitmapSource bs && bs.PixelWidth > 0 && bs.PixelHeight > 0)
                    {
                        double aspect = (double)bs.PixelWidth / bs.PixelHeight;
                        attachment.Height = Math.Max(AttachmentMinSize, attachment.Width / aspect);
                    }
                });
            }
            catch
            {
                // ignore decode failures (keep placeholder card)
            }
        }

        private static bool TryNormalizeHttpUrl(string raw, out string normalized)
        {
            normalized = string.Empty;
            if (string.IsNullOrWhiteSpace(raw)) return false;

            raw = raw.Trim();
            if (!raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                raw = "https://" + raw;
            }

            if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri)) return false;
            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            normalized = uri.ToString();
            return true;
        }

        private static async Task<string?> ReadTextFileAsync(string path)
        {
            try
            {
                byte[] bytes = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
                if (bytes.Length == 0) return null;

                Encoding enc = DetectEncodingFromBom(bytes) ?? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);
                return enc.GetString(bytes);
            }
            catch
            {
                return null;
            }
        }

        private static Encoding? DetectEncodingFromBom(byte[] bytes)
        {
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) return Encoding.UTF8;
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return Encoding.Unicode; // UTF-16 LE
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) return Encoding.BigEndianUnicode; // UTF-16 BE
            if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0x00 && bytes[3] == 0x00) return Encoding.UTF32; // UTF-32 LE
            return null;
        }

        private Point GetViewportCenterCanvasPoint()
        {
            double zoom = _zoomPanService.Zoom <= 0 ? 1.0 : _zoomPanService.Zoom;
            double vx = Viewport.ActualWidth / 2.0;
            double vy = Viewport.ActualHeight / 2.0;

            double x = (vx - _zoomPanService.PanX) / zoom;
            double y = (vy - _zoomPanService.PanY) / zoom;
            return new Point(x, y);
        }
    }
}

