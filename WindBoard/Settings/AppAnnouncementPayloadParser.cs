using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.UI.Xaml.Controls;

namespace WindBoard.Settings
{
    /// <summary>
    /// 远端 <c>announcements.json</c> 的解析与校验。
    /// 任何一条公告不满足契约都会拒绝整份内容，避免半份合法内容被展示。
    /// </summary>
    internal static class AppAnnouncementPayloadParser
    {
        internal const int SupportedSchemaVersion = 1;
        internal const int MaxAnnouncementCount = 8;
        internal const int MaxLanguageCount = 8;
        internal const int MaxIdLength = 64;
        internal const int MaxTitleLength = 120;
        internal const int MaxMessageLength = 500;
        internal const int MaxActionTextLength = 32;

        private const string DefaultLanguage = "zh-CN";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        internal static bool TryParse(
            string? json,
            out IReadOnlyList<AppAnnouncement> announcements,
            out string? error)
        {
            announcements = Array.Empty<AppAnnouncement>();
            error = null;

            if (string.IsNullOrWhiteSpace(json))
            {
                error = "公告内容为空";
                return false;
            }

            AnnouncementsPayload? payload;
            try
            {
                payload = JsonSerializer.Deserialize<AnnouncementsPayload>(json, JsonOptions);
            }
            catch (JsonException ex)
            {
                error = $"JSON 解析失败：{ex.Message}";
                return false;
            }

            if (payload is null)
            {
                error = "JSON 根节点为空";
                return false;
            }

            if (payload.SchemaVersion != SupportedSchemaVersion)
            {
                string actual = payload.SchemaVersion?.ToString(CultureInfo.InvariantCulture) ?? "(null)";
                error = $"schemaVersion 不受支持：{actual}";
                return false;
            }

            List<AnnouncementItem?>? items = payload.Announcements;
            if (items is null)
            {
                error = "缺少 announcements 数组";
                return false;
            }

            if (items.Count > MaxAnnouncementCount)
            {
                error = $"公告条数超过上限：count={items.Count}, max={MaxAnnouncementCount}";
                return false;
            }

            List<AppAnnouncement> parsed = new(items.Count);
            HashSet<string> ids = new(StringComparer.Ordinal);
            for (int index = 0; index < items.Count; index++)
            {
                if (!TryParseAnnouncement(items[index], index, out AppAnnouncement? announcement, out error))
                {
                    return false;
                }

                if (!ids.Add(announcement!.Id))
                {
                    error = $"公告 Id 重复：id='{announcement.Id}'";
                    return false;
                }

                parsed.Add(announcement);
            }

            announcements = parsed;
            return true;
        }

        private static bool TryParseAnnouncement(
            AnnouncementItem? item,
            int index,
            out AppAnnouncement? announcement,
            out string? error)
        {
            announcement = null;
            error = null;

            if (item is null)
            {
                error = $"第 {index} 条公告为空";
                return false;
            }

            string id = item.Id ?? string.Empty;
            if (!IsValidId(id))
            {
                error = $"第 {index} 条公告 Id 非法：id='{id}'";
                return false;
            }

            if (!TryParseSeverity(item.Severity, out InfoBarSeverity severity))
            {
                error = $"第 {index} 条公告 severity 未知：severity='{item.Severity}'";
                return false;
            }

            if (!TryParseAction(item.Action, out AppAnnouncementAction action))
            {
                error = $"第 {index} 条公告 action 未知：action='{item.Action}'";
                return false;
            }

            Dictionary<string, AnnouncementText?>? texts = item.Text;
            if (texts is null || texts.Count == 0)
            {
                error = $"第 {index} 条公告缺少 text";
                return false;
            }

            if (texts.Count > MaxLanguageCount)
            {
                error = $"第 {index} 条公告语言数超过上限：count={texts.Count}, max={MaxLanguageCount}";
                return false;
            }

            Dictionary<string, AppAnnouncementText> parsedTexts = new(texts.Count, StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, AnnouncementText?> pair in texts)
            {
                string language = (pair.Key ?? string.Empty).Trim();
                if (language.Length == 0)
                {
                    error = $"第 {index} 条公告存在空语言标签";
                    return false;
                }

                if (pair.Value is null)
                {
                    error = $"第 {index} 条公告语言 '{language}' 的文案为空";
                    return false;
                }

                if (!TryParseText(pair.Value, action, index, language, out AppAnnouncementText? text, out error))
                {
                    return false;
                }

                if (!parsedTexts.TryAdd(language, text!))
                {
                    error = $"第 {index} 条公告语言重复：language='{language}'";
                    return false;
                }
            }

            if (!parsedTexts.ContainsKey(DefaultLanguage))
            {
                error = $"第 {index} 条公告缺少 {DefaultLanguage} 文案";
                return false;
            }

            announcement = new AppAnnouncement(id, severity, action, parsedTexts);
            return true;
        }

