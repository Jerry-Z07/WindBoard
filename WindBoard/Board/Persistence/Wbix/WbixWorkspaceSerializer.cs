using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WindBoard.Board.Persistence;
using WindBoard.Localization;

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
                    Elements: Array.Empty<WbixPageElement>());

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
                Resources: resources);

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

            // 按 manifest.Index 排序，保证页序稳定。
            List<WbixManifestPage> sortedPages = manifest.Pages
                .OrderBy(p => p.Index)
                .ToList();

            var pages = new List<BoardPageSnapshot>(sortedPages.Count);
            foreach (WbixManifestPage pageEntry in sortedPages)
            {
                cancellationToken.ThrowIfCancellationRequested();

                ZipArchiveEntry? pageZipEntry = archive.GetEntry(pageEntry.Path);
                if (pageZipEntry is null)
                {
                    throw new InvalidDataException(L10n.Format("Wbix_MissingPageFile_Fmt", pageEntry.Path));
                }

                WbixPagePayload payload;
                await using (Stream pageStream = pageZipEntry.Open())
                {
                    payload = await JsonSerializer.DeserializeAsync<WbixPagePayload>(pageStream, JsonOptions, cancellationToken).ConfigureAwait(false)
                        ?? throw new InvalidDataException(L10n.Format("Wbix_PageParseFailed_Fmt", pageEntry.Path));
                }

                pages.Add(new BoardPageSnapshot(payload.Id, payload.Strokes));
            }

            int currentIndex = Math.Clamp(manifest.CurrentIndex, 0, Math.Max(0, pages.Count - 1));
            return new BoardWorkspaceSnapshot(pages, currentIndex);
        }
    }
}
