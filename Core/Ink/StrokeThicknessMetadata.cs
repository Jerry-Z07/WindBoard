using System;
using System.Globalization;
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

            object? value;
            try
            {
                value = stroke.GetPropertyData(LogicalThicknessDipPropertyId);
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }

            thicknessDip = ToDoubleOrDefault(value);
            return IsValidPositiveThickness(thicknessDip);

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
            catch (ArgumentException)
            {
            }
            catch (InvalidOperationException)
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
            double render = IsValidPositiveThickness(da.Width) && IsValidPositiveThickness(da.Height)
                ? (da.Width + da.Height) / 2.0
                : Math.Max(da.Width, da.Height);
            if (!IsValidPositiveThickness(render)) render = 1.0;

            logical = render * zoom;
            SetLogicalThicknessDip(stroke, logical);
            return logical;
        }

        private static double ToDoubleOrDefault(object? value)
        {
            if (value == null) return 0;
            if (value is double d) return d;
            if (value is float f) return f;

            try
            {
                return Convert.ToDouble(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return 0;
            }
        }

        private static bool IsValidPositiveThickness(double value)
            => !(double.IsNaN(value) || double.IsInfinity(value) || value <= 0);
    }
}
