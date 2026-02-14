using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using Windows.Storage;
using WindBoard.Board.Commands;
using WindBoard.Board.Elements;
using WindBoard.Board.Editing;
using WindBoard.Logging;

namespace WindBoard.Importing
{
    /// <summary>
    /// 白板导入服务：把用户选择的文件/文本/链接转换为板上元素，并以旧版“网格铺开”的方式放置。
    /// </summary>
    internal static class BoardImportService
    {
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

            BoardSession session = workspace.CurrentPage.Session;

            var created = new List<BoardElement>();

            int totalImages = request.ImageFiles?.Count ?? 0;
            int totalMedia = request.MediaFiles?.Count ?? 0;
            int totalTextFiles = request.TextFiles?.Count ?? 0;
            int hasText = string.IsNullOrWhiteSpace(request.TextContent) ? 0 : 1;
            int totalLinks = ImportUrlNormalizer.ParseAndNormalizeLinkLines(request.LinkLines).Count;

            AppLog.Info("Import", $"开始导入：images={totalImages}, media={totalMedia}, textFiles={totalTextFiles}, text={hasText}, links={totalLinks}");

            int index = 0;

            if (request.ImageFiles is { Count: > 0 } images)
            {
                for (int i = 0; i < images.Count; i++)
                {
                    StorageFile file = images[i];
                    BoardElement? element = await CreateImageElementAsync(file);
                    if (element is null)
                    {
                        continue;
                    }

                    Vector2 sizeDip = element is BoardMediaElement { PixelWidth: > 0, PixelHeight: > 0 } img
                        ? ComputeImageCardSizeDip(img.PixelWidth, img.PixelHeight, maxWidthDip: 520.0f, maxHeightDip: 360.0f)
                        : new Vector2(360.0f, 220.0f);

                    ImportPlacementPlanner.PlaceElementAtViewportCenterGrid(element, sizeDip, index++, cameraWorld, zoom);
                    session.Execute(new AddElementCommand(element, aboveInk: false));
                    created.Add(element);
                }
            }

            if (request.MediaFiles is { Count: > 0 } medias)
            {
                for (int i = 0; i < medias.Count; i++)
                {
                    StorageFile file = medias[i];
                    BoardMediaKind kind = ResolveMediaKind(file.Name);
                    if (kind == BoardMediaKind.Image)
                    {
                        // 防御：媒体列表不应包含图片；如果出现则按图片导入兜底。
                        BoardElement? element = await CreateImageElementAsync(file);
                        if (element is null)
                        {
                            continue;
                        }

                        Vector2 sizeDip = element is BoardMediaElement { PixelWidth: > 0, PixelHeight: > 0 } img
                            ? ComputeImageCardSizeDip(img.PixelWidth, img.PixelHeight, maxWidthDip: 520.0f, maxHeightDip: 360.0f)
                            : new Vector2(360.0f, 220.0f);

                        ImportPlacementPlanner.PlaceElementAtViewportCenterGrid(element, sizeDip, index++, cameraWorld, zoom);
                        session.Execute(new AddElementCommand(element, aboveInk: false));
                        created.Add(element);
                        continue;
                    }

                    var element2 = new BoardMediaElement
                    {
                        Kind = kind,
                        SourcePath = file.Path,
                        DisplayName = file.Name,
                    };

                    ImportPlacementPlanner.PlaceElementAtViewportCenterGrid(element2, sizeDip: new Vector2(360.0f, 160.0f), index++, cameraWorld, zoom);
                    session.Execute(new AddElementCommand(element2, aboveInk: false));
                    created.Add(element2);
                }
            }

