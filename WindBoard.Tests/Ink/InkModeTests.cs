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
    public void OnPointerMove_Touch_LiveTailFollowsRawPoint()
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

        Assert.True(stroke.StylusPoints.Count >= 2);

        var anchor = stroke.StylusPoints[^2];
        Assert.Equal(0, anchor.X, precision: 6);
        Assert.Equal(0, anchor.Y, precision: 6);

        var tail = stroke.StylusPoints[^1];
        Assert.Equal(1, tail.X, precision: 6);
        Assert.Equal(0, tail.Y, precision: 6);
    }
}

