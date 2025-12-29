namespace WindBoard.Core.Ink
{
    public sealed record SimulatedPressureParameters(
        float PressureMin,
        float PressureMax,
        float PressureNominal,
        float PressureEnd,
        double VSlowMmPerSec,
        double VFastMmPerSec,
        double VStopMmPerSec,
        int StopHoldMs,
        int AttackMs,
        int ReleaseMs);
}

