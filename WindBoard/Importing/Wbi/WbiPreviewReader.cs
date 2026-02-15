using System;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WindBoard.Logging;

namespace WindBoard.Importing.Wbi
{
    /// <summary>
    /// WBI 预读：用于导入对话框的“快速预览”。
    /// 
    /// 说明：
    /// - WBI 属于旧格式兼容输入，预读失败应返回 null，并记录日志；
    /// - WBI 没有封面资源，这里仅读取 manifest.json。
    /// </summary>
    internal static class WbiPreviewReader
    {
        private const string ManifestEntryName = "manifest.json";

        /// <summary>当前支持的最高版本（旧版 WBI 的最后一个公开版本）。</summary>
        private static readonly Version MaxSupportedVersion = new(1, 0);

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
        };

        internal sealed record WbiPreview(WbiManifest Manifest);

        public static async Task<WbiPreview?> TryReadAsync(string filePath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return null;
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);

                ZipArchiveEntry? manifestEntry = archive.GetEntry(ManifestEntryName);
                if (manifestEntry is null)
                {
                    return null;
                }

                WbiManifest manifest;
                await using (Stream ms = manifestEntry.Open())
                {
                    manifest = await JsonSerializer.DeserializeAsync<WbiManifest>(ms, JsonOptions, cancellationToken)
                        ?? throw new InvalidDataException("WBI manifest.json 解析失败。");
                }

                if (!IsVersionCompatible(manifest.MinCompatibleVersion))
                {
                    return null;
                }

                return new WbiPreview(manifest);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception ex)
            {
                AppLog.Warn("WBI", $"预读失败：'{filePath}'", ex);
                return null;
            }
        }

        private static bool IsVersionCompatible(string? minVersion)
        {
            if (string.IsNullOrWhiteSpace(minVersion))
            {
                return true;
            }

            if (!Version.TryParse(minVersion, out Version? required))
            {
                return false;
            }

            return required <= MaxSupportedVersion;
        }
    }
}

