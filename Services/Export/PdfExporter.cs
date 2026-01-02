using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using WindBoard.Models.Export;

namespace WindBoard.Services.Export
{
    /// <summary>
    /// PDF 导出器
    /// </summary>
    public sealed class PdfExporter
    {
        private readonly ExportRenderer _renderer = new();

        /// <summary>
        /// 导出为 PDF 文件
        /// </summary>
        public async Task ExportAsync(
            IList<BoardPage> pages,
            string filePath,
            PdfExportOptions options,
            Color backgroundColor,
            IProgress<ExportProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (pages == null || pages.Count == 0)
                throw new ArgumentException("没有可导出的页面", nameof(pages));

            await Task.Run(() =>
            {
                using var document = new PdfDocument();
                document.Info.Title = Path.GetFileNameWithoutExtension(filePath);
                document.Info.Creator = AppDisplayNames.GetAppNameFromSettings();

                for (int i = 0; i < pages.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    progress?.Report(new ExportProgress
                    {
                        CurrentPage = i + 1,
                        TotalPages = pages.Count,
                        StatusMessage = $"正在导出第 {i + 1} 页..."
                    });

                    var boardPage = pages[i];

                    // 确定页面方向
                    var orientation = DetermineOrientation(boardPage, options.Orientation);
                    double pageWidth = options.PageWidth;
                    double pageHeight = options.PageHeight;

                    if (orientation == PdfOrientation.Portrait)
                    {
                        // 交换宽高
                        (pageWidth, pageHeight) = (pageHeight, pageWidth);
                    }

                    // 创建 PDF 页面
                    var pdfPage = document.AddPage();
                    pdfPage.Width = XUnit.FromPoint(pageWidth);
                    pdfPage.Height = XUnit.FromPoint(pageHeight);

                    using var gfx = XGraphics.FromPdfPage(pdfPage);

                    // 计算内容区域（减去边距）
                    double contentWidth = pageWidth - options.Margin * 2;
                    double contentHeight = pageHeight - options.Margin * 2;

                    // 渲染页面为位图
                    int renderDpi = options.Dpi;
                    int renderWidth = (int)(contentWidth * renderDpi / 72.0);
                    int renderHeight = (int)(contentHeight * renderDpi / 72.0);

                    var imageOptions = new ImageExportOptions
                    {
                        Width = renderWidth,
                        Height = renderHeight,
                        KeepAspectRatio = options.ScaleMode == PdfScaleMode.FitPage,
                        BackgroundColor = backgroundColor,
                        Format = ExportFormat.Png
                    };

                    var bitmap = _renderer.RenderPage(boardPage, imageOptions);

                    // 将位图转换为 PNG 并嵌入 PDF
                    byte[] pngData = _renderer.RenderPageToPng(boardPage, imageOptions);

                    using var ms = new MemoryStream(pngData);
                    var xImage = XImage.FromStream(() => new MemoryStream(pngData));

                    // 计算图片在 PDF 中的位置和大小
                    double imgWidth = contentWidth;
                    double imgHeight = contentHeight;

                    if (options.ScaleMode == PdfScaleMode.FitPage)
                    {
                        // 保持比例居中
                        double scaleX = contentWidth / xImage.PixelWidth;
                        double scaleY = contentHeight / xImage.PixelHeight;
                        double scale = Math.Min(scaleX, scaleY);

                        imgWidth = xImage.PixelWidth * scale;
                        imgHeight = xImage.PixelHeight * scale;
                    }

                    double x = options.Margin + (contentWidth - imgWidth) / 2;
                    double y = options.Margin + (contentHeight - imgHeight) / 2;

                    gfx.DrawImage(xImage, x, y, imgWidth, imgHeight);
                }

                // 保存
                document.Save(filePath);

            }, cancellationToken);

            progress?.Report(new ExportProgress
            {
                CurrentPage = pages.Count,
                TotalPages = pages.Count,
                StatusMessage = "导出完成"
            });
        }

        /// <summary>
        /// 预估 PDF 文件大小（字节）
        /// </summary>
        public long EstimateFileSize(IList<BoardPage> pages, PdfExportOptions options)
        {
            long totalSize = 0;

            foreach (var page in pages)
            {
                // 每页渲染为图片后的大小
                int renderWidth = (int)((options.PageWidth - options.Margin * 2) * options.Dpi / 72.0);
                int renderHeight = (int)((options.PageHeight - options.Margin * 2) * options.Dpi / 72.0);
                long pixels = (long)renderWidth * renderHeight;

                // PNG 在 PDF 中的大小（压缩后）
                totalSize += (long)(pixels * 0.3);

                // PDF 元数据开销
                totalSize += 1000;
            }

            // PDF 文件头和结构开销
            totalSize += 5000;

            return totalSize;
        }

        private PdfOrientation DetermineOrientation(BoardPage page, PdfOrientation requested)
        {
            if (requested != PdfOrientation.Auto)
                return requested;

            // 根据内容边界自动判断
            Rect bounds = _renderer.CalculateContentBounds(page);
            if (bounds.IsEmpty)
                return PdfOrientation.Landscape;

            return bounds.Width >= bounds.Height ? PdfOrientation.Landscape : PdfOrientation.Portrait;
        }
    }
}
