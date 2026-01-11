using System;
using System.Windows;
using System.Windows.Controls;
using WindBoard.Core.Input;
using WindBoard.Core.Modes;
using WindBoard.Models.InkV2;
using Xunit;

namespace WindBoard.Tests.Ink;

public sealed class InkModeTests
{
    [StaFact]
    public void OnPointerUp_Touch_AppendsRawPoints()
    {
        var canvas = new Canvas { Width = 8000, Height = 8000 };

        var page = new BoardPage();
        var mode = new InkMode(canvas, () => 1.0, () => page, InkTool.CreateDefault);
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

        Assert.Single(page.Ink.Strokes);
        var stroke = page.Ink.Strokes[0];
        Assert.Single(stroke.Fragments);
        var fragment = stroke.Fragments[0];
        Assert.Equal(3, fragment.Points.Count);

        Assert.Equal(0, fragment.Points[0].XDip, precision: 6);
        Assert.Equal(0, fragment.Points[0].YDip, precision: 6);
        Assert.Equal(1, fragment.Points[1].XDip, precision: 6);
        Assert.Equal(0, fragment.Points[1].YDip, precision: 6);
        Assert.Equal(2, fragment.Points[2].XDip, precision: 6);
        Assert.Equal(0, fragment.Points[2].YDip, precision: 6);
    }
}
