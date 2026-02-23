using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using WindBoard.Board.Persistence;
using WindBoard.Board.Persistence.Wbix;
using WindBoard.Features.Export.Models;
using WindBoard.Localization;
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

                // 生成封面图（首页）：用于后续导入/文件列表中快速识别内容。
                // 说明：
                // - 封面图属于“可选资源”，即使生成失败也不应阻断 WBIX 导出；
                // - 当前仅导出笔迹，后续可扩展导出图片/视频等资源，并落盘到 assets/ 与 manifest.Resources。
                List<WbixResourceFile> resources = new();
                if (TryCreateWbixCoverResource(snapshot, cancellationToken, out WbixResourceFile? cover)
                    && cover is not null)
                {
                    resources.Add(cover);
                }

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
    }
}
