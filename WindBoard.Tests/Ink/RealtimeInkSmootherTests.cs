using System;
using System.Windows;
using WindBoard.Core.Ink;
using Xunit;
using static WindBoard.Tests.TestHelpers.InkTestHelpers;

namespace WindBoard.Tests.Ink;

public sealed class RealtimeInkSmootherTests
{
    [StaFact]
    public void Process_DoesNotThrow_WhenCornerAndStickyOverlap()
    {
        var parameters = new InkSmoothingParameters(
            StepMm: 0.05,
            EpsilonMm: 0.001,
            FcMin: 2.2,
            Beta: 0.045,
            DCutoff: 1.2,
            VStopMmPerSec: 18,
            StopHoldMs: 60,
            FcSticky: 0.8,
            CornerAngleDeg: 120,
            CornerHoldMs: 80,
            FcCorner: 18,
            EpsilonCornerMm: 0.001);

        var smoother = new RealtimeInkSmoother(parameters);
        const double zoom = 1.0;

        long ticks = 0;
        smoother.Process(ToDip(0, 0), ticks, zoom, isFinal: false);

        ticks += 50 * TimeSpan.TicksPerMillisecond;
        smoother.Process(ToDip(0.05, 0), ticks, zoom, isFinal: false);

        ticks += 50 * TimeSpan.TicksPerMillisecond;
        smoother.Process(ToDip(0.10, 0), ticks, zoom, isFinal: false);

        ticks += 50 * TimeSpan.TicksPerMillisecond;
        smoother.Process(ToDip(0.10, 0.05), ticks, zoom, isFinal: false);

        ticks += 50 * TimeSpan.TicksPerMillisecond;
        var output = smoother.Process(ToDip(0.10, 0.10), ticks, zoom, isFinal: true);

        Assert.NotEmpty(output);

        Point ToDip(double xMm, double yMm) => new(xMm * DipPerMm / zoom, yMm * DipPerMm / zoom);
    }

    [StaFact]
    public void Process_IsFinalTrue_OutputsRawEndpoint()
    {
        var parameters = new InkSmoothingParameters(
            StepMm: 1.3,
            EpsilonMm: 0.4,
            FcMin: 1.6,
            Beta: 0.028,
            DCutoff: 1.2,
            VStopMmPerSec: 18,
            StopHoldMs: 60,
            FcSticky: 0.8,
            CornerAngleDeg: 120,
            CornerHoldMs: 80,
            FcCorner: 18,
            EpsilonCornerMm: 0.22);

        var smoother = new RealtimeInkSmoother(parameters);
        const double zoom = 1.0;

        long ticks = 0;
        smoother.Process(ToDip(0, 0), ticks, zoom, isFinal: false);

        ticks += 16 * TimeSpan.TicksPerMillisecond;
        smoother.Process(ToDip(50, 0), ticks, zoom, isFinal: false);

        // 典型触摸书写：先停住再抬起（Up 点与最后一个 Move 相同）
        ticks += 50 * TimeSpan.TicksPerMillisecond;
        var output = smoother.Process(ToDip(50, 0), ticks, zoom, isFinal: true);

        Assert.NotEmpty(output);

        var endDip = smoother.ScreenMmToCanvasDip(output[^1], zoom);
        Assert.Equal(ToDip(50, 0).X, endDip.X, precision: 6);
        Assert.Equal(ToDip(50, 0).Y, endDip.Y, precision: 6);

        Point ToDip(double xMm, double yMm) => new(xMm * DipPerMm / zoom, yMm * DipPerMm / zoom);
    }
}
