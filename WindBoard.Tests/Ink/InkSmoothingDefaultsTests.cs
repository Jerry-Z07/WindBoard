using System.Windows;
using WindBoard.Core.Ink;
using Xunit;
using static WindBoard.Tests.TestHelpers.InkTestHelpers;

namespace WindBoard.Tests.Ink;

public sealed class InkSmoothingDefaultsTests
{
    [Fact]
    public void ForContact_WhenNullContact_ReturnsPenDefaults()
    {
        var actual = InkSmoothingDefaults.ForContact(contactSizeCanvasDip: null, zoom: 1.0);

        var expected = new InkSmoothingParameters(
            StepMm: 1.1,
            EpsilonMm: 0.22,
            FcMin: 2.2,
            Beta: 0.045,
            DCutoff: 1.2,
            VStopMmPerSec: 18,
            StopHoldMs: 60,
            FcSticky: 0.8,
            CornerAngleDeg: 120,
            CornerHoldMs: 80,
            FcCorner: 18,
            EpsilonCornerMm: 0.18);

        AssertParametersEqual(expected, actual);
    }

    [Fact]
    public void ForContact_WhenLargeContact_ReturnsFingerDefaults()
    {
        var sizeDip = new Size(31, 31);
        var actual = InkSmoothingDefaults.ForContact(contactSizeCanvasDip: sizeDip, zoom: 1.0);

        var expected = new InkSmoothingParameters(
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

        AssertParametersEqual(expected, actual);
    }

    [Fact]
    public void ForContact_WhenMidContact_LerpsBetweenPenAndFinger()
    {
        const double sizeMm = 6.5;
        var sizeDip = new Size(sizeMm * DipPerMm, sizeMm * DipPerMm);
        var actual = InkSmoothingDefaults.ForContact(contactSizeCanvasDip: sizeDip, zoom: 1.0);

        var expected = new InkSmoothingParameters(
            StepMm: 1.2,
            EpsilonMm: 0.31,
            FcMin: 1.9,
            Beta: 0.0365,
            DCutoff: 1.2,
            VStopMmPerSec: 18,
            StopHoldMs: 60,
            FcSticky: 0.8,
            CornerAngleDeg: 120,
            CornerHoldMs: 80,
            FcCorner: 18,
            EpsilonCornerMm: 0.2);

        AssertParametersEqual(expected, actual);
    }

    private static void AssertParametersEqual(InkSmoothingParameters expected, InkSmoothingParameters actual)
    {
        Assert.Equal(expected.StepMm, actual.StepMm, precision: 12);
        Assert.Equal(expected.EpsilonMm, actual.EpsilonMm, precision: 12);
        Assert.Equal(expected.FcMin, actual.FcMin, precision: 12);
        Assert.Equal(expected.Beta, actual.Beta, precision: 12);
        Assert.Equal(expected.DCutoff, actual.DCutoff, precision: 12);
        Assert.Equal(expected.VStopMmPerSec, actual.VStopMmPerSec, precision: 12);
        Assert.Equal(expected.StopHoldMs, actual.StopHoldMs);
        Assert.Equal(expected.FcSticky, actual.FcSticky, precision: 12);
        Assert.Equal(expected.CornerAngleDeg, actual.CornerAngleDeg, precision: 12);
        Assert.Equal(expected.CornerHoldMs, actual.CornerHoldMs);
        Assert.Equal(expected.FcCorner, actual.FcCorner, precision: 12);
        Assert.Equal(expected.EpsilonCornerMm, actual.EpsilonCornerMm, precision: 12);
    }
}
