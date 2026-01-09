using System;

namespace WindBoard.Models.InkV2
{
    public sealed record InkTool(
        uint ColorArgb,
        double BaseThicknessDip,
        InkThicknessSemantics ThicknessSemantics,
        InkBrushKind BrushKind = InkBrushKind.Pen)
    {
        public static InkTool CreateDefault()
        {
            // Default: opaque white, view-invariant thickness, matches current app's typical white ink.
            return new InkTool(
                ColorArgb: 0xFFFFFFFF,
                BaseThicknessDip: 2.0,
                ThicknessSemantics: InkThicknessSemantics.ViewInvariant,
                BrushKind: InkBrushKind.Pen);
        }

        public static InkTool CreateFromColor(uint colorArgb)
        {
            return new InkTool(
                ColorArgb: colorArgb,
                BaseThicknessDip: 2.0,
                ThicknessSemantics: InkThicknessSemantics.ViewInvariant,
                BrushKind: InkBrushKind.Pen);
        }
    }
}

