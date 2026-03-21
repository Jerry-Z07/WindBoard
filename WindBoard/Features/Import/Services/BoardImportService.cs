using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Windows.Storage;
using WindBoard.Board.Commands;
using WindBoard.Board.Elements;
using WindBoard.Board.Editing;
using WindBoard.Features.Import.Models;
using WindBoard.Logging;

namespace WindBoard.Features.Import.Services
{
    /// <summary>
    /// 白板导入服务：把用户选择的文件/文本/链接转换为板上元素，并以旧版“网格铺开”的方式放置。
    /// </summary>
    internal static class BoardImportService
    {
        private static readonly Vector2 MediaCardSizeDip = new(360.0f, 160.0f);
        private static readonly Vector2 FileCardSizeDip = new(360.0f, 160.0f);
        private static readonly Vector2 LinkCardSizeDip = new(360.0f, 160.0f);
        private static readonly Vector2 TextCardSizeDip = new(360.0f, 200.0f);
        private static readonly Vector2 LargeTextCardSizeDip = new(420.0f, 260.0f);
        private static readonly Vector2 ImageFallbackCardSizeDip = new(360.0f, 220.0f);

        internal static async Task<IReadOnlyList<BoardElement>> ImportElementsAsync(
            BoardWorkspace workspace,
            Vector2 cameraWorld,
            float zoom,
            ImportElementsRequest request)
        {
            if (workspace is null)
            {
                throw new ArgumentNullException(nameof(workspace));
            }

            if (request is null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            IReadOnlyList<string> links = ImportUrlNormalizer.ParseAndNormalizeLinkLines(request.LinkLines);

            BoardSession session = workspace.CurrentPage.Session;
            var context = new ImportExecutionContext(session, cameraWorld, zoom);

            await ImportImagesAsync(context, request.ImageFiles);
            await ImportMediasAsync(context, request.MediaFiles);
            ImportOtherFiles(context, request.OtherFiles);
            await ImportTextFilesAsync(context, request.TextFiles);
            ImportTextContent(context, request.TextContent);
            ImportLinks(context, links);
            return context.Created;
        }

        /// <summary>
        /// 导入执行上下文：用于在拆分后的方法之间共享状态，避免参数爆炸。
        /// </summary>
        private sealed class ImportExecutionContext
        {
            internal ImportExecutionContext(BoardSession session, Vector2 cameraWorld, float zoom)
            {
                Session = session ?? throw new ArgumentNullException(nameof(session));
                CameraWorld = cameraWorld;
                Zoom = zoom;
            }

            internal BoardSession Session { get; }

            internal Vector2 CameraWorld { get; }

            internal float Zoom { get; }

            internal int Index { get; set; }

            internal List<BoardElement> Created { get; } = new();
        }

        private static async Task ImportImagesAsync(ImportExecutionContext context, IReadOnlyList<StorageFile>? images)
        {
            if (images is not { Count: > 0 })
            {
                return;
            }

            for (int i = 0; i < images.Count; i++)
            {
                StorageFile? file = images[i];
                if (file is null)
                {
                    continue;
                }

                BoardMediaElement? element = await TryCreateImageElementAsync(file);
                if (element is null)
                {
                    continue;
                }

                Vector2 sizeDip = ComputeImageCardSizeDipOrFallback(element);
                PlaceExecuteAndTrack(context, element, sizeDip);
            }
        }

        private static async Task ImportMediasAsync(ImportExecutionContext context, IReadOnlyList<StorageFile>? medias)
        {
            if (medias is not { Count: > 0 })
            {
                return;
            }

            for (int i = 0; i < medias.Count; i++)
            {
                StorageFile? file = medias[i];
                if (file is null)
                {
                    continue;
                }

                ImportFileContentKind kind = ImportFileTypeResolver.Resolve(file.Name);

                switch (kind)
                {
                    case ImportFileContentKind.Image:
                    {
                        // 防御：媒体列表不应包含图片；如果出现则按图片导入兜底。
                        BoardMediaElement? element = await TryCreateImageElementAsync(file);
                        if (element is null)
                        {
                            continue;
                        }

                        Vector2 sizeDip = ComputeImageCardSizeDipOrFallback(element);
                        PlaceExecuteAndTrack(context, element, sizeDip);
                        break;
                    }
                    case ImportFileContentKind.Audio:
                    {
                        var element = new BoardMediaElement
                        {
                            Kind = BoardMediaKind.Audio,
                            SourcePath = file.Path,
                            DisplayName = file.Name,
                        };

                        PlaceExecuteAndTrack(context, element, MediaCardSizeDip);
                        break;
                    }
                    default:
                    {
                        // 兜底：未识别一律按视频占位导入，避免用户“导入后什么都没发生”。
                        var element = new BoardMediaElement
                        {
                            Kind = BoardMediaKind.Video,
                            SourcePath = file.Path,
                            DisplayName = file.Name,
                        };

                        PlaceExecuteAndTrack(context, element, MediaCardSizeDip);
                        break;
                    }
                }
            }
        }

        private static void ImportOtherFiles(ImportExecutionContext context, IReadOnlyList<StorageFile>? others)
        {
            if (others is not { Count: > 0 })
            {
                return;
            }

            for (int i = 0; i < others.Count; i++)
            {
                StorageFile? file = others[i];
                if (file is null)
                {
                    continue;
                }

                var element = new BoardFileElement
                {
                    SourcePath = file.Path,
                    DisplayName = file.Name,
                };

                PlaceExecuteAndTrack(context, element, FileCardSizeDip);
            }
        }

        private static async Task ImportTextFilesAsync(ImportExecutionContext context, IReadOnlyList<StorageFile>? texts)
        {
            if (texts is not { Count: > 0 })
            {
                return;
            }

            for (int i = 0; i < texts.Count; i++)
            {
                StorageFile? file = texts[i];
                if (file is null)
                {
                    continue;
                }

                ImportFileContentKind kind = ImportFileTypeResolver.Resolve(file.Name);

                if (kind == ImportFileContentKind.UrlShortcut)
                {
                    await ImportInternetShortcutFileAsync(context, file);
                    continue;
                }

                await ImportTextFileAsync(context, file);
            }
        }

        private static async Task ImportInternetShortcutFileAsync(ImportExecutionContext context, StorageFile file)
        {
            string content;
            try
            {
                content = await TextImportReader.ReadTextFileWithLimitAsync(file.Path, maxChars: 16_384);
            }
            catch (Exception ex)
            {
                AppLog.Warn("Import", $"读取 .url 文件失败：'{file.Path}'", ex);
                ImportFileFallback(context, file);
                return;
            }

            if (TryParseInternetShortcutUrl(content, out string url))
            {
                var link = new BoardLinkElement { Url = url };
                PlaceExecuteAndTrack(context, link, LinkCardSizeDip);
                return;
            }

            // 解析失败：按文本导入兜底，避免用户“什么都没发生”的体验。
            var text = new BoardTextElement { Text = content };
            PlaceExecuteAndTrack(context, text, TextCardSizeDip);
        }

        private static async Task ImportTextFileAsync(ImportExecutionContext context, StorageFile file)
        {
            string content;
            try
            {
                content = await TextImportReader.ReadTextFileWithLimitAsync(file.Path, maxChars: 64_000);
            }
            catch (Exception ex)
            {
                AppLog.Warn("Import", $"读取文本文件失败：'{file.Path}'", ex);
                ImportFileFallback(context, file);
                return;
            }

            var element = new BoardTextElement { Text = content };
            PlaceExecuteAndTrack(context, element, LargeTextCardSizeDip);
        }

        private static void ImportTextContent(ImportExecutionContext context, string? textContent)
        {
            if (string.IsNullOrWhiteSpace(textContent))
            {
                return;
            }

            var text = new BoardTextElement { Text = textContent.TrimEnd() };
            PlaceExecuteAndTrack(context, text, TextCardSizeDip);
        }

        private static void ImportLinks(ImportExecutionContext context, IReadOnlyList<string> links)
        {
            for (int i = 0; i < links.Count; i++)
            {
                string url = links[i];
                var link = new BoardLinkElement { Url = url };
                PlaceExecuteAndTrack(context, link, LinkCardSizeDip);
            }
        }

        private static void ImportFileFallback(ImportExecutionContext context, StorageFile file)
        {
            var element = new BoardFileElement
            {
                SourcePath = file.Path,
                DisplayName = file.Name,
            };

            PlaceExecuteAndTrack(context, element, FileCardSizeDip);
        }

        private static void PlaceExecuteAndTrack(ImportExecutionContext context, BoardElement element, Vector2 sizeDip)
        {
            ImportPlacementPlanner.PlaceElementAtViewportCenterGrid(element, sizeDip, context.Index++, context.CameraWorld, context.Zoom);
            context.Session.Execute(new AddElementCommand(element, aboveInk: false));
            context.Created.Add(element);
        }

        private static async Task<BoardMediaElement?> TryCreateImageElementAsync(StorageFile file)
        {
            (byte[] pixels, int w, int h)? decoded = null;
            try
            {
                decoded = await ImageImportDecoder.TryDecodeToBgra8PremulAsync(file, maxPixelEdge: 2048);
            }
            catch (Exception ex)
            {
                // 说明：像素解码属于“可选增强”，失败不应阻断导入。
                AppLog.Warn("Import", $"图片解码失败：'{file.Path}'", ex);
            }

            return new BoardMediaElement
            {
                Kind = BoardMediaKind.Image,
                SourcePath = file.Path,
                DisplayName = file.Name,
                PixelWidth = decoded?.w ?? 0,
                PixelHeight = decoded?.h ?? 0,
                Bgra8PremulPixels = decoded?.pixels,
            };
        }

        private static Vector2 ComputeImageCardSizeDipOrFallback(BoardMediaElement element)
        {
            if (element.PixelWidth > 0 && element.PixelHeight > 0)
            {
                return ComputeImageCardSizeDip(element.PixelWidth, element.PixelHeight, maxWidthDip: 520.0f, maxHeightDip: 360.0f);
            }

            return ImageFallbackCardSizeDip;
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
    }
}
