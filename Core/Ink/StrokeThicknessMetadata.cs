using System;
using System.Windows.Ink;

namespace WindBoard.Core.Ink
{
    public static class StrokeThicknessMetadata
    {
        public static readonly Guid LogicalThicknessDipPropertyId = new Guid("2B81C61B-4E2A-4E6C-9D04-0FD9755C03C9");

        public static bool TryGetLogicalThicknessDip(Stroke stroke, out double thicknessDip)
        {
            thicknessDip = 0;
            if (stroke == null) return false;

            if (!stroke.ContainsPropertyData(LogicalThicknessDipPropertyId)) return false;

            try
            {
                var value = stroke.GetPropertyData(LogicalThicknessDipPropertyId);
                if (value is double d)
                {
                    thicknessDip = d;
                    return IsValidPositiveThickness(thicknessDip);
                }
                if (value is float f)
                {
                    thicknessDip = f;
                    return IsValidPositiveThickness(thicknessDip);
                }
                if (value is int i)
                {
                    thicknessDip = i;
                    return IsValidPositiveThickness(thicknessDip);
                }
                if (value is long l)
                {
                    thicknessDip = l;
                    return IsValidPositiveThickness(thicknessDip);
                }
            }
            catch
            {
            }

            return false;
        }

        public static void SetLogicalThicknessDip(Stroke stroke, double thicknessDip)
        {
            if (stroke == null) return;
            if (!IsValidPositiveThickness(thicknessDip)) return;

            try
            {
                if (stroke.ContainsPropertyData(LogicalThicknessDipPropertyId))
                {
                    stroke.RemovePropertyData(LogicalThicknessDipPropertyId);
                }
                stroke.AddPropertyData(LogicalThicknessDipPropertyId, thicknessDip);
            }
            catch
            {
            }
        }

        public static double GetOrCreateLogicalThicknessDip(Stroke stroke, double currentZoom)
        {
            if (TryGetLogicalThicknessDip(stroke, out var logical))
            {
                return logical;
            }

            double zoom = currentZoom;
            if (!IsValidPositiveThickness(zoom)) zoom = 1.0;

            var da = stroke.DrawingAttributes;
            double render = (da.Width + da.Height) / 2.0;
            if (!IsValidPositiveThickness(render)) render = Math.Max(da.Width, da.Height);
            if (!IsValidPositiveThickness(render)) render = 1.0;

            logical = render * zoom;
            SetLogicalThicknessDip(stroke, logical);
            return logical;
        }

        private static bool IsValidPositiveThickness(double value)
            => !(double.IsNaN(value) || double.IsInfinity(value) || value <= 0);
    }
}
