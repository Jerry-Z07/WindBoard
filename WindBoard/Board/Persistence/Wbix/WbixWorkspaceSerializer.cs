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
    /// <summary>
    /// WBIX（WindBoard Interchange）工作区序列化实现。
    /// 
    /// 文件结构（Zip）：
    /// - manifest.json
    /// - pages/page-000.json
    /// - pages/page-001.json
    /// - assets/（资源目录，可为空；v2 导出会尝试生成 assets/cover.png 封面图）
    /// </summary>
    internal sealed class WbixWorkspaceSerializer : IBoardWorkspaceSerializer
    {
        internal const string FormatName = "wbix";
        internal const int CurrentVersion = 2;

        private const string ManifestEntryName = "manifest.json";
        private const string PagesFolder = "pages";
        private const string AssetsFolder = "assets";

        // 导入属于外部输入：限制单资源大小，避免压缩包内超大条目导致 OOM。
        private const long MaxResourceBytes = 32L * 1024 * 1024;
        private const long MaxTotalExtractedBytes = 256L * 1024 * 1024;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            PropertyNameCaseInsensitive = true,
            // System.Numerics.Vector2/Vector4 是 public field（非 property），需要显式开启字段序列化。
            IncludeFields = true,
        };

        public async Task SaveAsync(BoardWorkspaceSnapshot snapshot, Stream output, CancellationToken cancellationToken = default)
        {
            await SaveAsync(snapshot, output, resourceFiles: null, cancellationToken).ConfigureAwait(false);
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

            IReadOnlyList<WbixResourceEntry> resources = Array.Empty<WbixResourceEntry>();
            if (resourceFiles is not null && resourceFiles.Count > 0)
            {
                var list = new List<WbixResourceEntry>(resourceFiles.Count);

                foreach (WbixResourceFile file in resourceFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (string.IsNullOrWhiteSpace(file.Id))
                    {
                        throw new ArgumentException(L10n.Get("Wbix_ResourceIdEmpty_Message"), nameof(resourceFiles));
                    }

                    if (string.IsNullOrWhiteSpace(file.Type))
                    {
                        throw new ArgumentException(L10n.Get("Wbix_ResourceTypeEmpty_Message"), nameof(resourceFiles));
                    }

                    if (string.IsNullOrWhiteSpace(file.Path))
                    {
                        throw new ArgumentException(L10n.Get("Wbix_ResourcePathEmpty_Message"), nameof(resourceFiles));
                    }

                    if (file.Bytes is null || file.Bytes.Length == 0)
                    {
                        throw new ArgumentException(L10n.Format("Wbix_ResourceBytesEmpty_Fmt", file.Path), nameof(resourceFiles));
                    }

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

                resources = list;
            }

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

        public async Task<BoardWorkspaceSnapshot> LoadAsync(Stream input, CancellationToken cancellationToken = default)
        {
            if (input is null)
            {
                throw new ArgumentNullException(nameof(input));
            }

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

            if (manifest.Version <= 0 || manifest.Version > CurrentVersion)
            {
                throw new InvalidDataException(L10n.Format("Wbix_VersionNotSupported_Fmt", manifest.Version));
            }

            Dictionary<string, WbixResourceEntry> resourcesById = BuildResourceIndex(manifest.Resources);
            string? extractFolder = null;
            long extractedBytes = 0;

            // 按 manifest.Index 排序，保证页序稳定。
            List<WbixManifestPage> sortedPages = manifest.Pages
                .OrderBy(p => p.Index)
                .ToList();

            var pages = new List<BoardPageSnapshot>(sortedPages.Count);
            foreach (WbixManifestPage pageEntry in sortedPages)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string pagePath = NormalizeZipPath(pageEntry.Path);
                if (!IsSafeZipPath(pagePath, requiredPrefix: $"{PagesFolder}/"))
                {
                    throw new InvalidDataException(L10n.Format("Wbix_MissingPageFile_Fmt", pageEntry.Path));
                }

                ZipArchiveEntry? pageZipEntry = archive.GetEntry(pagePath);
                if (pageZipEntry is null)
                {
                    throw new InvalidDataException(L10n.Format("Wbix_MissingPageFile_Fmt", pageEntry.Path));
                }

                WbixPagePayload payload;
                await using (Stream pageStream = pageZipEntry.Open())
                {
                    payload = await JsonSerializer.DeserializeAsync<WbixPagePayload>(pageStream, JsonOptions, cancellationToken).ConfigureAwait(false)
                        ?? throw new InvalidDataException(L10n.Format("Wbix_PageParseFailed_Fmt", pagePath));
                }

                IReadOnlyList<StrokeSnapshot> strokes = payload.Strokes ?? Array.Empty<StrokeSnapshot>();
                (IReadOnlyList<BoardElementSnapshot> below, IReadOnlyList<BoardElementSnapshot> above) = ParseWbixElements(
                    archive,
                    payload.Elements,
                    resourcesById,
                    () => EnsureExtractFolder(ref extractFolder),
                    ref extractedBytes);

                pages.Add(new BoardPageSnapshot(payload.Id, strokes, below, above));
            }

            int currentIndex = Math.Clamp(manifest.CurrentIndex, 0, Math.Max(0, pages.Count - 1));
            return new BoardWorkspaceSnapshot(
                pages,
                currentIndex,
                ViewportCameraWorld: manifest.ViewportCameraWorld,
                ViewportZoom: manifest.ViewportZoom,
                ViewportSizeDip: manifest.ViewportSizeDip);
        }

        private static Dictionary<string, WbixResourceEntry> BuildResourceIndex(IReadOnlyList<WbixResourceEntry>? resources)
        {
            var map = new Dictionary<string, WbixResourceEntry>(StringComparer.OrdinalIgnoreCase);
            if (resources is null || resources.Count == 0)
            {
                return map;
            }

            for (int i = 0; i < resources.Count; i++)
            {
                WbixResourceEntry r = resources[i];
                if (string.IsNullOrWhiteSpace(r.Id) || string.IsNullOrWhiteSpace(r.Path))
                {
                    continue;
                }

                // 重复 id 时保留第一个，避免外部输入构造歧义。
                map.TryAdd(r.Id, r);
            }

            return map;
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

        private static IReadOnlyList<WbixPageElement> CreateWbixPageElements(BoardPageSnapshot page, IReadOnlyDictionary<Guid, string> embeddedImageResourceIdByElementId)
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

            static void AppendElements(
                List<WbixPageElement> output,
                IReadOnlyList<BoardElementSnapshot> inputs,
                string layer,
                IReadOnlyDictionary<Guid, string> embeddedImageResourceIdByElementId)
            {
                for (int i = 0; i < inputs.Count; i++)
                {
                    BoardElementSnapshot e = inputs[i];
                    int order = e.Order;
                    if (order < 0)
                    {
                        order = i;
                    }

                    WbixPageElement? element = e switch
                    {
                        BoardTextElementSnapshot text => CreateTextElement(text, layer, order),
                        BoardLinkElementSnapshot link => CreateLinkElement(link, layer, order),
                        BoardMediaElementSnapshot media => CreateMediaElement(media, layer, order, embeddedImageResourceIdByElementId),
                        BoardFileElementSnapshot file => CreateFileElement(file, layer, order),
                        _ => null,
                    };

                    if (element is not null)
                    {
                        output.Add(element);
                    }
                }
            }

            static WbixPageElement CreateTextElement(BoardTextElementSnapshot text, string layer, int order)
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

            static WbixPageElement CreateLinkElement(BoardLinkElementSnapshot link, string layer, int order)
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

            static WbixPageElement CreateMediaElement(BoardMediaElementSnapshot media, string layer, int order, IReadOnlyDictionary<Guid, string> embeddedImageResourceIdByElementId)
            {
                string? resourceId = null;
                bool isEmbeddedImage = media.Kind == BoardMediaKind.Image
                    && embeddedImageResourceIdByElementId.TryGetValue(media.Id, out resourceId)
                    && !string.IsNullOrWhiteSpace(resourceId);

                // 内嵌图片属于“可移植资源”：不落盘本地绝对路径，避免路径泄漏与跨机不可用。
                string? sourcePath = isEmbeddedImage ? null : media.SourcePath;

                JsonElement data = JsonSerializer.SerializeToElement(new
                {
                    media.Id,
                    Layer = layer,
                    media.PositionWorld,
                    media.SizeWorld,
                    Order = order,
                    Kind = ToWbixMediaKind(media.Kind),
                    media.DisplayName,
                    SourcePath = sourcePath,
                    ResourceId = isEmbeddedImage ? resourceId : null,
                }, JsonOptions);

                return new WbixPageElement("media", data);
            }

            static WbixPageElement CreateFileElement(BoardFileElementSnapshot file, string layer, int order)
            {
                JsonElement data = JsonSerializer.SerializeToElement(new
                {
                    file.Id,
                    Layer = layer,
                    file.PositionWorld,
                    file.SizeWorld,
                    Order = order,
                    file.DisplayName,
                    file.SourcePath,
                }, JsonOptions);

                return new WbixPageElement("file", data);
            }
        }

        private static (IReadOnlyList<BoardElementSnapshot> below, IReadOnlyList<BoardElementSnapshot> above) ParseWbixElements(
            ZipArchive archive,
            IReadOnlyList<WbixPageElement>? elements,
            IReadOnlyDictionary<string, WbixResourceEntry> resourcesById,
            Func<string> getOrCreateExtractFolder,
            ref long extractedBytes)
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
                    if (TryParseElement(archive, e, resourcesById, getOrCreateExtractFolder, ref extractedBytes, fallbackOrder: i, out BoardElementSnapshot? snapshot, out bool aboveInk))
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

        private static bool TryParseElement(
            ZipArchive archive,
            WbixPageElement element,
            IReadOnlyDictionary<string, WbixResourceEntry> resourcesById,
            Func<string> getOrCreateExtractFolder,
            ref long extractedBytes,
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
                {
                    string text = TryGetString(data, "text") ?? string.Empty;
                    snapshot = new BoardTextElementSnapshot(id, pos, size, order, text);
                    return true;
                }

                case "link":
                {
                    string url = TryGetString(data, "url") ?? string.Empty;
                    string? title = TryGetString(data, "title");
                    snapshot = new BoardLinkElementSnapshot(id, pos, size, order, url, title);
                    return true;
                }

                case "media":
                {
                    string kindText = TryGetString(data, "kind") ?? "video";
                    BoardMediaKind kind = ParseMediaKind(kindText);

                    string displayName = TryGetString(data, "displayName") ?? string.Empty;
                    string? sourcePath = TryGetString(data, "sourcePath");

                    string? resourceId = TryGetString(data, "resourceId");
                    if (!string.IsNullOrWhiteSpace(resourceId)
                        && resourcesById.TryGetValue(resourceId, out WbixResourceEntry? resource)
                        && resource is not null
                        && TryExtractResourceToTempFile(archive, resource, getOrCreateExtractFolder, ref extractedBytes, out string? extractedPath))
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

                    snapshot = new BoardMediaElementSnapshot(id, pos, size, order, kind, sourcePath, displayName);
                    return true;
                }

                case "file":
                {
                    string displayName = TryGetString(data, "displayName") ?? string.Empty;
                    string sourcePath = TryGetString(data, "sourcePath") ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(displayName))
                    {
                        displayName = string.IsNullOrWhiteSpace(sourcePath) ? "文件" : (Path.GetFileName(sourcePath) ?? string.Empty);
                    }

                    snapshot = new BoardFileElementSnapshot(id, pos, size, order, sourcePath, displayName);
                    return true;
                }

                default:
                    // 未知类型：忽略，保持前向兼容。
                    return false;
            }
        }

        private static string EnsureExtractFolder(ref string? extractFolder)
        {
            if (!string.IsNullOrWhiteSpace(extractFolder))
            {
                return extractFolder!;
            }

            string folder = Path.Combine(Path.GetTempPath(), "WindBoard_Import_WBIX_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(folder);
            extractFolder = folder;
            return folder;
        }

        private static bool TryExtractResourceToTempFile(
            ZipArchive archive,
            WbixResourceEntry resource,
            Func<string> getOrCreateExtractFolder,
            ref long extractedBytes,
            out string? extractedPath)
        {
            extractedPath = null;

            if (string.IsNullOrWhiteSpace(resource.Path))
            {
                return false;
            }

            string zipPath = NormalizeZipPath(resource.Path);
            if (!IsSafeZipPath(zipPath, requiredPrefix: $"{AssetsFolder}/"))
            {
                AppLog.Warn("WBIX", $"资源路径不安全，已忽略：path='{resource.Path}'");
                return false;
            }

            ZipArchiveEntry? entry = archive.GetEntry(zipPath);
            if (entry is null)
            {
                return false;
            }

            if (entry.Length <= 0 || entry.Length > MaxResourceBytes)
            {
                AppLog.Warn("WBIX", $"资源大小超限，已忽略：path='{zipPath}', bytes={entry.Length}");
                return false;
            }

            if (extractedBytes + entry.Length > MaxTotalExtractedBytes)
            {
                AppLog.Warn("WBIX", $"资源总大小超限，已忽略：path='{zipPath}', bytes={entry.Length}, total={extractedBytes}");
                return false;
            }

            string fileName = Path.GetFileName(zipPath) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return false;
            }

            string folder = getOrCreateExtractFolder();
            string fullPath = Path.Combine(folder, fileName);

            try
            {
                using Stream src = entry.Open();
                using var dst = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.Read);
                src.CopyTo(dst);

                extractedBytes += entry.Length;
                extractedPath = fullPath;
                return true;
            }
            catch (Exception ex)
            {
                AppLog.Warn("WBIX", $"提取资源失败：path='{zipPath}'", ex);
                return false;
            }
        }

        private static string NormalizeZipPath(string path)
        {
            return (path ?? string.Empty).Replace('\\', '/').TrimStart('/');
        }

        private static bool IsSafeZipPath(string path, string requiredPrefix)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(requiredPrefix)
                && !path.StartsWith(requiredPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (path.Contains("..", StringComparison.Ordinal))
            {
                return false;
            }

            // Zip entry 以相对路径为主，避免绝对路径。
            if (path.StartsWith("/", StringComparison.Ordinal) || path.StartsWith("\\", StringComparison.Ordinal))
            {
                return false;
            }

            return true;
        }

        private static Guid? TryGetGuid(JsonElement obj, string propertyName)
        {
            if (obj.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!obj.TryGetProperty(propertyName, out JsonElement v))
            {
                return null;
            }

            if (v.ValueKind == JsonValueKind.String && Guid.TryParse(v.GetString(), out Guid gid))
            {
                return gid;
            }

            return null;
        }

        private static int? TryGetInt32(JsonElement obj, string propertyName)
        {
            if (obj.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!obj.TryGetProperty(propertyName, out JsonElement v))
            {
                return null;
            }

            if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int i))
            {
                return i;
            }

            return null;
        }

        private static string? TryGetString(JsonElement obj, string propertyName)
        {
            if (obj.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!obj.TryGetProperty(propertyName, out JsonElement v))
            {
                return null;
            }

            return v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        }

        private static Vector2? TryGetVector2(JsonElement obj, string propertyName)
        {
            if (obj.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!obj.TryGetProperty(propertyName, out JsonElement v) || v.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            float? x = TryGetSingle(v, "x");
            float? y = TryGetSingle(v, "y");

            if (x is null || y is null)
            {
                return null;
            }

            return new Vector2(x.Value, y.Value);
        }

        private static float? TryGetSingle(JsonElement obj, string propertyName)
        {
            if (obj.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!obj.TryGetProperty(propertyName, out JsonElement v))
            {
                return null;
            }

            if (v.ValueKind == JsonValueKind.Number)
            {
                if (v.TryGetSingle(out float f))
                {
                    return f;
                }

                if (v.TryGetDouble(out double d))
                {
                    return (float)d;
                }
            }

            return null;
        }

        private static BoardMediaKind ParseMediaKind(string kind)
        {
            switch ((kind ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "image":
                    return BoardMediaKind.Image;
                case "audio":
                    return BoardMediaKind.Audio;
                case "video":
                default:
                    return BoardMediaKind.Video;
            }
        }

        private static string ToWbixMediaKind(BoardMediaKind kind)
        {
            return kind switch
            {
                BoardMediaKind.Image => "image",
                BoardMediaKind.Audio => "audio",
                BoardMediaKind.Video => "video",
                _ => "video",
            };
        }
    }
}
