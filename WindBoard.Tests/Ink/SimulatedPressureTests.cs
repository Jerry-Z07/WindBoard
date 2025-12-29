using WindBoard.Core.Ink;
using Xunit;

namespace WindBoard.Tests.Ink;

public sealed class SimulatedPressureTests
{
    [Fact]
    public void Update_WhenAttackAndReleaseZero_ReturnsTargetImmediately()
    {
        var parameters = new SimulatedPressureParameters(
            PressureMin: 0.70f,
            PressureMax: 0.90f,
            PressureNominal: 0.80f,
            PressureEnd: 0.60f,
            VSlowMmPerSec: 20,
            VFastMmPerSec: 100,
            VStopMmPerSec: 18,
            StopHoldMs: 60,
            AttackMs: 0,
            ReleaseMs: 0);

        var sim = new SimulatedPressure(parameters);

        float slow = sim.Update(speedMmPerSec: 0, dtSec: 0.016);
        Assert.Equal(parameters.PressureMax, slow, precision: 6);

        float fast = sim.Update(speedMmPerSec: 999, dtSec: 0.016);
        Assert.Equal(parameters.PressureMin, fast, precision: 6);

        float mid = sim.Update(speedMmPerSec: 60, dtSec: 0.016);
        Assert.Equal(0.80f, mid, precision: 6);

        float end = sim.Finish();
        Assert.Equal(parameters.PressureEnd, end, precision: 6);
    }
}

