using System.Windows.Ink;
using System.Windows.Input;

namespace WindBoard.Tests.TestHelpers;

internal static class InkTestHelpers
{
    public const double DipPerMm = 96.0 / 25.4;

    public static Stroke CreateStroke()
    {
        var points = new StylusPointCollection
        {
            new StylusPoint(0, 0),
            new StylusPoint(1, 1)
        };

        return new Stroke(points);
    }
}
