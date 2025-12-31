using System.Windows.Input;

namespace WindBoard.Core.Input
{
    internal static class StylusPressureHardware
    {
        public static bool HasPressureHardware(StylusPointDescription? description)
        {
            if (description == null) return false;
            if (!description.HasProperty(StylusPointProperties.NormalPressure)) return false;

            try
            {
                var info = description.GetPropertyInfo(StylusPointProperties.NormalPressure);
                return info.Maximum > info.Minimum && info.Resolution > 0;
            }
            catch
            {
                return true;
            }
        }
    }
}

