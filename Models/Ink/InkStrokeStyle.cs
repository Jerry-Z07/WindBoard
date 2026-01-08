using System.Windows.Media;

namespace WindBoard.Models.Ink
{
    public sealed record InkStrokeStyle(
        InkBrushKind BrushKind,
        Color Color,
        double LogicalThicknessDip,
        bool UsesPressure);
}

