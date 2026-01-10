using WindBoard.Models.InkV2;

namespace WindBoard.Tests.TestHelpers;

internal static class InkV2TestHelpers
{
    public static InkStroke CreateLineStroke(
        double x1,
        double y1,
        double x2,
        double y2,
        InkTool? tool = null,
        float pressure = 0.5f)
    {
        tool ??= InkTool.CreateDefault();

        var stroke = new InkStroke(tool);
        var fragment = new InkFragment();
        fragment.Points.Add(new InkPoint(x1, y1, pressure));
        fragment.Points.Add(new InkPoint(x2, y2, pressure));
        stroke.Fragments.Add(fragment);
        return stroke;
    }
}

