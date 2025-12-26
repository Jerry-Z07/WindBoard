using System.Windows;

namespace WindBoard.Core.Ink
{
    public sealed record InkSmoothingParameters(
        double StepMm,
        double EpsilonMm,
        double FcMin,
        double Beta,
        double DCutoff,
        double VStopMmPerSec,
        int StopHoldMs,
        double FcSticky,
        double CornerAngleDeg,
        int CornerHoldMs,
        double FcCorner,
        double EpsilonCornerMm);
}

