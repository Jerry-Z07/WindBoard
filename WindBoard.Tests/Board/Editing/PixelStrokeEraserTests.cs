using System.Linq;
using System.Numerics;
using WindBoard.Board;
using WindBoard.Board.Editing;
using Xunit;

namespace WindBoard.Tests.Board.Editing;

public sealed class PixelStrokeEraserTests
{
    [Fact]
    public void Erase_命中中间时会把笔迹分割为两段()
    {
        var document = new BoardDocument();

        Stroke stroke = StrokeTestFactory.CreateStroke(
            new Vector2(0.0f, 0.0f),
            new Vector2(10.0f, 0.0f));

        document.Strokes.Add(stroke);

        var eraser = new PixelStrokeEraser();
        bool changed = eraser.Erase(
            document,
            fromWorld: new Vector2(5.0f, -5.0f),
            toWorld: new Vector2(5.0f, 5.0f),
            radiusWorld: Vector2.Zero);

        Assert.True(changed);
        Assert.Equal(2, document.Strokes.Count);
        Assert.DoesNotContain(stroke, document.Strokes);

        float leftMaxX = document.Strokes[0].Points.Max(p => p.Position.X);
        float rightMinX = document.Strokes[1].Points.Min(p => p.Position.X);
        Assert.True(leftMaxX < 5.0f);
        Assert.True(rightMinX > 5.0f);
    }

    [Fact]
    public void Erase_只命中一端时会截断笔迹()
    {
        var document = new BoardDocument();

        Stroke stroke = StrokeTestFactory.CreateStroke(
            new Vector2(0.0f, 0.0f),
            new Vector2(10.0f, 0.0f));

        document.Strokes.Add(stroke);

        var eraser = new PixelStrokeEraser();
        bool changed = eraser.Erase(
            document,
            fromWorld: new Vector2(0.0f, -5.0f),
            toWorld: new Vector2(0.0f, 5.0f),
            radiusWorld: Vector2.Zero);

        Assert.True(changed);
        Assert.Single(document.Strokes);
        Assert.NotSame(stroke, document.Strokes[0]);

        float minX = document.Strokes[0].Points.Min(p => p.Position.X);
        Assert.True(minX > 0.0f);
    }

    [Fact]
    public void Erase_覆盖整条笔迹时会删除()
    {
        var document = new BoardDocument();

        Stroke stroke = StrokeTestFactory.CreateStroke(
            new Vector2(0.0f, 0.0f),
            new Vector2(10.0f, 0.0f));

        document.Strokes.Add(stroke);

        var eraser = new PixelStrokeEraser();
        bool changed = eraser.Erase(
            document,
            fromWorld: new Vector2(5.0f, -5.0f),
            toWorld: new Vector2(5.0f, 5.0f),
            radiusWorld: new Vector2(100.0f, 100.0f));

        Assert.True(changed);
        Assert.Empty(document.Strokes);
    }

    [Fact]
    public void Erase_未命中时不会修改文档且保持引用()
    {
        var document = new BoardDocument();

        Stroke stroke = StrokeTestFactory.CreateStroke(
            new Vector2(0.0f, 0.0f),
            new Vector2(10.0f, 0.0f));

        document.Strokes.Add(stroke);

        var eraser = new PixelStrokeEraser();
        bool changed = eraser.Erase(
            document,
            fromWorld: new Vector2(100.0f, 100.0f),
            toWorld: new Vector2(110.0f, 110.0f),
            radiusWorld: new Vector2(1.0f, 1.0f));

        Assert.False(changed);
        Assert.Single(document.Strokes);
        Assert.Same(stroke, document.Strokes[0]);
    }

    [Fact]
    public void Erase_只会影响命中的笔迹_未命中的保持引用()
    {
        var document = new BoardDocument();

        Stroke hit = StrokeTestFactory.CreateStroke(
            new Vector2(0.0f, 0.0f),
            new Vector2(10.0f, 0.0f));

        Stroke keep = StrokeTestFactory.CreateStroke(
            new Vector2(100.0f, 100.0f),
            new Vector2(110.0f, 100.0f));

        document.Strokes.Add(hit);
        document.Strokes.Add(keep);

        var eraser = new PixelStrokeEraser();
        bool changed = eraser.Erase(
            document,
            fromWorld: new Vector2(5.0f, -5.0f),
            toWorld: new Vector2(5.0f, 5.0f),
            radiusWorld: Vector2.Zero);

        Assert.True(changed);
        Assert.DoesNotContain(hit, document.Strokes);
        Assert.Contains(keep, document.Strokes);
        Assert.Same(keep, document.Strokes[^1]);
    }

}
