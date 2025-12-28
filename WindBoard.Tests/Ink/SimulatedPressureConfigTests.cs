using WindBoard.Core.Ink;
using Xunit;

namespace WindBoard.Tests.Ink;

public sealed class SimulatedPressureConfigTests
{
    [Fact]
    public void ClampInPlace_SwapsSpeedRange_WhenMaxBelowMin()
    {
        var cfg = new SimulatedPressureConfig
        {
            SpeedMinMmPerSec = 1000,
            SpeedMaxMmPerSec = 10
        };

        cfg.ClampInPlace();

        Assert.True(cfg.SpeedMinMmPerSec <= cfg.SpeedMaxMmPerSec);
        Assert.Equal(10, cfg.SpeedMinMmPerSec, precision: 12);
        Assert.Equal(1000, cfg.SpeedMaxMmPerSec, precision: 12);
    }

    [Fact]
    public void ClampInPlace_ClampsFloors_AndKeepsEndFloorNotAboveFloor()
    {
        var cfg = new SimulatedPressureConfig
        {
            PressureFloor = 0.2f,
            EndPressureFloor = 0.9f
        };

        cfg.ClampInPlace();

        Assert.Equal(cfg.PressureFloor, cfg.EndPressureFloor);
        Assert.InRange(cfg.PressureFloor, 0.02f, 1.0f);
    }

    [Fact]
    public void Clone_CreatesIndependentCopy()
    {
        var cfg = new SimulatedPressureConfig
        {
            Enabled = false,
            StartTaperMm = 1.23,
            MaxEndTaperPoints = 42
        };

        var clone = cfg.Clone();

        Assert.NotSame(cfg, clone);
        Assert.Equal(cfg.Enabled, clone.Enabled);
        Assert.Equal(cfg.StartTaperMm, clone.StartTaperMm, precision: 12);
        Assert.Equal(cfg.MaxEndTaperPoints, clone.MaxEndTaperPoints);

        clone.MaxEndTaperPoints = 99;
        Assert.Equal(42, cfg.MaxEndTaperPoints);
    }
}

