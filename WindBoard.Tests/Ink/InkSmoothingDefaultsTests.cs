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
            StepMm: 0.9,
            EpsilonMm: 0.15,
            FcMin: 2.8,
            Beta: 0.055,
            DCutoff: 1.2,
            VStopMmPerSec: 12,
            StopHoldMs: 45,
            FcSticky: 1.0,
            CornerAngleDeg: 100,
            CornerHoldMs: 50,
            FcCorner: 22,
            EpsilonCornerMm: 0.12);

        AssertParametersEqual(expected, actual);
    }

    [Fact]
    public void ForContact_WhenLargeContact_ReturnsFingerDefaults()
    {
        var sizeDip = new Size(31, 31);
        var actual = InkSmoothingDefaults.ForContact(contactSizeCanvasDip: sizeDip, zoom: 1.0);

        var expected = new InkSmoothingParameters(
            StepMm: 1.1,
            EpsilonMm: 0.3,
            FcMin: 1.8,
            Beta: 0.035,
            DCutoff: 1.2,
            VStopMmPerSec: 12,
            StopHoldMs: 45,
            FcSticky: 1.0,
            CornerAngleDeg: 105,
            CornerHoldMs: 55,
            FcCorner: 20,
            EpsilonCornerMm: 0.18);

        AssertParametersEqual(expected, actual);
    }

    [Fact]
    public void ForContact_WhenMidContact_LerpsBetweenPenAndFinger()
    {
        const double sizeMm = 6.5;
        var sizeDip = new Size(sizeMm * DipPerMm, sizeMm * DipPerMm);
        var actual = InkSmoothingDefaults.ForContact(contactSizeCanvasDip: sizeDip, zoom: 1.0);

        // t = (6.5 - 5.0) / (8.0 - 5.0) = 0.5
        var expected = new InkSmoothingParameters(
            StepMm: 1.0,
            EpsilonMm: 0.225,
            FcMin: 2.3,
            Beta: 0.045,
            DCutoff: 1.2,
            VStopMmPerSec: 12,
            StopHoldMs: 45,
            FcSticky: 1.0,
            CornerAngleDeg: 102.5,
            CornerHoldMs: 52,
            FcCorner: 21,
            EpsilonCornerMm: 0.15);

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
