using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using WindBoard.Models.Ink;

namespace WindBoard.Models.Wbi
{
    /// <summary>
    /// WBI 笔迹模型数据（用于自绘/模型引擎）
    /// </summary>
    public sealed class WbiInkPayload
    {
        [JsonProperty("version")]
        public string Version { get; set; } = "1.0";

        [JsonProperty("strokes")]
        public List<WbiInkStrokeData> Strokes { get; set; } = new();
    }

    public sealed class WbiInkStrokeData
    {
        [JsonProperty("id")]
        public Guid Id { get; set; }

        [JsonProperty("zoom_at_creation")]
        public double ZoomAtCreation { get; set; } = 1.0;

        [JsonProperty("brush_kind")]
        public InkBrushKind BrushKind { get; set; } = InkBrushKind.Pen;

        /// <summary>ARGB packed as 0xAARRGGBB</summary>
        [JsonProperty("color")]
        public uint ColorArgb { get; set; }

        [JsonProperty("logical_thickness_dip")]
        public double LogicalThicknessDip { get; set; } = 1.0;

        [JsonProperty("uses_pressure")]
        public bool UsesPressure { get; set; }

        [JsonProperty("points")]
        public List<WbiInkPointData> Points { get; set; } = new();
    }

    public sealed class WbiInkPointData
    {
        [JsonProperty("x")]
        public double X { get; set; }

        [JsonProperty("y")]
        public double Y { get; set; }

        [JsonProperty("p")]
        public float Pressure { get; set; }

        [JsonProperty("t")]
        public long TimestampTicks { get; set; }
    }
}