            if (request.TextFiles is { Count: > 0 } texts)
            {
                for (int i = 0; i < texts.Count; i++)
                {
                    StorageFile file = texts[i];
                    string ext = Path.GetExtension(file.Name).ToLowerInvariant();

                    if (string.Equals(ext, ".url", StringComparison.OrdinalIgnoreCase))
                    {
                        string content = await TextImportReader.ReadTextFileWithLimitAsync(file.Path, maxChars: 16_384);
                        if (TryParseInternetShortcutUrl(content, out string url))
                        {
                            var link = new BoardLinkElement { Url = url };
                            ImportPlacementPlanner.PlaceElementAtViewportCenterGrid(link, sizeDip: new Vector2(360.0f, 160.0f), index++, cameraWorld, zoom);
                            session.Execute(new AddElementCommand(link, aboveInk: false));
                            created.Add(link);
                            continue;
                        }

                        // 解析失败：按文本导入兜底，避免用户“什么都没发生”的体验。
                        var text = new BoardTextElement { Text = content };
                        ImportPlacementPlanner.PlaceElementAtViewportCenterGrid(text, sizeDip: new Vector2(360.0f, 200.0f), index++, cameraWorld, zoom);
                        session.Execute(new AddElementCommand(text, aboveInk: false));
                        created.Add(text);
                        continue;
                    }

                    string content2 = await TextImportReader.ReadTextFileWithLimitAsync(file.Path, maxChars: 64_000);
                    var element3 = new BoardTextElement { Text = content2 };
                    ImportPlacementPlanner.PlaceElementAtViewportCenterGrid(element3, sizeDip: new Vector2(420.0f, 260.0f), index++, cameraWorld, zoom);
                    session.Execute(new AddElementCommand(element3, aboveInk: false));
                    created.Add(element3);
                }
            }

            if (!string.IsNullOrWhiteSpace(request.TextContent))
            {
                var text = new BoardTextElement { Text = request.TextContent.TrimEnd() };
                ImportPlacementPlanner.PlaceElementAtViewportCenterGrid(text, sizeDip: new Vector2(360.0f, 200.0f), index++, cameraWorld, zoom);
                session.Execute(new AddElementCommand(text, aboveInk: false));
                created.Add(text);
            }

            IReadOnlyList<string> links = ImportUrlNormalizer.ParseAndNormalizeLinkLines(request.LinkLines);
            for (int i = 0; i < links.Count; i++)
            {
                string url = links[i];
                var link = new BoardLinkElement { Url = url };
                ImportPlacementPlanner.PlaceElementAtViewportCenterGrid(link, sizeDip: new Vector2(360.0f, 160.0f), index++, cameraWorld, zoom);
                session.Execute(new AddElementCommand(link, aboveInk: false));
                created.Add(link);
            }

            AppLog.Info("Import", $"导入完成：created={created.Count}");
            return created;
        }

        private static async Task<BoardElement?> CreateImageElementAsync(StorageFile file)
        {
            if (file is null)
            {
                return null;
            }

            (byte[] pixels, int w, int h)? decoded = await ImageImportDecoder.TryDecodeToBgra8PremulAsync(file, maxPixelEdge: 2048);
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

        private static Vector2 ComputeImageCardSizeDip(int pixelWidth, int pixelHeight, float maxWidthDip, float maxHeightDip)
        {
            float iw = Math.Max(1.0f, pixelWidth);
            float ih = Math.Max(1.0f, pixelHeight);

            float scale = Math.Min(maxWidthDip / iw, maxHeightDip / ih);
            float w = Math.Clamp(iw * scale, 160.0f, maxWidthDip);
            float h = Math.Clamp(ih * scale, 120.0f, maxHeightDip);
            return new Vector2(w, h);
        }

        private static BoardMediaKind ResolveMediaKind(string fileName)
        {
            string ext = Path.GetExtension(fileName).ToLowerInvariant();

            if (IsAudioExtension(ext))
            {
                return BoardMediaKind.Audio;
            }

            if (IsVideoExtension(ext))
            {
                return BoardMediaKind.Video;
            }

            // 未识别时按视频占位导入，避免用户“导入后什么都没发生”。
            return BoardMediaKind.Video;
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

        private static bool IsAudioExtension(string ext)
        {
            return ext is ".mp3" or ".wav" or ".m4a" or ".aac" or ".flac" or ".ogg";
        }

        private static bool IsVideoExtension(string ext)
        {
            return ext is ".mp4" or ".mov" or ".mkv" or ".wmv" or ".avi" or ".webm";
        }
    }
}
