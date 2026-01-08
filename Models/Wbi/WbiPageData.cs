using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace WindBoard.Models.Wbi
{
    /// <summary>
    /// WBI 页面数据
    /// </summary>
    public sealed class WbiPageData
    {
        /// <summary>页码</summary>
        [JsonProperty("number")]
        public int Number { get; set; }

        /// <summary>画布宽度</summary>
        [JsonProperty("canvas_width")]
        public double CanvasWidth { get; set; } = 8000;

        /// <summary>画布高度</summary>
        [JsonProperty("canvas_height")]
        public double CanvasHeight { get; set; } = 8000;

        /// <summary>缩放级别</summary>
        [JsonProperty("zoom")]
        public double Zoom { get; set; } = 1.0;

        /// <summary>水平平移</summary>
        [JsonProperty("pan_x")]
        public double PanX { get; set; }

        /// <summary>垂直平移</summary>
        [JsonProperty("pan_y")]
        public double PanY { get; set; }

        /// <summary>笔迹文件名（.isf）</summary>
        [JsonProperty("strokes_file")]
        public string? StrokesFile { get; set; }

        /// <summary>附件列表</summary>
        [JsonProperty("attachments")]
        public List<WbiAttachmentData> Attachments { get; set; } = new();
    }

    /// <summary>
    /// WBI 附件数据
    /// </summary>
    public sealed class WbiAttachmentData
    {
        /// <summary>唯一标识符</summary>
        [JsonProperty("id")]
        public Guid Id { get; set; }

        /// <summary>附件类型</summary>
        [JsonProperty("type")]
        public string Type { get; set; } = "Image";

        /// <summary>X 坐标</summary>
        [JsonProperty("x")]
        public double X { get; set; }

        /// <summary>Y 坐标</summary>
        [JsonProperty("y")]
        public double Y { get; set; }

        /// <summary>宽度</summary>
        [JsonProperty("width")]
        public double Width { get; set; } = 320;

        /// <summary>高度</summary>
        [JsonProperty("height")]
        public double Height { get; set; } = 180;

        /// <summary>Z 序号</summary>
        [JsonProperty("z_index")]
        public int ZIndex { get; set; }

        /// <summary>是否置顶</summary>
        [JsonProperty("is_pinned_top")]
        public bool IsPinnedTop { get; set; }

        /// <summary>资源文件名（图片附件，存储在 assets/ 目录）</summary>
        [JsonProperty("asset_file")]
        public string? AssetFile { get; set; }

        /// <summary>原始文件路径（视频附件）</summary>
        [JsonProperty("file_path")]
        public string? FilePath { get; set; }

        /// <summary>文本内容</summary>
        [JsonProperty("text")]
        public string? Text { get; set; }

        /// <summary>链接 URL</summary>
        [JsonProperty("url")]
        public string? Url { get; set; }
    }
}
