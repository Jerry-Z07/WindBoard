using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using WindBoard.Board.Elements;
using WindBoard.Board.Persistence;
using WindBoard.Board.Persistence.Wbix;
using WindBoard.Features.Export.Models;
using WindBoard.Localization;
using WindBoard.Logging;
using Windows.UI;

namespace WindBoard.Features.Export.Services
{
    /// <summary>
    /// 导出服务（PNG / PDF / WBIX）。
    /// 
    /// 说明：
    /// - 导出通常是耗时操作，这里统一在后台线程执行（Task.Run）；
    /// - 输入建议使用 <see cref="BoardWorkspaceSnapshot"/>，避免导出过程中 UI 继续编辑导致数据竞争。
    /// </summary>
    internal sealed class BoardExportService : IBoardExportService
    {
        private readonly WbixWorkspaceSerializer _wbixSerializer = new();

        private const long MaxEmbeddedImageBytes = 32L * 1024 * 1024;
        private const long MaxTotalEmbeddedImageBytes = 256L * 1024 * 1024;

        public Task ExportWbixAsync(BoardWorkspaceSnapshot snapshot, string filePath, CancellationToken cancellationToken = default)
        {
            if (snapshot is null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException(L10n.Get("Export_PathEmpty_Message"), nameof(filePath));
            }

            return Task.Run(async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                // 资源列表（封面 + 页面元素资源等）：用于后续导入/文件列表中快速识别内容与增强可移植性。
                // 说明：
                // - 封面图属于“可选资源”，即使生成失败也不应阻断 WBIX 导出；
                // - 图片资源属于“可选增强”：尽量内嵌到 assets/，提高跨机可用性。
                List<WbixResourceFile> resources = new();
                if (TryCreateWbixCoverResource(snapshot, cancellationToken, out WbixResourceFile? cover)
                    && cover is not null)
                {
                    resources.Add(cover);
                }

                TryAddWbixEmbeddedImageResources(snapshot, resources, cancellationToken);

                await using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
                await _wbixSerializer.SaveAsync(snapshot, stream, resources, cancellationToken).ConfigureAwait(false);
            }, cancellationToken);
        }

        public Task ExportPngAsync(BoardWorkspaceSnapshot snapshot, int pageIndex, string filePath, BoardRasterExportOptions options, CancellationToken cancellationToken = default)
        {
            if (snapshot is null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if ((uint)pageIndex >= (uint)snapshot.Pages.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(pageIndex));
            }

            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException(L10n.Get("Export_PathEmpty_Message"), nameof(filePath));
            }

