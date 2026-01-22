using System.Collections.Generic;
using System.Windows;
using WindBoard.Core.Ink;
using Xunit;

namespace WindBoard.Tests.Ink;

public sealed class DetailPreservingSmootherTests
{
    [StaFact]
    public void Push_StraightLineJitter_ReducesDeviation()
    {
        var parameters = DetailPreservingSmootherParameters.NoPressureDefaults;
        var smoother = new DetailPreservingSmoother(parameters, new Point(0, 0), zoomAtStart: 1.0, logicalThicknessDip: 2.0);

        var outputs = new List<DetailPreservingSample>();

        smoother.Push(new DetailPreservingSample(new Point(10, 0.3), pressure: 0.5f), isFinal: false, outputs);
        outputs.Clear();

        smoother.Push(new DetailPreservingSample(new Point(20, 0), pressure: 0.5f), isFinal: true, outputs);

        Assert.True(outputs.Count >= 2);

        double y = outputs[0].CanvasDip.Y;
        Assert.InRange(y, 0.0, 0.1);
    }

    [StaFact]
    public void Push_CornerAngleAboveThreshold_LeavesMidRaw()
    {
        var parameters = DetailPreservingSmootherParameters.NoPressureDefaults;
        var smoother = new DetailPreservingSmoother(parameters, new Point(0, 0), zoomAtStart: 1.0, logicalThicknessDip: 2.0);

        var outputs = new List<DetailPreservingSample>();

        smoother.Push(new DetailPreservingSample(new Point(10, 0), pressure: 0.5f), isFinal: false, outputs);
        outputs.Clear();

        smoother.Push(new DetailPreservingSample(new Point(10, 10), pressure: 0.5f), isFinal: true, outputs);

        Assert.True(outputs.Count >= 2);

        Assert.Equal(10.0, outputs[0].CanvasDip.X, precision: 6);
        Assert.Equal(0.0, outputs[0].CanvasDip.Y, precision: 6);
    }

    [StaFact]
    public void Push_NearOldSegment_ClampsMidToRaw()
    {
        var parameters = DetailPreservingSmootherParameters.NoPressureDefaults;

        const double zoomAtStart = 1.0;
        const double logicalThicknessDip = 2.0;

        // Baseline (no history): mid should be smoothed toward chord.
        var baseline = new DetailPreservingSmoother(parameters, new Point(80, 0), zoomAtStart, logicalThicknessDip);
        var baselineOutputs = new List<DetailPreservingSample>();

        baseline.Push(new DetailPreservingSample(new Point(100, 2), pressure: 0.5f), isFinal: false, baselineOutputs);
        baselineOutputs.Clear();
        baseline.Push(new DetailPreservingSample(new Point(120, 0), pressure: 0.5f), isFinal: true, baselineOutputs);

        Assert.True(baselineOutputs.Count >= 2);
        Assert.True(baselineOutputs[0].CanvasDip.Y < 2.0);

        // With enough history: mid is close to an old segment (y=0), so smoothing is clamped to raw.
        var smoother = new DetailPreservingSmoother(parameters, new Point(0, 0), zoomAtStart, logicalThicknessDip);
        var outputs = new List<DetailPreservingSample>();

        // Build a long path: x-axis then vertical up.
        for (int x = 20; x <= 200; x += 20)
        {
            outputs.Clear();
            smoother.Push(new DetailPreservingSample(new Point(x, 0), pressure: 0.5f), isFinal: false, outputs);
        }

        for (int y = 20; y <= 200; y += 20)
        {
            outputs.Clear();
            smoother.Push(new DetailPreservingSample(new Point(200, y), pressure: 0.5f), isFinal: false, outputs);
        }

        // Jump back near the old x-axis segment and create the test triple.
        outputs.Clear();
        smoother.Push(new DetailPreservingSample(new Point(80, 0), pressure: 0.5f), isFinal: false, outputs);

        outputs.Clear();
        smoother.Push(new DetailPreservingSample(new Point(100, 2), pressure: 0.5f), isFinal: false, outputs);

        outputs.Clear();
        smoother.Push(new DetailPreservingSample(new Point(120, 0), pressure: 0.5f), isFinal: true, outputs);

        Assert.True(outputs.Count >= 2);
        Assert.Equal(2.0, outputs[0].CanvasDip.Y, precision: 6);
    }

    [StaFact]
    public void Push_LongLineThinStroke_StillSmoothsJitter()
    {
        var parameters = DetailPreservingSmootherParameters.NoPressureDefaults;
        var smoother = new DetailPreservingSmoother(parameters, new Point(0, 0), zoomAtStart: 1.0, logicalThicknessDip: 1.0);
        var outputs = new List<DetailPreservingSample>();

        // Feed a long line with a single jitter bump; expect the bump to be reduced even for a thin stroke.
        smoother.Push(new DetailPreservingSample(new Point(50, 0), pressure: 0.5f), isFinal: false, outputs);
        outputs.Clear();

        smoother.Push(new DetailPreservingSample(new Point(100, 2), pressure: 0.5f), isFinal: false, outputs);
        outputs.Clear();

        smoother.Push(new DetailPreservingSample(new Point(150, 0), pressure: 0.5f), isFinal: true, outputs);

        Assert.True(outputs.Count >= 2);
        Assert.True(outputs[0].CanvasDip.Y < 2.0);
    }
}
