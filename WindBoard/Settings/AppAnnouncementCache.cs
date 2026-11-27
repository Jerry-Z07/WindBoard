using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using WindBoard.Logging;
using WindBoard.Persistence;

namespace WindBoard.Settings
{
    /// <summary>
    /// 公告的 last-known-good 缓存（原始 JSON 内容 + 条件请求头 + 拉取时间）。
    /// </summary>
    internal sealed class AppAnnouncementCache
    {
        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; set; }

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;

        [JsonPropertyName("etag")]
        public string? ETag { get; set; }

        [JsonPropertyName("lastModified")]
        public string? LastModified { get; set; }

        [JsonPropertyName("fetchedAtUtc")]
        public DateTimeOffset FetchedAtUtc { get; set; }
    }

    /// <summary>
    /// 公告缓存文件的读写。失败只记录日志并返回 false，不影响界面使用。
    /// </summary>
    internal sealed class AppAnnouncementCacheStore
    {
        private const string LogCategory = "Announcements";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            WriteIndented = true,
        };

        internal AppAnnouncementCacheStore(string filePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

            FilePath = filePath;
        }

        internal static AppAnnouncementCacheStore CreateDefault()
        {
            return new AppAnnouncementCacheStore(Path.Combine(AppDataPaths.RootDirectory, "announcements.cache.json"));
        }

        internal string FilePath { get; }

        internal bool TryLoad(out AppAnnouncementCache? cache)
        {
            cache = null;

            try
            {
                if (!File.Exists(FilePath))
                {
                    return false;
                }

                string json = File.ReadAllText(FilePath);
                AppAnnouncementCache? loaded = JsonSerializer.Deserialize<AppAnnouncementCache>(json, JsonOptions);
                if (loaded is null || string.IsNullOrWhiteSpace(loaded.Content))
                {
                    AppLog.Warn(LogCategory, $"公告缓存内容为空，已忽略：path='{FilePath}'");
                    return false;
                }

                cache = loaded;
                return true;
            }
            catch (Exception ex)
            {
                AppLog.Warn(LogCategory, $"读取公告缓存失败，已忽略：path='{FilePath}'", ex);
                return false;
            }
        }

        internal bool TrySave(AppAnnouncementCache cache)
        {
            ArgumentNullException.ThrowIfNull(cache);

            try
            {
                string? directory = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string json = JsonSerializer.Serialize(cache, JsonOptions);
                string tempPath = FilePath + ".tmp";
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, FilePath, overwrite: true);
                return true;
            }
            catch (Exception ex)
            {
                AppLog.Warn(LogCategory, $"写入公告缓存失败：path='{FilePath}'", ex);
                return false;
            }
        }
    }
}
