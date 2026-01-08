using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using WindBoard.Core.Input;
using WindBoard.Core.Modes;
using Xunit;

namespace WindBoard.Tests.Ink;

public sealed class InkModeTests
{
    [StaFact]
    public void OnPointerUp_Touch_AppendsRawPoints()
    {
        var canvas = new InkCanvas
        {
            Width = 8000,
            Height = 8000,
            Strokes = new StrokeCollection()
        };

        var mode = new InkMode(canvas, () => 1.0);
        mode.SwitchOn();

        long ticks = 0;
        var contactSize = new Size(50, 50);

        mode.OnPointerDown(new InputEventArgs
        {
            DeviceType = InputDeviceType.Touch,
            CanvasPoint = new Point(0, 0),
            ViewportPoint = new Point(0, 0),
            PointerId = 1,
            Pressure = null,
            IsInAir = false,
            LeftButton = false,
            RightButton = false,
            MiddleButton = false,
            Ctrl = false,
            Shift = false,
            Alt = false,
            TimestampTicks = ticks,
            ContactSize = contactSize
        });

        Assert.Single(canvas.Strokes);
        var stroke = canvas.Strokes[0];

        ticks += 16 * TimeSpan.TicksPerMillisecond;
        mode.OnPointerMove(new InputEventArgs
        {
            DeviceType = InputDeviceType.Touch,
            CanvasPoint = new Point(1, 0),
            ViewportPoint = new Point(1, 0),
            PointerId = 1,
            Pressure = null,
            IsInAir = false,
            LeftButton = false,
            RightButton = false,
            MiddleButton = false,
            Ctrl = false,
            Shift = false,
            Alt = false,
            TimestampTicks = ticks,
            ContactSize = contactSize
        });

        ticks += 16 * TimeSpan.TicksPerMillisecond;
        mode.OnPointerUp(new InputEventArgs
        {
            DeviceType = InputDeviceType.Touch,
            CanvasPoint = new Point(2, 0),
            ViewportPoint = new Point(2, 0),
            PointerId = 1,
            Pressure = null,
            IsInAir = false,
            LeftButton = false,
            RightButton = false,
            MiddleButton = false,
            Ctrl = false,
            Shift = false,
            Alt = false,
            TimestampTicks = ticks,
            ContactSize = contactSize
        });

        Assert.Equal(3, stroke.StylusPoints.Count);

        Assert.Equal(0, stroke.StylusPoints[0].X, precision: 6);
        Assert.Equal(0, stroke.StylusPoints[0].Y, precision: 6);
        Assert.Equal(1, stroke.StylusPoints[1].X, precision: 6);
        Assert.Equal(0, stroke.StylusPoints[1].Y, precision: 6);
        Assert.Equal(2, stroke.StylusPoints[2].X, precision: 6);
        Assert.Equal(0, stroke.StylusPoints[2].Y, precision: 6);
    }
}
