using System;
using System.Windows;

namespace WindBoard.Core.Input
{
    public class InputEventArgs : EventArgs
    {
        public InputDeviceType DeviceType { get; set; }
        public Point CanvasPoint { get; set; }
        public Point ViewportPoint { get; set; }
        public int? PointerId { get; set; }
        public double? Pressure { get; set; }
        public bool HasPressureHardware { get; set; }
        public bool IsInAir { get; set; }
        public bool LeftButton { get; set; }
        public bool RightButton { get; set; }
        public bool MiddleButton { get; set; }
        public bool Ctrl { get; set; }
        public bool Shift { get; set; }
        public bool Alt { get; set; }
        public long TimestampTicks { get; set; }
        public Size? ContactSize { get; set; }

        /// <summary>
        /// Creates a snapshot copy of this instance.
        /// Some input sources may reuse the same <see cref="InputEventArgs"/> instance across multiple dispatch calls.
        /// </summary>
        public InputEventArgs Clone()
        {
            return CloneWithPoint(CanvasPoint, ViewportPoint);
        }

        public InputEventArgs CloneWithPoint(Point canvasPoint, Point viewportPoint)
        {
            return new InputEventArgs
            {
                DeviceType = DeviceType,
                CanvasPoint = canvasPoint,
                ViewportPoint = viewportPoint,
                PointerId = PointerId,
                Pressure = Pressure,
                HasPressureHardware = HasPressureHardware,
                IsInAir = IsInAir,
                LeftButton = LeftButton,
                RightButton = RightButton,
                MiddleButton = MiddleButton,
                Ctrl = Ctrl,
                Shift = Shift,
                Alt = Alt,
                TimestampTicks = TimestampTicks,
                ContactSize = ContactSize
            };
        }
    }
}
