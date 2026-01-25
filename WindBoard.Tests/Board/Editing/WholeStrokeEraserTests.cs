using System.Numerics;
using WindBoard.Board;
using WindBoard.Board.Editing;
using Xunit;

namespace WindBoard.Tests.Board.Editing;

public sealed class WholeStrokeEraserTests
{
    [Fact]
    public void Erase_命中时会删除整条笔迹()
    {
        var document = new BoardDocument();

        var hitStroke = CreateStroke(
            new Vector2(0.0f, 0.0f),
            new Vector2(10.0f, 0.0f));

        var keepStroke = CreateStroke(
            new Vector2(100.0f, 100.0f),
            new Vector2(110.0f, 100.0f));

        document.Strokes.Add(hitStroke);
        document.Strokes.Add(keepStroke);

        var eraser = new WholeStrokeEraser();
        bool changed = eraser.Erase(
            document,
            fromWorld: new Vector2(5.0f, -5.0f),
            toWorld: new Vector2(5.0f, 5.0f),
            radiusWorld: 0.0f);

        Assert.True(changed);
        Assert.Single(document.Strokes);
        Assert.Same(keepStroke, document.Strokes[0]);
    }

    [Fact]
    public void Erase_未命中时不会修改文档()
    {
        var document = new BoardDocument();

        var stroke = CreateStroke(
            new Vector2(0.0f, 0.0f),
            new Vector2(10.0f, 0.0f));

        document.Strokes.Add(stroke);

        var eraser = new WholeStrokeEraser();
        bool changed = eraser.Erase(
            document,
            fromWorld: new Vector2(100.0f, 100.0f),
            toWorld: new Vector2(110.0f, 110.0f),
            radiusWorld: 1.0f);

        Assert.False(changed);
        Assert.Single(document.Strokes);
        Assert.Same(stroke, document.Strokes[0]);
    }

    private static Stroke CreateStroke(Vector2 p0, Vector2 p1)
    {
        var stroke = new Stroke
        {
            BaseSize = 6.0f,
            EnablePressure = false,
        };

        stroke.Points.Add(new StrokePoint(p0, 1.0f));
        stroke.ExpandBounds(p0, 1.0f);

        stroke.Points.Add(new StrokePoint(p1, 1.0f));
        stroke.ExpandBounds(p1, 1.0f);

        return stroke;
    }
}

