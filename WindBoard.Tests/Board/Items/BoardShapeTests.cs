using System.Numerics;
using WindBoard.Board.Items;
using Vortice.Mathematics;
using Xunit;

namespace WindBoard.Tests.Board.Items;

public sealed class BoardShapeTests
{
    [Fact]
    public void SetGeometry_RecalculatesBounds_WithHalfWidthPadding()
    {
        // 包围盒 = AABB(Start, End) 外扩 Width/2：点 (0,0)-(10,0)、Width=6 → (-3,-3)-(13,3)，
        // 与 Stroke 的半宽语义一致。
        var shape = new BoardShape(BoardShapeKind.Line)
        {
            Width = 6.0f,
        };

        shape.SetGeometry(new Vector2(0.0f, 0.0f), new Vector2(10.0f, 0.0f));

        Rect bounds = shape.BoundsWorld;
        AssertEx.Equal(-3.0f, bounds.Left);
        AssertEx.Equal(-3.0f, bounds.Top);
        AssertEx.Equal(13.0f, bounds.Right);
        AssertEx.Equal(3.0f, bounds.Bottom);
    }

    [Fact]
    public void SetGeometry_DiagonalEndpoints_BoundsCoversAabb()
    {
        // Start/End 反向（End 在左上）：包围盒按 AABB 覆盖，与方向无关。
        var shape = new BoardShape(BoardShapeKind.Rectangle)
        {
            Width = 4.0f,
        };

        shape.SetGeometry(new Vector2(10.0f, 10.0f), new Vector2(0.0f, 0.0f));

        Rect bounds = shape.BoundsWorld;
        AssertEx.Equal(-2.0f, bounds.Left);
        AssertEx.Equal(-2.0f, bounds.Top);
        AssertEx.Equal(12.0f, bounds.Right);
        AssertEx.Equal(12.0f, bounds.Bottom);
    }

    [Fact]
    public void WidthChange_RecalculatesBounds()
    {
        var shape = new BoardShape(BoardShapeKind.Ellipse);
        shape.SetGeometry(new Vector2(0.0f, 0.0f), new Vector2(10.0f, 0.0f));
        shape.Width = 10.0f;

        Rect bounds = shape.BoundsWorld;
        AssertEx.Equal(-5.0f, bounds.Left);
        AssertEx.Equal(-5.0f, bounds.Top);
        AssertEx.Equal(15.0f, bounds.Right);
        AssertEx.Equal(5.0f, bounds.Bottom);
    }

    [Fact]
    public void Translate_MovesStartEndAndBounds()
    {
        var shape = new BoardShape(BoardShapeKind.Arrow)
        {
            Width = 2.0f,
        };
        shape.SetGeometry(new Vector2(0.0f, 0.0f), new Vector2(10.0f, 0.0f));

        shape.Translate(new Vector2(5.0f, 10.0f));

        AssertEx.Equal(new Vector2(5.0f, 10.0f), shape.Start);
        AssertEx.Equal(new Vector2(15.0f, 10.0f), shape.End);

        Rect bounds = shape.BoundsWorld;
        AssertEx.Equal(4.0f, bounds.Left);
        AssertEx.Equal(9.0f, bounds.Top);
        AssertEx.Equal(16.0f, bounds.Right);
        AssertEx.Equal(11.0f, bounds.Bottom);
    }

    [Fact]
    public void Translate_ZeroDelta_IsNoOp()
    {
        var shape = new BoardShape(BoardShapeKind.Line);
        shape.SetGeometry(new Vector2(1.0f, 2.0f), new Vector2(3.0f, 4.0f));

        shape.Translate(Vector2.Zero);

        AssertEx.Equal(new Vector2(1.0f, 2.0f), shape.Start);
        AssertEx.Equal(new Vector2(3.0f, 4.0f), shape.End);
    }
}
