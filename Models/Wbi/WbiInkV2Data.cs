using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace WindBoard.Models.Wbi
{
    public sealed class WbiInkV2DocumentData
    {
        [JsonProperty("strokes")]
        public List<WbiInkV2StrokeData> Strokes { get; set; } = new();
    }

    public sealed class WbiInkV2StrokeData
    {
        [JsonProperty("id")]
        public Guid Id { get; set; }

        [JsonProperty("tool")]
        public WbiInkV2ToolData Tool { get; set; } = new();

        [JsonProperty("fragments")]
        public List<WbiInkV2FragmentData> Fragments { get; set; } = new();
    }

    public sealed class WbiInkV2FragmentData
    {
        [JsonProperty("id")]
        public Guid Id { get; set; }

        [JsonProperty("points")]
        public List<WbiInkV2PointData> Points { get; set; } = new();
    }

    public sealed class WbiInkV2ToolData
    {
        [JsonProperty("color_argb")]
        public uint ColorArgb { get; set; }

        [JsonProperty("base_thickness_dip")]
        public double BaseThicknessDip { get; set; }

        [JsonProperty("thickness_semantics")]
        public int ThicknessSemantics { get; set; }

        [JsonProperty("brush_kind")]
        public int BrushKind { get; set; }

        [JsonProperty("uses_pressure")]
        public bool UsesPressure { get; set; }

        [JsonProperty("pressure_nominal")]
        public float PressureNominal { get; set; }
    }

    public sealed class WbiInkV2PointData
    {
        [JsonProperty("x")]
        public double XDip { get; set; }

        [JsonProperty("y")]
        public double YDip { get; set; }

        [JsonProperty("p")]
        public float Pressure { get; set; }

        [JsonProperty("t")]
        public long TimestampTicks { get; set; }
    }
}

