using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using WindBoard.Models.Export;

namespace WindBoard.Services.Export
{
    /// <summary>
    /// 导出服务：协调各种导出操作
    /// </summary>
    public sealed class ExportService
    {
        private readonly ExportRenderer _renderer = new();
        private readonly PdfExporter _pdfExporter = new();
        private readonly WbiExporter _wbiExporter = new();
        private readonly WbiImporter _wbiImporter = new();

        /// <summary>
        /// 导出为图片
        /// </summary>
        /// <param name="pages">要导出的页面列表</param>
        /// <param name="folderPath">输出文件夹路径（多页时）或文件路径（单页时）</param>
        /// <param name="options">导出选项</param>
        /// <param name="fileNamePrefix">文件名前缀（多页时使用）</param>
        /// <param name="progress">进度报告</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>导出的文件路径列表</returns>
        public async Task<List<string>> ExportToImageAsync(
            IList<BoardPage> pages,
            string folderPath,
            ImageExportOptions options,
            string fileNamePrefix = "page",
            IProgress<ExportProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (pages == null || pages.Count == 0)
                throw new ArgumentException("没有可导出的页面", nameof(pages));

            var exportedFiles = new List<string>();
            string extension = options.Format == ExportFormat.Png ? ".png" : ".jpg";

            // 确保目录存在
            if (!Directory.Exists(folderPath))
                Directory.CreateDirectory(folderPath);

            await Task.Run(() =>
            {
                for (int i = 0; i < pages.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    progress?.Report(new ExportProgress
                    {
                        CurrentPage = i + 1,
                        TotalPages = pages.Count,
                        StatusMessage = $"正在导出第 {i + 1} 页..."
                    });

                    var page = pages[i];
                    string fileName = pages.Count == 1
                        ? $"{fileNamePrefix}{extension}"
                        : $"{fileNamePrefix}_{i + 1:D3}{extension}";
                    string filePath = Path.Combine(folderPath, fileName);

                    byte[] data = options.Format == ExportFormat.Png
                        ? _renderer.RenderPageToPng(page, options)
                        : _renderer.RenderPageToJpeg(page, options);

                    File.WriteAllBytes(filePath, data);
                    exportedFiles.Add(filePath);
                }
            }, cancellationToken);

            progress?.Report(new ExportProgress
            {
                CurrentPage = pages.Count,
                TotalPages = pages.Count,
                StatusMessage = "导出完成"
            });

            return exportedFiles;
        }

        /// <summary>
        /// 导出为 PDF
        /// </summary>
        public Task ExportToPdfAsync(
            IList<BoardPage> pages,
            string filePath,
            PdfExportOptions options,
            Color backgroundColor,
            IProgress<ExportProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return _pdfExporter.ExportAsync(pages, filePath, options, backgroundColor, progress, cancellationToken);
        }

        /// <summary>
        /// 导出为 WBI
        /// </summary>
        public Task ExportToWbiAsync(
            IList<BoardPage> pages,
            string filePath,
            WbiExportOptions options,
            IProgress<ExportProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return _wbiExporter.ExportAsync(pages, filePath, options, progress, cancellationToken);
        }

        /// <summary>
        /// 从 WBI 导入
        /// </summary>
        public Task<WbiImportResult> ImportFromWbiAsync(
            string filePath,
            string? assetExtractFolder = null,
            IProgress<ExportProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return _wbiImporter.ImportAsync(filePath, assetExtractFolder, progress, cancellationToken);
        }

        /// <summary>
        /// 获取 WBI 文件信息
        /// </summary>
        public Models.Wbi.WbiManifest? GetWbiManifest(string filePath)
        {
            return _wbiImporter.GetManifest(filePath);
        }

        /// <summary>
        /// 预估导出文件大小
        /// </summary>
        public long EstimateExportSize(IList<BoardPage> pages, ExportFormat format, object options)
        {
            return format switch
            {
                ExportFormat.Png or ExportFormat.Jpg =>
                    pages.Sum(p => _renderer.EstimateFileSize(p, (ImageExportOptions)options)),
                ExportFormat.Pdf =>
                    _pdfExporter.EstimateFileSize(pages, (PdfExportOptions)options),
                ExportFormat.Wbi =>
                    _wbiExporter.EstimateFileSize(pages, (WbiExportOptions)options),
                _ => 0
            };
        }

        /// <summary>
        /// 格式化文件大小显示
        /// </summary>
        public static string FormatFileSize(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB" };
            int suffixIndex = 0;
            double size = bytes;

            while (size >= 1024 && suffixIndex < suffixes.Length - 1)
            {
                size /= 1024;
                suffixIndex++;
            }

            return $"{size:F1} {suffixes[suffixIndex]}";
        }
    }
}
