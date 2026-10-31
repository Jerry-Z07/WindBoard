using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WindBoard.Board.Elements;
using WindBoard.Board.Persistence;
using WindBoard.Localization;
using WindBoard.Logging;

namespace WindBoard.Board.Persistence.Wbix
{
    internal sealed partial class WbixWorkspaceSerializer
    {
        public async Task<BoardWorkspaceSnapshot> LoadAsync(Stream input, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(input);

            cancellationToken.ThrowIfCancellationRequested();

            using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);

            ZipArchiveEntry? manifestEntry = archive.GetEntry(ManifestEntryName);
            if (manifestEntry is null)
            {
                throw new InvalidDataException(L10n.Get("Wbix_MissingManifest_Message"));
            }

            WbixManifest manifest;
            await using (Stream manifestStream = manifestEntry.Open())
            {
                manifest = await JsonSerializer.DeserializeAsync<WbixManifest>(manifestStream, JsonOptions, cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidDataException(L10n.Get("Wbix_ManifestParseFailed_Message"));
            }

            if (!string.Equals(manifest.Format, FormatName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(L10n.Format("Wbix_FormatMismatch_Fmt", manifest.Format));
            }

            // 版本契约：按 manifest.version 分派页读取器。
            // 时序与旧版本门禁一致——format 校验之后、任何页被读取之前，因此「版本非法但 pages 为空」的文件同样被拒绝。
            IWbixPageReader pageReader = WbixPageReaderRegistry.For(manifest.Version);

            Dictionary<string, WbixResourceEntry> resourcesById = BuildResourceIndex(manifest.Resources);
            var loadContext = new WbixLoadContext(archive, resourcesById);

            // 按 manifest.Index 排序，保证页序稳定。
            List<WbixManifestPage> sortedPages = manifest.Pages
                .OrderBy(p => p.Index)
                .ToList();

            var pages = new List<BoardPageSnapshot>(sortedPages.Count);
            foreach (WbixManifestPage pageEntry in sortedPages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                pages.Add(await pageReader.ReadPageAsync(loadContext, pageEntry, cancellationToken).ConfigureAwait(false));
            }

            int currentIndex = Math.Clamp(manifest.CurrentIndex, 0, Math.Max(0, pages.Count - 1));
            return new BoardWorkspaceSnapshot(
                pages,
                currentIndex,
                ViewportCameraWorld: manifest.ViewportCameraWorld,
                ViewportZoom: manifest.ViewportZoom,
                ViewportSizeDip: manifest.ViewportSizeDip);
        }

        /// <summary>
        /// WBIX 导入上下文：承载资源索引、临时目录与总提取大小，避免多参数/多 ref 传播。
        /// </summary>
        private sealed class WbixLoadContext
        {
            private readonly ZipArchive _archive;
            private readonly IReadOnlyDictionary<string, WbixResourceEntry> _resourcesById;
            private readonly Dictionary<InkItemReadMode, JsonSerializerOptions> _pageJsonOptions = new();
            private readonly WbixLegacyFlatItemWarningGate _legacyFlatWarnGate = new();

            private string? _extractFolder;
            private long _extractedBytes;

            public WbixLoadContext(ZipArchive archive, IReadOnlyDictionary<string, WbixResourceEntry> resourcesById)
            {
                _archive = archive ?? throw new ArgumentNullException(nameof(archive));
                _resourcesById = resourcesById ?? throw new ArgumentNullException(nameof(resourcesById));
            }

            /// <summary>
            /// 当前文件对应的 Zip 归档（页读取器按路径取条目）。
            /// </summary>
            public ZipArchive Archive => _archive;

            /// <summary>
            /// 取得指定读模式下用于反序列化页面文件的 JSON 选项（同一上下文内每种模式只创建一次）。
            /// </summary>
            public JsonSerializerOptions GetPageJsonOptions(InkItemReadMode readMode)
            {
                if (_pageJsonOptions.TryGetValue(readMode, out JsonSerializerOptions? options))
                {
                    return options;
                }

                options = CreateJsonOptions(readMode, OnUnexpectedLegacyFlatItem);
                _pageJsonOptions[readMode] = options;
                return options;
            }

            /// <summary>
            /// v3 文件出现扁平笔迹条目的兜底告警：每个文件最多一条，避免超大文件刷日志。
            /// </summary>
            private void OnUnexpectedLegacyFlatItem()
            {
                if (_legacyFlatWarnGate.TryEnter())
                {
                    AppLog.Warn("WBIX", "v3 文件的笔迹条目中出现扁平形态，已按 kind='stroke' 兼容读取。");
                }
            }

            public (IReadOnlyList<BoardElementSnapshot> below, IReadOnlyList<BoardElementSnapshot> above) ParseElements(IReadOnlyList<WbixPageElement>? elements)
            {
                if (elements is null || elements.Count == 0)
                {
                    return (Array.Empty<BoardElementSnapshot>(), Array.Empty<BoardElementSnapshot>());
                }

                var below = new List<(BoardElementSnapshot element, int order, int index)>();
                var above = new List<(BoardElementSnapshot element, int order, int index)>();

                for (int i = 0; i < elements.Count; i++)
                {
                    WbixPageElement e = elements[i];

                    try
                    {
                        if (TryParseElement(e, fallbackOrder: i, out BoardElementSnapshot? snapshot, out bool aboveInk))
                        {
                            if (aboveInk)
                            {
                                above.Add((snapshot!, snapshot!.Order, i));
                            }
                            else
                            {
                                below.Add((snapshot!, snapshot!.Order, i));
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // elements 属于可选扩展位：单个元素解析失败不应阻断整个文件导入。
                        AppLog.Warn("WBIX", $"元素解析失败：type='{e.Type}'", ex);
                    }
                }

                below.Sort(static (a, b) =>
                {
                    int c = a.order.CompareTo(b.order);
                    return c != 0 ? c : a.index.CompareTo(b.index);
                });

                above.Sort(static (a, b) =>
                {
                    int c = a.order.CompareTo(b.order);
                    return c != 0 ? c : a.index.CompareTo(b.index);
                });

                return (
                    below.Select(x => x.element).ToArray(),
                    above.Select(x => x.element).ToArray());
            }

            private bool TryParseElement(
                WbixPageElement element,
                int fallbackOrder,
                out BoardElementSnapshot? snapshot,
                out bool aboveInk)
            {
                snapshot = null;
                aboveInk = false;

                string type = (element.Type ?? string.Empty).Trim();
                if (type.Length == 0)
                {
                    return false;
                }

                JsonElement data = element.Data;

                Guid id = TryGetGuid(data, "id") ?? Guid.NewGuid();
                Vector2 pos = TryGetVector2(data, "positionWorld") ?? Vector2.Zero;
                Vector2 size = TryGetVector2(data, "sizeWorld") ?? new Vector2(320.0f, 180.0f);

                int order = TryGetInt32(data, "order") ?? fallbackOrder;

                string layer = (TryGetString(data, "layer") ?? "belowInk").Trim();
                aboveInk = string.Equals(layer, "aboveInk", StringComparison.OrdinalIgnoreCase);

                switch (type.ToLowerInvariant())
                {
                    case "text":
                        snapshot = ParseTextElement(id, pos, size, order, data);
                        break;

                    case "link":
                        snapshot = ParseLinkElement(id, pos, size, order, data);
                        break;

                    case "media":
                        snapshot = ParseMediaElement(id, pos, size, order, data);
                        break;

                    case "file":
                        snapshot = ParseFileElement(id, pos, size, order, data);
                        break;

                    default:
                        // 未知类型：忽略，保持前向兼容。
                        snapshot = null;
                        break;
                }

                return snapshot is not null;
            }

            private static BoardElementSnapshot ParseTextElement(Guid id, Vector2 pos, Vector2 size, int order, JsonElement data)
            {
                string text = TryGetString(data, "text") ?? string.Empty;
                return new BoardTextElementSnapshot(id, pos, size, order, text);
            }

            private static BoardElementSnapshot ParseLinkElement(Guid id, Vector2 pos, Vector2 size, int order, JsonElement data)
            {
                string url = TryGetString(data, "url") ?? string.Empty;
                string? title = TryGetString(data, "title");
                return new BoardLinkElementSnapshot(id, pos, size, order, url, title);
            }

            private BoardElementSnapshot ParseMediaElement(Guid id, Vector2 pos, Vector2 size, int order, JsonElement data)
            {
                string kindText = TryGetString(data, "kind") ?? "video";
                BoardMediaKind kind = ParseMediaKind(kindText);

                string displayName = TryGetString(data, "displayName") ?? string.Empty;
                string? sourcePath = TryGetString(data, "sourcePath");

                string? resourceId = TryGetString(data, "resourceId");
                if (!string.IsNullOrWhiteSpace(resourceId)
                    && _resourcesById.TryGetValue(resourceId, out WbixResourceEntry? resource)
                    && resource is not null
                    && TryExtractResourceToTempFile(resource, out string? extractedPath))
                {
                    sourcePath = extractedPath;
                    if (string.IsNullOrWhiteSpace(displayName))
                    {
                        displayName = Path.GetFileName(extractedPath) ?? string.Empty;
                    }
                }

                sourcePath ??= string.Empty;
                if (string.IsNullOrWhiteSpace(displayName))
                {
                    displayName = kind == BoardMediaKind.Image ? "图片" : kind == BoardMediaKind.Audio ? "音频" : "视频";
                }

                return new BoardMediaElementSnapshot(id, pos, size, order, kind, sourcePath, displayName);
            }

            private static BoardElementSnapshot ParseFileElement(Guid id, Vector2 pos, Vector2 size, int order, JsonElement data)
            {
                string displayName = TryGetString(data, "displayName") ?? string.Empty;
                string sourcePath = TryGetString(data, "sourcePath") ?? string.Empty;
                if (string.IsNullOrWhiteSpace(displayName))
                {
                    displayName = string.IsNullOrWhiteSpace(sourcePath) ? "文件" : (Path.GetFileName(sourcePath) ?? string.Empty);
                }

                return new BoardFileElementSnapshot(id, pos, size, order, sourcePath, displayName);
            }

            private bool TryExtractResourceToTempFile(WbixResourceEntry resource, out string? extractedPath)
            {
                extractedPath = null;

                if (!TryValidateResourceForExtraction(resource, out string zipPath, out ZipArchiveEntry? entry))
                {
                    return false;
                }

                if (!TryWriteEntryToTempFile(zipPath, entry!, out string? extractedFullPath))
                {
                    return false;
                }

                _extractedBytes += entry!.Length;
                extractedPath = extractedFullPath;
                return true;
            }

            private bool TryValidateResourceForExtraction(WbixResourceEntry resource, out string zipPath, out ZipArchiveEntry? entry)
            {
                zipPath = string.Empty;
                entry = null;

                if (string.IsNullOrWhiteSpace(resource.Path))
                {
                    return false;
                }

                zipPath = NormalizeZipPath(resource.Path);
                if (!IsSafeZipPath(zipPath, requiredPrefix: $"{AssetsFolder}/"))
                {
                    AppLog.Warn("WBIX", $"资源路径不安全，已忽略：path='{resource.Path}'");
                    return false;
                }

                entry = _archive.GetEntry(zipPath);
                if (entry is null)
                {
                    return false;
                }

                long length = entry.Length;
                if (length <= 0 || length > MaxResourceBytes)
                {
                    AppLog.Warn("WBIX", $"资源大小超限，已忽略：path='{zipPath}', bytes={length}");
                    return false;
                }

                bool withinTotal = _extractedBytes + length <= MaxTotalExtractedBytes;
                if (!withinTotal)
                {
                    AppLog.Warn("WBIX", $"资源总大小超限，已忽略：path='{zipPath}', bytes={length}, total={_extractedBytes}");
                    entry = null;
                }

                return withinTotal;
            }

            private bool TryWriteEntryToTempFile(string zipPath, ZipArchiveEntry entry, out string? extractedFullPath)
            {
                extractedFullPath = null;

                string fileName = Path.GetFileName(zipPath) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    return false;
                }

                string folder = EnsureExtractFolder(ref _extractFolder);
                string fullPath = Path.Combine(folder, fileName);

                try
                {
                    using Stream src = entry.Open();
                    using var dst = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.Read);
                    src.CopyTo(dst);

                    extractedFullPath = fullPath;
                    return true;
                }
                catch (Exception ex)
                {
                    AppLog.Warn("WBIX", $"提取资源失败：path='{zipPath}'", ex);
                    return false;
                }
            }
        }
    }
}
