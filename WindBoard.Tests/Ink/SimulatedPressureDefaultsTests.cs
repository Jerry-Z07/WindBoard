using System.Windows;
using WindBoard.Core.Ink;
using Xunit;
using static WindBoard.Tests.TestHelpers.InkTestHelpers;

namespace WindBoard.Tests.Ink;

public sealed class SimulatedPressureDefaultsTests
{
    [Fact]
    public void ForContact_WhenNullContact_ReturnsPenDefaults()
    {
        var actual = SimulatedPressureDefaults.ForContact(contactSizeCanvasDip: null, zoom: 1.0);

        var expected = new SimulatedPressureParameters(
            PressureMin: 0.78f,
            PressureMax: 0.95f,
            PressureNominal: 0.86f,
            PressureEnd: 0.60f,
            VSlowMmPerSec: 35,
            VFastMmPerSec: 260,
            VStopMmPerSec: 18,
            StopHoldMs: 60,
            AttackMs: 25,
            ReleaseMs: 15);

        AssertParametersEqual(expected, actual);
    }

    [Fact]
    public void ForContact_WhenLargeContact_ReturnsFingerDefaults()
    {
        var sizeDip = new Size(31, 31);
        var actual = SimulatedPressureDefaults.ForContact(contactSizeCanvasDip: sizeDip, zoom: 1.0);

        var expected = new SimulatedPressureParameters(
            PressureMin: 0.82f,
            PressureMax: 0.94f,
            PressureNominal: 0.88f,
            PressureEnd: 0.65f,
            VSlowMmPerSec: 45,
            VFastMmPerSec: 320,
            VStopMmPerSec: 18,
            StopHoldMs: 60,
            AttackMs: 30,
            ReleaseMs: 18);

        AssertParametersEqual(expected, actual);
    }

    [Fact]
    public void ForContact_WhenMidContact_LerpsBetweenPenAndFinger()
    {
        const double sizeMm = 6.5;
        var sizeDip = new Size(sizeMm * DipPerMm, sizeMm * DipPerMm);
        var actual = SimulatedPressureDefaults.ForContact(contactSizeCanvasDip: sizeDip, zoom: 1.0);

        var expected = new SimulatedPressureParameters(
            PressureMin: 0.80f,
            PressureMax: 0.945f,
            PressureNominal: 0.87f,
            PressureEnd: 0.625f,
            VSlowMmPerSec: 40,
            VFastMmPerSec: 290,
            VStopMmPerSec: 18,
            StopHoldMs: 60,
            AttackMs: 28,
            ReleaseMs: 16);

        AssertParametersEqual(expected, actual);
    }

    private static void AssertParametersEqual(SimulatedPressureParameters expected, SimulatedPressureParameters actual)
    {
        Assert.Equal(expected.PressureMin, actual.PressureMin, precision: 6);
        Assert.Equal(expected.PressureMax, actual.PressureMax, precision: 6);
        Assert.Equal(expected.PressureNominal, actual.PressureNominal, precision: 6);
        Assert.Equal(expected.PressureEnd, actual.PressureEnd, precision: 6);
        Assert.Equal(expected.VSlowMmPerSec, actual.VSlowMmPerSec, precision: 12);
        Assert.Equal(expected.VFastMmPerSec, actual.VFastMmPerSec, precision: 12);
        Assert.Equal(expected.VStopMmPerSec, actual.VStopMmPerSec, precision: 12);
        Assert.Equal(expected.StopHoldMs, actual.StopHoldMs);
        Assert.Equal(expected.AttackMs, actual.AttackMs);
        Assert.Equal(expected.ReleaseMs, actual.ReleaseMs);
    }
}
