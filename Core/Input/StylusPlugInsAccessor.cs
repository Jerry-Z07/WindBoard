using System;
using System.Reflection;
using System.Windows;
using System.Windows.Input.StylusPlugIns;

namespace WindBoard.Core.Input
{
    internal static class StylusPlugInsAccessor
    {
        private static readonly PropertyInfo StylusPlugInsProperty =
            typeof(UIElement).GetProperty("StylusPlugIns", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("无法访问 StylusPlugIns 属性");

        public static StylusPlugInCollection Get(UIElement element)
        {
            if (StylusPlugInsProperty.GetValue(element) is StylusPlugInCollection collection)
            {
                return collection;
            }

            throw new InvalidOperationException("无法访问 StylusPlugIns 属性");
        }
    }
}
