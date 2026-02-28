using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WindBoard.Board.Elements;
using WindBoard.Board.Persistence;
using WindBoard.Localization;

namespace WindBoard.Board.Persistence.Wbix
{
    internal sealed partial class WbixWorkspaceSerializer
    {
        public Task SaveAsync(BoardWorkspaceSnapshot snapshot, Stream output, CancellationToken cancellationToken = default)
        {
            return SaveAsync(snapshot, output, resourceFiles: null, cancellationToken);
        }

        public async Task SaveAsync(BoardWorkspaceSnapshot snapshot, Stream output, IReadOnlyList<WbixResourceFile>? resourceFiles, CancellationToken cancellationToken = default)
        {
            if (snapshot is null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            if (output is null)
            {
                throw new ArgumentNullException(nameof(output));
            }

            cancellationToken.ThrowIfCancellationRequested();

            using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);

            Dictionary<Guid, string> embeddedImageResourceIdByElementId = BuildEmbeddedImageResourceIdMap(resourceFiles);

            List<WbixManifestPage> pages = await WritePagesAsync(archive, snapshot, embeddedImageResourceIdByElementId, cancellationToken).ConfigureAwait(false);
            IReadOnlyList<WbixResourceEntry> resources = await WriteResourcesAsync(archive, resourceFiles, cancellationToken).ConfigureAwait(false);
            await WriteManifestAsync(archive, snapshot, pages, resources, cancellationToken).ConfigureAwait(false);
        }

        private static async Task<List<WbixManifestPage>> WritePagesAsync(
            ZipArchive archive,
            BoardWorkspaceSnapshot snapshot,
            IReadOnlyDictionary<Guid, string> embeddedImageResourceIdByElementId,
            CancellationToken cancellationToken)
        {
            var pages = new List<WbixManifestPage>(snapshot.Pages.Count);

            for (int i = 0; i < snapshot.Pages.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                BoardPageSnapshot page = snapshot.Pages[i];
                string pageEntryName = $"{PagesFolder}/page-{i:000}.json";
                pages.Add(new WbixManifestPage(page.Id, i, pageEntryName));

                var payload = new WbixPagePayload(
                    page.Id,
                    page.Strokes,
                    Elements: CreateWbixPageElements(page, embeddedImageResourceIdByElementId));

                ZipArchiveEntry entry = archive.CreateEntry(pageEntryName, CompressionLevel.Optimal);
                await using Stream pageStream = entry.Open();
                await JsonSerializer.SerializeAsync(pageStream, payload, JsonOptions, cancellationToken).ConfigureAwait(false);
            }

            return pages;
        }

        private static async Task<IReadOnlyList<WbixResourceEntry>> WriteResourcesAsync(
            ZipArchive archive,
            IReadOnlyList<WbixResourceFile>? resourceFiles,
            CancellationToken cancellationToken)
        {
            if (resourceFiles is null || resourceFiles.Count == 0)
            {
                return Array.Empty<WbixResourceEntry>();
            }

            var list = new List<WbixResourceEntry>(resourceFiles.Count);

            for (int i = 0; i < resourceFiles.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                WbixResourceFile file = resourceFiles[i];
                ValidateResourceFileOrThrow(file, nameof(resourceFiles));

                // 写入二进制资源（例如：assets/cover.png）。
                ZipArchiveEntry entry = archive.CreateEntry(file.Path, CompressionLevel.Optimal);
                await using (Stream resourceStream = entry.Open())
                {
                    await resourceStream.WriteAsync(file.Bytes, 0, file.Bytes.Length, cancellationToken).ConfigureAwait(false);
                }

                list.Add(new WbixResourceEntry(
                    Id: file.Id,
                    Type: file.Type,
                    Path: file.Path,
                    ContentType: file.ContentType,
                    Meta: file.Meta));
            }

            return list;
        }

        private static void ValidateResourceFileOrThrow(WbixResourceFile file, string paramName)
        {
            if (string.IsNullOrWhiteSpace(file.Id))
            {
                throw new ArgumentException(L10n.Get("Wbix_ResourceIdEmpty_Message"), paramName);
            }

            if (string.IsNullOrWhiteSpace(file.Type))
            {
                throw new ArgumentException(L10n.Get("Wbix_ResourceTypeEmpty_Message"), paramName);
            }

            if (string.IsNullOrWhiteSpace(file.Path))
            {
                throw new ArgumentException(L10n.Get("Wbix_ResourcePathEmpty_Message"), paramName);
            }

            if (file.Bytes is null || file.Bytes.Length == 0)
            {
                throw new ArgumentException(L10n.Format("Wbix_ResourceBytesEmpty_Fmt", file.Path), paramName);
            }
        }

        private static async Task WriteManifestAsync(
            ZipArchive archive,
            BoardWorkspaceSnapshot snapshot,
            IReadOnlyList<WbixManifestPage> pages,
            IReadOnlyList<WbixResourceEntry> resources,
            CancellationToken cancellationToken)
        {
            var manifest = new WbixManifest(
                Format: FormatName,
                Version: CurrentVersion,
                CreatedUtc: DateTimeOffset.UtcNow,
                CurrentIndex: snapshot.CurrentIndex,
                Pages: pages,
                Resources: resources,
                ViewportCameraWorld: snapshot.ViewportCameraWorld,
                ViewportZoom: snapshot.ViewportZoom,
                ViewportSizeDip: snapshot.ViewportSizeDip);

            ZipArchiveEntry manifestEntry = archive.CreateEntry(ManifestEntryName, CompressionLevel.Optimal);
            await using Stream manifestStream = manifestEntry.Open();
            await JsonSerializer.SerializeAsync(manifestStream, manifest, JsonOptions, cancellationToken).ConfigureAwait(false);
        }

