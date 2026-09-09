using System.Linq;
using System.Numerics;
using WindBoard.Board;
using WindBoard.Board.Editing;
using Xunit;

namespace WindBoard.Tests.Board.Editing;

public sealed class PixelStrokeEraserTests
{
    // 命中中间时会把笔迹分割为两段
    [Fact]
    public void Erase_SplitsStrokeIntoTwo_WhenHitInMiddle()
    {
        var document = new BoardDocument();

        Stroke stroke = StrokeTestFactory.CreateStroke(
            new Vector2(0.0f, 0.0f),
            new Vector2(10.0f, 0.0f));

        document.InkItems.Add(stroke);

        var eraser = new PixelStrokeEraser();
        bool changed = eraser.Erase(
            document,
            fromWorld: new Vector2(5.0f, -5.0f),
            toWorld: new Vector2(5.0f, 5.0f),
            radiusWorld: Vector2.Zero);

        Assert.True(changed);
        Assert.Equal(2, document.InkItems.Count);
        Assert.DoesNotContain(stroke, document.InkItems);

        float leftMaxX = ((Stroke)document.InkItems[0]).Points.Max(p => p.Position.X);
        float rightMinX = ((Stroke)document.InkItems[1]).Points.Min(p => p.Position.X);
        Assert.True(leftMaxX < 5.0f);
        Assert.True(rightMinX > 5.0f);
    }

    // 只命中一端时会截断笔迹
    [Fact]
    public void Erase_TruncatesStroke_WhenHitAtEnd()
    {
        var document = new BoardDocument();

        Stroke stroke = StrokeTestFactory.CreateStroke(
            new Vector2(0.0f, 0.0f),
            new Vector2(10.0f, 0.0f));

        document.InkItems.Add(stroke);

        var eraser = new PixelStrokeEraser();
        bool changed = eraser.Erase(
            document,
            fromWorld: new Vector2(0.0f, -5.0f),
            toWorld: new Vector2(0.0f, 5.0f),
            radiusWorld: Vector2.Zero);

        Assert.True(changed);
        Assert.Single(document.InkItems);
        Assert.NotSame(stroke, document.InkItems[0]);

        float minX = ((Stroke)document.InkItems[0]).Points.Min(p => p.Position.X);
        Assert.True(minX > 0.0f);
    }

    // 覆盖整条笔迹时会删除
    [Fact]
    public void Erase_DeletesStroke_WhenCoveredCompletely()
    {
        var document = new BoardDocument();

        Stroke stroke = StrokeTestFactory.CreateStroke(
            new Vector2(0.0f, 0.0f),
            new Vector2(10.0f, 0.0f));

        document.InkItems.Add(stroke);

        var eraser = new PixelStrokeEraser();
        bool changed = eraser.Erase(
            document,
            fromWorld: new Vector2(5.0f, -5.0f),
            toWorld: new Vector2(5.0f, 5.0f),
            radiusWorld: new Vector2(100.0f, 100.0f));

        Assert.True(changed);
        Assert.Empty(document.InkItems);
    }

    // 未命中时不会修改文档且保持引用
    [Fact]
    public void Erase_NoChangeAndKeepsReference_WhenMissed()
    {
        var document = new BoardDocument();

        Stroke stroke = StrokeTestFactory.CreateStroke(
            new Vector2(0.0f, 0.0f),
            new Vector2(10.0f, 0.0f));

        document.InkItems.Add(stroke);

        var eraser = new PixelStrokeEraser();
        bool changed = eraser.Erase(
            document,
            fromWorld: new Vector2(100.0f, 100.0f),
            toWorld: new Vector2(110.0f, 110.0f),
            radiusWorld: new Vector2(1.0f, 1.0f));

        Assert.False(changed);
        Assert.Single(document.InkItems);
        Assert.Same(stroke, document.InkItems[0]);
    }

    // 只会影响命中的笔迹，未命中的保持引用
    [Fact]
    public void Erase_OnlyAffectsHitStrokes_AndKeepsOthersReference()
    {
        var document = new BoardDocument();

        Stroke hit = StrokeTestFactory.CreateStroke(
            new Vector2(0.0f, 0.0f),
            new Vector2(10.0f, 0.0f));

        Stroke keep = StrokeTestFactory.CreateStroke(
            new Vector2(100.0f, 100.0f),
            new Vector2(110.0f, 100.0f));

        document.InkItems.Add(hit);
        document.InkItems.Add(keep);

        var eraser = new PixelStrokeEraser();
        bool changed = eraser.Erase(
            document,
            fromWorld: new Vector2(5.0f, -5.0f),
            toWorld: new Vector2(5.0f, 5.0f),
            radiusWorld: Vector2.Zero);

        Assert.True(changed);
        Assert.DoesNotContain(hit, document.InkItems);
        Assert.Contains(keep, document.InkItems);
        Assert.Same(keep, document.InkItems[^1]);
    }

    // 擦除路由按条目类型分流：非 Stroke 条目（不可分割）命中即整笔删除，不做像素分割
    [Fact]
    public void Erase_RemovesNonStrokeItemWhole_WhenHit()
    {
        var document = new BoardDocument();

        var fake = new TestInkItem(Vortice.Mathematics.Rect.FromLTRB(0.0f, 0.0f, 10.0f, 10.0f));
        document.InkItems.Add(fake);

        var eraser = new PixelStrokeEraser();
        bool changed = eraser.Erase(
            document,
            fromWorld: new Vector2(5.0f, -5.0f),
            toWorld: new Vector2(5.0f, 5.0f),
            radiusWorld: Vector2.Zero);

        Assert.True(changed);
        Assert.Empty(document.InkItems);
    }

    // 非 Stroke 条目未命中时保持原引用不变
    [Fact]
    public void Erase_KeepsNonStrokeItem_WhenMissed()
    {
        var document = new BoardDocument();

        var fake = new TestInkItem(Vortice.Mathematics.Rect.FromLTRB(0.0f, 0.0f, 10.0f, 10.0f));
        document.InkItems.Add(fake);

        var eraser = new PixelStrokeEraser();
        bool changed = eraser.Erase(
            document,
            fromWorld: new Vector2(100.0f, 100.0f),
            toWorld: new Vector2(110.0f, 110.0f),
            radiusWorld: Vector2.Zero);

        Assert.False(changed);
        Assert.Single(document.InkItems);
        Assert.Same(fake, document.InkItems[0]);
    }

}
