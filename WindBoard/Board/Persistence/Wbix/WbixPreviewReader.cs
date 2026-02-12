using System;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Threading.Tasks;
using WindBoard.Logging;
using WindBoard.Localization;

namespace WindBoard.Board.Persistence.Wbix
{
    /// <summary>
    /// WBIX 预读：用于导入确认对话框等“快速预览”场景。
    /// 
    /// 注意：
    /// - 该预读只读取 manifest 与可选封面（assets/cover.png），不会加载所有页面数据；
    /// - 预读失败应返回 null，并记录日志，不应阻断 UI 线程。
    /// </summary>
    internal static class WbixPreviewReader
    {
        private const string ManifestEntryName = "manifest.json";
        private const string DefaultCoverEntryName = "assets/cover.png";
        private const int MaxCoverBytes = 8 * 1024 * 1024;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
        };

        internal sealed record WbixPreview(
            WbixManifest Manifest,
            byte[]? CoverPngBytes);

        public static async Task<WbixPreview?> TryReadAsync(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return null;
            }

            try
            {
                await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);

                ZipArchiveEntry? manifestEntry = archive.GetEntry(ManifestEntryName);
                if (manifestEntry is null)
                {
                    return null;
                }

                WbixManifest manifest;
                await using (Stream ms = manifestEntry.Open())
                {
                    manifest = await JsonSerializer.DeserializeAsync<WbixManifest>(ms, JsonOptions).ConfigureAwait(false)
                        ?? throw new InvalidDataException(L10n.Get("Wbix_ManifestParseFailed_Message"));
                }

                string? coverPath = TryResolveCoverPathFromManifest(manifest);
                byte[]? coverBytes = null;

                // 封面属于可选资源：缺失时允许降级。
                if (!string.IsNullOrWhiteSpace(coverPath))
                {
                    coverBytes = TryReadZipEntryBytes(archive, coverPath!, maxBytes: MaxCoverBytes);
                }

                coverBytes ??= TryReadZipEntryBytes(archive, DefaultCoverEntryName, maxBytes: MaxCoverBytes);

                return new WbixPreview(manifest, coverBytes);
            }
            catch (Exception ex)
            {
                AppLog.Warn("WBIX", $"预读失败：'{filePath}'", ex);
                return null;
            }
        }

        private static string? TryResolveCoverPathFromManifest(WbixManifest manifest)
        {
            if (manifest.Resources is null)
            {
                return null;
            }

            foreach (WbixResourceEntry r in manifest.Resources)
            {
                if (string.Equals(r.Id, "cover", StringComparison.OrdinalIgnoreCase))
                {
                    return r.Path;
                }

                if (r.Meta is not null
                    && r.Meta.TryGetValue("role", out string? role)
                    && string.Equals(role, "cover", StringComparison.OrdinalIgnoreCase))
                {
                    return r.Path;
                }
            }

            return null;
        }

        private static byte[]? TryReadZipEntryBytes(ZipArchive archive, string entryName, int maxBytes)
        {
            try
            {
                ZipArchiveEntry? entry = archive.GetEntry(entryName);
                if (entry is null)
                {
                    return null;
                }

                if (entry.Length <= 0 || entry.Length > maxBytes)
                {
                    return null;
                }

                using Stream s = entry.Open();
                using var ms = new MemoryStream((int)Math.Min(int.MaxValue, entry.Length));
                s.CopyTo(ms);
                return ms.ToArray();
            }
            catch
            {
                return null;
            }
        }
    }
}
