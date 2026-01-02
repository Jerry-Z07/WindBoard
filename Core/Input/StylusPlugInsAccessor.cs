using System;
using System.Reflection;
using System.Windows;
using System.Windows.Input.StylusPlugIns;
using WindBoard.Services;

namespace WindBoard.Core.Input
{
    internal static class StylusPlugInsAccessor
    {
        private static readonly PropertyInfo StylusPlugInsProperty =
            typeof(UIElement).GetProperty("StylusPlugIns", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException(LocalizationService.Instance.GetString("StylusPlugInsAccessor_Unavailable"));

        public static StylusPlugInCollection Get(UIElement element)
        {
            if (StylusPlugInsProperty.GetValue(element) is StylusPlugInCollection collection)
            {
                return collection;
            }

            throw new InvalidOperationException(LocalizationService.Instance.GetString("StylusPlugInsAccessor_Unavailable"));
        }
    }
}
