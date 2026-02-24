using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace WindBoard.Features.Import.Wbi
{
    /// <summary>
    /// WBI（旧版 WindBoard Interchange）格式模型。
    /// 
    /// 注意：
    /// - 这些类型仅用于“旧格式兼容导入”，不作为新版本的持久化格式；
    /// - 字段名使用 snake_case，与旧版落盘 JSON 保持一致；
    /// - 为兼容历史文件，属性尽量使用可空/默认值。
    /// </summary>
    internal sealed class WbiManifest
    {
        [JsonPropertyName("version")]
        public string? Version { get; set; } = "1.0";

        [JsonPropertyName("min_compatible_version")]
        public string? MinCompatibleVersion { get; set; } = "1.0";

        [JsonPropertyName("app_version")]
        public string? AppVersion { get; set; }

        [JsonPropertyName("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [JsonPropertyName("page_count")]
        public int PageCount { get; set; }

        [JsonPropertyName("include_image_assets")]
        public bool IncludeImageAssets { get; set; }

        [JsonPropertyName("pages")]
        public List<WbiPageRef> Pages { get; set; } = new();
    }

    internal sealed class WbiPageRef
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("number")]
        public int Number { get; set; }
    }

    internal sealed class WbiPageData
    {
        [JsonPropertyName("number")]
        public int Number { get; set; }

        [JsonPropertyName("canvas_width")]
        public double CanvasWidth { get; set; } = 8000;

        [JsonPropertyName("canvas_height")]
        public double CanvasHeight { get; set; } = 8000;

        [JsonPropertyName("zoom")]
        public double Zoom { get; set; } = 1.0;

        [JsonPropertyName("pan_x")]
        public double PanX { get; set; }

        [JsonPropertyName("pan_y")]
        public double PanY { get; set; }

        [JsonPropertyName("strokes_file")]
        public string? StrokesFile { get; set; }

        [JsonPropertyName("attachments")]
        public List<WbiAttachmentData> Attachments { get; set; } = new();
    }

    internal sealed class WbiAttachmentData
    {
        [JsonPropertyName("id")]
        public Guid Id { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("x")]
        public double X { get; set; }

        [JsonPropertyName("y")]
        public double Y { get; set; }

        [JsonPropertyName("width")]
        public double Width { get; set; } = 320;

        [JsonPropertyName("height")]
        public double Height { get; set; } = 180;

        [JsonPropertyName("z_index")]
        public int ZIndex { get; set; }

        [JsonPropertyName("is_pinned_top")]
        public bool IsPinnedTop { get; set; }

        [JsonPropertyName("asset_file")]
        public string? AssetFile { get; set; }

        [JsonPropertyName("file_path")]
        public string? FilePath { get; set; }

        [JsonPropertyName("text")]
        public string? Text { get; set; }

        [JsonPropertyName("url")]
        public string? Url { get; set; }
    }
}
