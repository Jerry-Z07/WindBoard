using System.Collections.Generic;
using System.Numerics;
using WindBoard.Board;
using WindBoard.Board.Commands;
using Xunit;

namespace WindBoard.Tests.Board.Commands;

public sealed class UpdateStrokePointsCommandTests
{
    // Do/Undo 应能正确回放笔迹点列与包围盒
    [Fact]
    public void Do_Undo_UpdatesPointsAndBounds()
    {
        var stroke = new Stroke
        {
            BaseSize = 6.0f,
            EnablePressure = false,
        };

        stroke.Points.Add(new StrokePoint(new Vector2(0, 0), 1.0f));
        stroke.ExpandBounds(new Vector2(0, 0), 1.0f);
        stroke.Points.Add(new StrokePoint(new Vector2(10, 0), 1.0f));
        stroke.ExpandBounds(new Vector2(10, 0), 1.0f);

        var before = new List<StrokePoint>(stroke.Points);
        var after = new List<StrokePoint>
        {
            new(new Vector2(5, 5), 1.0f),
            new(new Vector2(15, 5), 1.0f),
        };

        var command = new UpdateStrokePointsCommand(stroke, before, after);
        var doc = new BoardDocument();

        command.Do(doc);
        Assert.Equal(after, stroke.Points);
        AssertEx.Equal(new Vector2(2, 2), stroke.BoundsMin);
        AssertEx.Equal(new Vector2(18, 8), stroke.BoundsMax);

        command.Undo(doc);
        Assert.Equal(before, stroke.Points);
        AssertEx.Equal(new Vector2(-3, -3), stroke.BoundsMin);
        AssertEx.Equal(new Vector2(13, 3), stroke.BoundsMax);
    }
}