            BoardPageSnapshot page = snapshot.Pages[pageIndex];

            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                using var raster = new BoardRasterExporter();
                raster.ExportPng(page, filePath, options, cancellationToken);
            }, cancellationToken);
        }

        public Task ExportPngPagesToFolderAsync(BoardWorkspaceSnapshot snapshot, IReadOnlyList<int> pageIndices, string folderPath, string datePrefix, BoardRasterExportOptions options, CancellationToken cancellationToken = default)
        {
            if (snapshot is null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            if (pageIndices is null)
            {
                throw new ArgumentNullException(nameof(pageIndices));
            }

            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (string.IsNullOrWhiteSpace(folderPath))
            {
                throw new ArgumentException(L10n.Get("Export_FolderEmpty_Message"), nameof(folderPath));
            }

            if (string.IsNullOrWhiteSpace(datePrefix))
            {
                throw new ArgumentException(L10n.Get("Export_FilePrefixEmpty_Message"), nameof(datePrefix));
            }

            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                Directory.CreateDirectory(folderPath);

                using var raster = new BoardRasterExporter();

                foreach (int index in pageIndices)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if ((uint)index >= (uint)snapshot.Pages.Count)
                    {
                        continue;
                    }

                    // 文件名使用 1 基页码，便于用户在资源管理器中理解与排序。
                    // 规则：年-月-日-页码（例如：2026-02-05-001.png）
                    string fileName = $"{datePrefix}-{index + 1:000}.png";
                    string path = Path.Combine(folderPath, fileName);
                    raster.ExportPng(snapshot.Pages[index], path, options, cancellationToken);
                }
            }, cancellationToken);
        }

        public Task ExportPdfAsync(BoardWorkspaceSnapshot snapshot, IReadOnlyList<int> pageIndices, string filePath, BoardPdfExportOptions options, CancellationToken cancellationToken = default)
        {
            if (snapshot is null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            if (pageIndices is null)
            {
                throw new ArgumentNullException(nameof(pageIndices));
            }

            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException(L10n.Get("Export_PathEmpty_Message"), nameof(filePath));
            }

            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                var pages = new List<BoardPageSnapshot>(pageIndices.Count);
                foreach (int index in pageIndices)
                {
                    if ((uint)index >= (uint)snapshot.Pages.Count)
                    {
                        continue;
                    }

                    pages.Add(snapshot.Pages[index]);
                }

                if (pages.Count == 0)
                {
                    throw new InvalidOperationException(L10n.Get("Export_NoPagesSelected_Message"));
                }

                BoardPdfExporter.Export(pages, filePath, options, cancellationToken);
            }, cancellationToken);
        }

        private static bool TryCreateWbixCoverResource(BoardWorkspaceSnapshot snapshot, CancellationToken cancellationToken, out WbixResourceFile? coverResource)
        {
            coverResource = null;

            try
            {
                if (snapshot.Pages.Count == 0)
                {
                    return false;
                }

                BoardPageSnapshot firstPage = snapshot.Pages[0];

                // 封面固定为 512×512，便于后续在 UI 中统一展示。
                const int coverSize = 512;

                var rasterOptions = new BoardRasterExportOptions(
                    Dpi: 96,
                    PaddingDip: 24.0f,
                    BackgroundColor: Color.FromArgb(255, 255, 255, 255),
                    FallbackViewportSizeDip: new Vector2(coverSize, coverSize),
                    FixedFrame: new BoardRasterFixedFrame(coverSize, coverSize));

                string tempPath = Path.Combine(Path.GetTempPath(), $"windboard-cover-{Guid.NewGuid():N}.png");
                try
                {
                    using var raster = new BoardRasterExporter();
                    raster.ExportPng(firstPage, tempPath, rasterOptions, cancellationToken);

                    byte[] bytes = File.ReadAllBytes(tempPath);

                    var meta = new Dictionary<string, string>
                    {
                        ["role"] = "cover",
                        ["pageIndex"] = "0",
                        ["pixelWidth"] = coverSize.ToString(),
                        ["pixelHeight"] = coverSize.ToString(),
                    };

                    coverResource = new WbixResourceFile(
                        Id: "cover",
                        Type: "image",
                        Path: "assets/cover.png",
                        ContentType: "image/png",
                        Meta: meta,
                        Bytes: bytes);

                    return true;
                }
                finally
                {
                    try
                    {
                        if (File.Exists(tempPath))
                        {
                            File.Delete(tempPath);
                        }
                    }
                    catch
                    {
                        // 忽略临时文件清理失败：不应影响导出主流程。
                    }
                }
            }
            catch
            {
                // 封面资源导出失败时，不应阻断 WBIX 导出。
                coverResource = null;
                return false;
            }
        }

        private static void TryAddWbixEmbeddedImageResources(BoardWorkspaceSnapshot snapshot, List<WbixResourceFile> resources, CancellationToken cancellationToken)
        {
            if (snapshot is null || snapshot.Pages.Count == 0)
            {
                return;
            }

            long totalBytes = 0;

            for (int pageIndex = 0; pageIndex < snapshot.Pages.Count; pageIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                BoardPageSnapshot page = snapshot.Pages[pageIndex];

                AppendImageResourcesFromElements(page.ElementsBelowInk, page.Id, pageIndex);
                AppendImageResourcesFromElements(page.ElementsAboveInk, page.Id, pageIndex);
            }

            void AppendImageResourcesFromElements(IReadOnlyList<BoardElementSnapshot>? elements, Guid pageId, int pageIndex)
            {
                if (elements is null || elements.Count == 0)
                {
                    return;
                }

                for (int i = 0; i < elements.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (elements[i] is not BoardMediaElementSnapshot { Kind: BoardMediaKind.Image } img)
                    {
                        continue;
                    }

                    string sourcePath = img.SourcePath ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                    {
                        continue;
                    }

                    try
                    {
                        var info = new FileInfo(sourcePath);
                        if (info.Length <= 0 || info.Length > MaxEmbeddedImageBytes)
                        {
                            AppLog.Warn("WBIX", $"跳过内嵌图片（大小超限）：path='{sourcePath}', bytes={info.Length}");
                            continue;
                        }

                        if (totalBytes + info.Length > MaxTotalEmbeddedImageBytes)
                        {
                            AppLog.Warn("WBIX", $"跳过内嵌图片（总大小超限）：path='{sourcePath}', bytes={info.Length}, total={totalBytes}");
                            continue;
                        }

                        string ext = NormalizeImageExtension(Path.GetExtension(sourcePath));
                        if (string.IsNullOrWhiteSpace(ext))
                        {
                            continue;
                        }

                        string fileName = $"{img.Id:N}{ext}";
                        string zipPath = $"assets/elements/{fileName}";
                        string contentType = ResolveImageContentType(ext);

                        byte[] bytes = File.ReadAllBytes(sourcePath);
                        if (bytes.Length == 0)
                        {
                            continue;
                        }

                        totalBytes += bytes.Length;

                        var meta = new Dictionary<string, string>
                        {
                            ["role"] = "elementImage",
                            ["elementId"] = img.Id.ToString("D"),
                            ["pageId"] = pageId.ToString("D"),
                            ["pageIndex"] = pageIndex.ToString(),
                        };

                        resources.Add(new WbixResourceFile(
                            Id: $"img-{img.Id:D}",
                            Type: "image",
                            Path: zipPath,
                            ContentType: contentType,
                            Meta: meta,
                            Bytes: bytes));
                    }
                    catch (Exception ex)
                    {
                        AppLog.Warn("WBIX", $"内嵌图片失败：path='{sourcePath}'", ex);
                    }
                }
            }
        }

        private static string NormalizeImageExtension(string? ext)
        {
            string e = (ext ?? string.Empty).Trim().ToLowerInvariant();
            if (e.Length == 0)
            {
                return string.Empty;
            }

            // 仅允许常见图片后缀，避免把任意二进制伪装为“图片”写入。
            return e switch
            {
                ".png" => ".png",
                ".jpg" => ".jpg",
                ".jpeg" => ".jpeg",
                ".webp" => ".webp",
                ".bmp" => ".bmp",
                ".gif" => ".gif",
                _ => string.Empty,
            };
        }

        private static string ResolveImageContentType(string ext)
        {
            return ext switch
            {
                ".png" => "image/png",
                ".jpg" => "image/jpeg",
                ".jpeg" => "image/jpeg",
                ".webp" => "image/webp",
                ".bmp" => "image/bmp",
                ".gif" => "image/gif",
                _ => "application/octet-stream",
            };
        }
    }
}