        private static Dictionary<Guid, string> BuildEmbeddedImageResourceIdMap(IReadOnlyList<WbixResourceFile>? resourceFiles)
        {
            var map = new Dictionary<Guid, string>();
            if (resourceFiles is null || resourceFiles.Count == 0)
            {
                return map;
            }

            for (int i = 0; i < resourceFiles.Count; i++)
            {
                WbixResourceFile file = resourceFiles[i];
                if (file.Meta is null)
                {
                    continue;
                }

                // 约定：BoardExportService 会把内嵌图片资源写入 meta：
                // - role=elementImage
                // - elementId={Guid}
                if (!file.Meta.TryGetValue("role", out string? role) || !string.Equals(role, "elementImage", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!file.Meta.TryGetValue("elementId", out string? elementId) || string.IsNullOrWhiteSpace(elementId))
                {
                    continue;
                }

                if (!Guid.TryParse(elementId, out Guid gid))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(file.Id))
                {
                    continue;
                }

                map[gid] = file.Id;
            }

            return map;
        }

        private static IReadOnlyList<WbixPageElement> CreateWbixPageElements(
            BoardPageSnapshot page,
            IReadOnlyDictionary<Guid, string> embeddedImageResourceIdByElementId)
        {
            IReadOnlyList<BoardElementSnapshot> below = page.ElementsBelowInk ?? Array.Empty<BoardElementSnapshot>();
            IReadOnlyList<BoardElementSnapshot> above = page.ElementsAboveInk ?? Array.Empty<BoardElementSnapshot>();

            if (below.Count == 0 && above.Count == 0)
            {
                return Array.Empty<WbixPageElement>();
            }

            var list = new List<WbixPageElement>(below.Count + above.Count);

            AppendElements(list, below, layer: "belowInk", embeddedImageResourceIdByElementId);
            AppendElements(list, above, layer: "aboveInk", embeddedImageResourceIdByElementId);

            return list;
        }

        private static void AppendElements(
            List<WbixPageElement> output,
            IReadOnlyList<BoardElementSnapshot> inputs,
            string layer,
            IReadOnlyDictionary<Guid, string> embeddedImageResourceIdByElementId)
        {
            for (int i = 0; i < inputs.Count; i++)
            {
                BoardElementSnapshot e = inputs[i];
                int order = e.Order < 0 ? i : e.Order;

                if (TryCreateWbixElement(e, layer, order, embeddedImageResourceIdByElementId, out WbixPageElement? element)
                    && element is not null)
                {
                    output.Add(element);
                }
            }
        }

        private static bool TryCreateWbixElement(
            BoardElementSnapshot snapshot,
            string layer,
            int order,
            IReadOnlyDictionary<Guid, string> embeddedImageResourceIdByElementId,
            out WbixPageElement? element)
        {
            element = null;

            switch (snapshot)
            {
                case BoardTextElementSnapshot text:
                    element = CreateTextElement(text, layer, order);
                    return true;

                case BoardLinkElementSnapshot link:
                    element = CreateLinkElement(link, layer, order);
                    return true;

                case BoardMediaElementSnapshot media:
                    element = CreateMediaElement(media, layer, order, embeddedImageResourceIdByElementId);
                    return true;

                case BoardFileElementSnapshot file:
                    element = CreateFileElement(file, layer, order);
                    return true;

                default:
                    return false;
            }
        }

        private static WbixPageElement CreateTextElement(BoardTextElementSnapshot text, string layer, int order)
        {
            JsonElement data = JsonSerializer.SerializeToElement(new
            {
                text.Id,
                Layer = layer,
                text.PositionWorld,
                text.SizeWorld,
                Order = order,
                text.Text,
            }, JsonOptions);

            return new WbixPageElement("text", data);
        }

        private static WbixPageElement CreateLinkElement(BoardLinkElementSnapshot link, string layer, int order)
        {
            JsonElement data = JsonSerializer.SerializeToElement(new
            {
                link.Id,
                Layer = layer,
                link.PositionWorld,
                link.SizeWorld,
                Order = order,
                link.Url,
                link.Title,
            }, JsonOptions);

            return new WbixPageElement("link", data);
        }

        private static WbixPageElement CreateMediaElement(
            BoardMediaElementSnapshot media,
            string layer,
            int order,
            IReadOnlyDictionary<Guid, string> embeddedImageResourceIdByElementId)
        {
            string? resourceId = null;
            bool isEmbeddedImage = media.Kind == BoardMediaKind.Image
                && embeddedImageResourceIdByElementId.TryGetValue(media.Id, out resourceId)
                && !string.IsNullOrWhiteSpace(resourceId);

            // 内嵌图片属于“可移植资源”：不落盘本地绝对路径，避免路径泄漏与跨机不可用。
            string? sourcePath = isEmbeddedImage ? (string?)null : media.SourcePath;

            JsonElement data = JsonSerializer.SerializeToElement(new
            {
                media.Id,
                Layer = layer,
                media.PositionWorld,
                media.SizeWorld,
                Order = order,
                Kind = ToWbixMediaKind(media.Kind),
                SourcePath = sourcePath,
                media.DisplayName,
                ResourceId = resourceId,
            }, JsonOptions);

            return new WbixPageElement("media", data);
        }

        private static WbixPageElement CreateFileElement(BoardFileElementSnapshot file, string layer, int order)
        {
            JsonElement data = JsonSerializer.SerializeToElement(new
            {
                file.Id,
                Layer = layer,
                file.PositionWorld,
                file.SizeWorld,
                Order = order,
                file.SourcePath,
                file.DisplayName,
            }, JsonOptions);

            return new WbixPageElement("file", data);
        }
    }
}
