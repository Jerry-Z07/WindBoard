using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text.Json;
using WindBoard.Board.Elements;

namespace WindBoard.Board.Persistence.Wbix
{
    internal sealed partial class WbixWorkspaceSerializer
    {
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

