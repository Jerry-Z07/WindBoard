using System.Numerics;
using WindBoard.Board;
using WindBoard.Board.Editing;
using WindBoard.Board.Items;
using Vortice.Mathematics;
using Xunit;

namespace WindBoard.Tests.Board.Editing;

public sealed class WholeStrokeEraserTests
{
    // 命中时会删除整条笔迹
    [Fact]
    public void Erase_DeletesWholeStroke_WhenHit()
    {
        var document = new BoardDocument();

        var hitStroke = StrokeTestFactory.CreateStroke(
            new Vector2(0.0f, 0.0f),
            new Vector2(10.0f, 0.0f));

        var keepStroke = StrokeTestFactory.CreateStroke(
            new Vector2(100.0f, 100.0f),
            new Vector2(110.0f, 100.0f));

        document.InkItems.Add(hitStroke);
        document.InkItems.Add(keepStroke);

        var eraser = new WholeStrokeEraser();
        bool changed = eraser.Erase(
            document,
            fromWorld: new Vector2(5.0f, -5.0f),
            toWorld: new Vector2(5.0f, 5.0f),
            radiusWorld: Vector2.Zero);

        Assert.True(changed);
        Assert.Single(document.InkItems);
        Assert.Same(keepStroke, document.InkItems[0]);
    }

    // 未命中时不会修改文档
    [Fact]
    public void Erase_DoesNotModifyDocument_WhenMissed()
    {
        var document = new BoardDocument();

        var stroke = StrokeTestFactory.CreateStroke(
            new Vector2(0.0f, 0.0f),
            new Vector2(10.0f, 0.0f));

        document.InkItems.Add(stroke);

        var eraser = new WholeStrokeEraser();
        bool changed = eraser.Erase(
            document,
            fromWorld: new Vector2(100.0f, 100.0f),
            toWorld: new Vector2(110.0f, 110.0f),
            radiusWorld: new Vector2(1.0f, 1.0f));

        Assert.False(changed);
        Assert.Single(document.InkItems);
        Assert.Same(stroke, document.InkItems[0]);
    }

    // 擦除路由按条目类型分流：非 Stroke 条目（不可分割）命中即整笔删除
    [Fact]
    public void Erase_RemovesNonStrokeItem_WhenHit()
    {
        var document = new BoardDocument();

        var fake = new TestInkItem(Rect.FromLTRB(0.0f, 0.0f, 10.0f, 10.0f));
        document.InkItems.Add(fake);

        var eraser = new WholeStrokeEraser();
        bool changed = eraser.Erase(
            document,
            fromWorld: new Vector2(5.0f, -5.0f),
            toWorld: new Vector2(5.0f, 5.0f),
            radiusWorld: Vector2.Zero);

        Assert.True(changed);
        Assert.Empty(document.InkItems);
    }

}
