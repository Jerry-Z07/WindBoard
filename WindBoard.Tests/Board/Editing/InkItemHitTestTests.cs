using System;
using System.Numerics;
using WindBoard.Board;
using WindBoard.Board.Editing;
using WindBoard.Board.Items;
using Vortice.Mathematics;
using Xunit;

namespace WindBoard.Tests.Board.Editing;

public sealed class InkItemHitTestTests
{
    // 擦除命中经接口分发到 Stroke 的线段距离算法。
    [Fact]
    public void IsInkItemHitByEraserSegment_HitsStroke_WhenEraserCrossesSegment()
    {
        Stroke stroke = StrokeTestFactory.CreateStroke(
            new Vector2(0.0f, 0.0f),
            new Vector2(10.0f, 0.0f));

        bool hit = InkItemHitTest.IsInkItemHitByEraserSegment(
            stroke,
            eraserFromWorld: new Vector2(5.0f, -5.0f),
            eraserToWorld: new Vector2(5.0f, 5.0f),
            eraserRadiusWorld: Vector2.Zero);

        Assert.True(hit);
    }

    [Fact]
    public void IsInkItemHitByEraserSegment_HitsNonStrokeItem_WhenBoundsOverlapEraserAabb()
    {
        var item = new TestInkItem(Rect.FromLTRB(0.0f, 0.0f, 10.0f, 10.0f));

        bool hit = InkItemHitTest.IsInkItemHitByEraserSegment(
            item,
            eraserFromWorld: new Vector2(5.0f, -5.0f),
            eraserToWorld: new Vector2(5.0f, 5.0f),
            eraserRadiusWorld: Vector2.Zero);

        Assert.True(hit);
    }

    [Fact]
    public void IsInkItemHitByEraserSegment_MissesNonStrokeItem_WhenBoundsFarAway()
    {
        var item = new TestInkItem(Rect.FromLTRB(0.0f, 0.0f, 10.0f, 10.0f));

        bool hit = InkItemHitTest.IsInkItemHitByEraserSegment(
            item,
            eraserFromWorld: new Vector2(50.0f, 50.0f),
            eraserToWorld: new Vector2(60.0f, 50.0f),
            eraserRadiusWorld: Vector2.Zero);

        Assert.False(hit);
    }

    [Fact]
    public void IsInkItemHitByEraserSegment_MissesNonStrokeItem_WhenBoundsEmpty()
    {
        var item = new TestInkItem(Rect.Empty);

        bool hit = InkItemHitTest.IsInkItemHitByEraserSegment(
            item,
            eraserFromWorld: new Vector2(0.0f, 0.0f),
            eraserToWorld: new Vector2(10.0f, 0.0f),
            eraserRadiusWorld: Vector2.Zero);

        Assert.False(hit);
    }

    // null 条目应抛出 ArgumentNullException（防御外部误用）
    [Fact]
    public void IsInkItemHitByEraserSegment_Throws_WhenItemNull()
    {
        Assert.Throws<ArgumentNullException>(() => InkItemHitTest.IsInkItemHitByEraserSegment(
            null!,
            eraserFromWorld: Vector2.Zero,
            eraserToWorld: Vector2.Zero,
            eraserRadiusWorld: Vector2.Zero));
    }
}