        private static bool TryParseText(
            AnnouncementText source,
            AppAnnouncementAction action,
            int index,
            string language,
            out AppAnnouncementText? text,
            out string? error)
        {
            text = null;
            error = null;

            string title = (source.Title ?? string.Empty).Trim();
            if (title.Length == 0 || title.Length > MaxTitleLength)
            {
                error = $"第 {index} 条公告 {language} title 长度非法：length={title.Length}";
                return false;
            }

            string message = (source.Message ?? string.Empty).Trim();
            if (message.Length == 0 || message.Length > MaxMessageLength)
            {
                error = $"第 {index} 条公告 {language} message 长度非法：length={message.Length}";
                return false;
            }

            string actionText = (source.ActionText ?? string.Empty).Trim();
            if (actionText.Length > MaxActionTextLength)
            {
                error = $"第 {index} 条公告 {language} actionText 长度非法：length={actionText.Length}";
                return false;
            }

            if (action != AppAnnouncementAction.None && actionText.Length == 0)
            {
                error = $"第 {index} 条公告 {language} 缺少 actionText";
                return false;
            }

            text = new AppAnnouncementText(title, message, actionText.Length == 0 ? null : actionText);
            return true;
        }

        private static bool TryParseSeverity(string? value, out InfoBarSeverity severity)
        {
            severity = InfoBarSeverity.Informational;
            string normalized = (value ?? string.Empty).Trim();

            if (string.Equals(normalized, "informational", StringComparison.OrdinalIgnoreCase))
            {
                severity = InfoBarSeverity.Informational;
                return true;
            }

            if (string.Equals(normalized, "success", StringComparison.OrdinalIgnoreCase))
            {
                severity = InfoBarSeverity.Success;
                return true;
            }

            if (string.Equals(normalized, "warning", StringComparison.OrdinalIgnoreCase))
            {
                severity = InfoBarSeverity.Warning;
                return true;
            }

            if (string.Equals(normalized, "error", StringComparison.OrdinalIgnoreCase))
            {
                severity = InfoBarSeverity.Error;
                return true;
            }

            return false;
        }

        private static bool TryParseAction(string? value, out AppAnnouncementAction action)
        {
            action = AppAnnouncementAction.None;
            if (value is null)
            {
                return true;
            }

            string normalized = value.Trim();
            if (string.Equals(normalized, "none", StringComparison.OrdinalIgnoreCase))
            {
                action = AppAnnouncementAction.None;
                return true;
            }

            if (string.Equals(normalized, "openBackupSettings", StringComparison.OrdinalIgnoreCase))
            {
                action = AppAnnouncementAction.OpenBackupSettings;
                return true;
            }

            return false;
        }

        private static bool IsValidId(string id)
        {
            if (id.Length is < 1 or > MaxIdLength)
            {
                return false;
            }

            foreach (char c in id)
            {
                if (c is >= 'a' and <= 'z')
                {
                    continue;
                }

                if (c is >= '0' and <= '9')
                {
                    continue;
                }

                if (c == '-')
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        private sealed class AnnouncementsPayload
        {
            [JsonPropertyName("schemaVersion")]
            public int? SchemaVersion { get; set; }

            [JsonPropertyName("announcements")]
            public List<AnnouncementItem?>? Announcements { get; set; }
        }

        private sealed class AnnouncementItem
        {
            [JsonPropertyName("id")]
            public string? Id { get; set; }

            [JsonPropertyName("severity")]
            public string? Severity { get; set; }

            [JsonPropertyName("action")]
            public string? Action { get; set; }

            [JsonPropertyName("text")]
            public Dictionary<string, AnnouncementText?>? Text { get; set; }
        }

        private sealed class AnnouncementText
        {
            [JsonPropertyName("title")]
            public string? Title { get; set; }

            [JsonPropertyName("message")]
            public string? Message { get; set; }

            [JsonPropertyName("actionText")]
            public string? ActionText { get; set; }
        }
    }
}
