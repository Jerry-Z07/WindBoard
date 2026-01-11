using System;
using System.Windows.Ink;
using WindBoard.Models.InkV2;

namespace WindBoard.Core.Ink
{
    public static class StrokeInkSemanticsMetadata
    {
        public static readonly Guid ThicknessSemanticsPropertyId = new Guid("B2B0B7E1-7FD2-4D39-9C7E-5B6A4A2E8B1F");
        public static readonly Guid InkStrokeIdPropertyId = new Guid("D4E1B1F8-17D1-4E8A-9A52-0B2D4A8BE6C3");
        public static readonly Guid InkFragmentIdPropertyId = new Guid("A0F6B6B2-6F38-4EAA-9A9C-3B6E6E4E3E8A");

        public static bool TryGetThicknessSemantics(Stroke stroke, out InkThicknessSemantics semantics)
        {
            semantics = default;
            if (stroke == null) return false;
            if (!stroke.ContainsPropertyData(ThicknessSemanticsPropertyId)) return false;

            object? value;
            try
            {
                value = stroke.GetPropertyData(ThicknessSemanticsPropertyId);
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }

            if (value is InkThicknessSemantics e)
            {
                semantics = e;
                return true;
            }

            if (value is int i && Enum.IsDefined(typeof(InkThicknessSemantics), i))
            {
                semantics = (InkThicknessSemantics)i;
                return true;
            }

            if (value is string s && Enum.TryParse<InkThicknessSemantics>(s, ignoreCase: true, out var parsed))
            {
                semantics = parsed;
                return true;
            }

            return false;
        }

        public static void SetThicknessSemantics(Stroke stroke, InkThicknessSemantics semantics)
        {
            if (stroke == null) return;

            try
            {
                if (stroke.ContainsPropertyData(ThicknessSemanticsPropertyId))
                {
                    stroke.RemovePropertyData(ThicknessSemanticsPropertyId);
                }
                stroke.AddPropertyData(ThicknessSemanticsPropertyId, (int)semantics);
            }
            catch (ArgumentException)
            {
            }
            catch (InvalidOperationException)
            {
            }
        }

        public static bool TryGetInkStrokeId(Stroke stroke, out Guid strokeId)
        {
            strokeId = default;
            return TryGetGuidProperty(stroke, InkStrokeIdPropertyId, out strokeId);
        }

        public static void SetInkStrokeId(Stroke stroke, Guid strokeId)
        {
            SetGuidProperty(stroke, InkStrokeIdPropertyId, strokeId);
        }

        public static bool TryGetInkFragmentId(Stroke stroke, out Guid fragmentId)
        {
            fragmentId = default;
            return TryGetGuidProperty(stroke, InkFragmentIdPropertyId, out fragmentId);
        }

        public static void SetInkFragmentId(Stroke stroke, Guid fragmentId)
        {
            SetGuidProperty(stroke, InkFragmentIdPropertyId, fragmentId);
        }

        private static bool TryGetGuidProperty(Stroke stroke, Guid propertyId, out Guid valueGuid)
        {
            valueGuid = default;
            if (stroke == null) return false;
            if (!stroke.ContainsPropertyData(propertyId)) return false;

            object? value;
            try
            {
                value = stroke.GetPropertyData(propertyId);
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }

            if (value is Guid g)
            {
                valueGuid = g;
                return true;
            }

            if (value is string s && Guid.TryParse(s, out var parsed))
            {
                valueGuid = parsed;
                return true;
            }

            return false;
        }

        private static void SetGuidProperty(Stroke stroke, Guid propertyId, Guid value)
        {
            if (stroke == null) return;

            try
            {
                if (stroke.ContainsPropertyData(propertyId))
                {
                    stroke.RemovePropertyData(propertyId);
                }
                stroke.AddPropertyData(propertyId, value);
            }
            catch (ArgumentException)
            {
            }
            catch (InvalidOperationException)
            {
            }
        }
    }
}

