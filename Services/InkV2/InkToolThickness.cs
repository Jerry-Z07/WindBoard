using System;
using WindBoard.Models.InkV2;

namespace WindBoard.Services.InkV2
{
    internal static class InkToolThickness
    {
        public static double ComputeLogicalThicknessDip(InkTool tool)
        {
            double thickness = tool.BaseThicknessDip;
            if (thickness <= 0 || double.IsNaN(thickness) || double.IsInfinity(thickness))
            {
                thickness = 1.0;
            }

            if (!tool.UsesPressure)
            {
                return thickness;
            }

            float nominal = tool.PressureNominal;
            if (float.IsNaN(nominal) || float.IsInfinity(nominal) || nominal <= 0.05f || nominal > 1.0f)
            {
                return thickness;
            }

            return thickness / nominal;
        }

        public static double ComputeRenderThicknessDip(InkTool tool, double zoom, double logicalThicknessDip)
        {
            if (zoom <= 0) zoom = 1.0;

            return tool.ThicknessSemantics == InkThicknessSemantics.ViewInvariant
                ? logicalThicknessDip / zoom
                : logicalThicknessDip;
        }

        public static double ComputeScreenThicknessDip(InkTool tool, double zoom, double logicalThicknessDip)
        {
            if (zoom <= 0) zoom = 1.0;

            return tool.ThicknessSemantics == InkThicknessSemantics.WorldInvariant
                ? logicalThicknessDip * zoom
                : logicalThicknessDip;
        }
    }
}

