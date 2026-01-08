using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using WindBoard.Controls;
using WindBoard.Core.Ink.Backend;
using WindBoard.Core.Input;
using WindBoard.Core.Modes;
using WindBoard.Models.Ink;
using Xunit;

namespace WindBoard.Tests.Ink;

public sealed class InkModeTests
{
    [StaFact]
    public void OnPointerUp_Touch_AppendsRawPoints()
    {
        var canvas = new InkCanvas { Width = 8000, Height = 8000 };
        var surface = new InkSurface();
        using var backend = new CustomInkBackend(surface);
        var document = new List<InkStrokeModel>();
        backend.BindDocument(document);

        var mode = new InkMode(canvas, backend, () => 1.0);
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

        Assert.Single(document);
        var stroke = document[0];

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

        Assert.Equal(3, stroke.Points.Count);

        Assert.Equal(0, stroke.Points[0].X, precision: 6);
        Assert.Equal(0, stroke.Points[0].Y, precision: 6);
        Assert.Equal(1, stroke.Points[1].X, precision: 6);
        Assert.Equal(0, stroke.Points[1].Y, precision: 6);
        Assert.Equal(2, stroke.Points[2].X, precision: 6);
        Assert.Equal(0, stroke.Points[2].Y, precision: 6);
    }
}
