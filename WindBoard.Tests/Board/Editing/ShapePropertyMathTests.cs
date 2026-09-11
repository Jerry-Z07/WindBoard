using System;
using System.Numerics;
using WindBoard.Board.Editing;
using Xunit;

namespace WindBoard.Tests.Board.Editing;

/// <summary>
/// 形状属性面板字段换算用例（design F）：长度/角度 ↔ Start/End、宽/高 ↔ 对角点几何。
/// </summary>
public sealed class ShapePropertyMathTests
{
    [Fact]
    public void ReadLineLengthAngle_HorizontalLine_AngleIsZero()
    {
        (float length, float angle) = ShapePropertyMath.ReadLineLengthAngle(new Vector2(1.0f, 2.0f), new Vector2(6.0f, 2.0f));

        AssertEx.Equal(5.0f, length);
        AssertEx.Equal(0.0f, angle);
    }

    [Fact]
    public void ReadLineLengthAngle_DownwardLine_ClockwisePositive()
    {
        // 画布 Y 轴向下：Start→End 向下时角度为 +90°（顺时针为正，与画布视觉一致）。
        (float length, float angle) = ShapePropertyMath.ReadLineLengthAngle(new Vector2(0.0f, 0.0f), new Vector2(0.0f, 4.0f));

        AssertEx.Equal(4.0f, length);
        AssertEx.Equal(90.0f, angle);
    }

    [Fact]
    public void ReadLineLengthAngle_ReverseDirection_AngleIsNegative()
    {
        (_, float angle) = ShapePropertyMath.ReadLineLengthAngle(new Vector2(0.0f, 0.0f), new Vector2(-4.0f, 0.0f));

        AssertEx.Equal(180.0f, Math.Abs(angle));
    }

    [Fact]
    public void ComputeLineEndFromLengthAngle_AnchorsAtStart()
    {
        Vector2 end = ShapePropertyMath.ComputeLineEndFromLengthAngle(new Vector2(1.0f, 1.0f), 5.0, 0.0);

        AssertEx.Equal(6.0f, end.X);
        AssertEx.Equal(1.0f, end.Y);
    }

    [Fact]
    public void ComputeLineEndFromLengthAngle_Rotate90PointsDown()
    {
        Vector2 end = ShapePropertyMath.ComputeLineEndFromLengthAngle(new Vector2(2.0f, 2.0f), 3.0, 90.0);

        AssertEx.Equal(2.0f, end.X);
        AssertEx.Equal(5.0f, end.Y);
    }

    [Fact]
    public void LineLengthAngle_RoundTrip_RestoresGeometry()
    {
        Vector2 start = new(3.0f, 4.0f);
        Vector2 end = new(10.0f, 8.0f);

        (float length, float angle) = ShapePropertyMath.ReadLineLengthAngle(start, end);
        Vector2 rebuilt = ShapePropertyMath.ComputeLineEndFromLengthAngle(start, length, angle);

        AssertEx.Equal(end.X, rebuilt.X, 0.001f);
        AssertEx.Equal(end.Y, rebuilt.Y, 0.001f);
    }

    [Fact]
    public void ReadRectSize_NormalizesNegativeDrag()
    {
        // 负向拖拽（Start 在右下）：读侧按 Min/Max 规范化为正值（design A）。
        (float width, float height) = ShapePropertyMath.ReadRectSize(new Vector2(15.0f, 20.0f), new Vector2(5.0f, 8.0f));

        AssertEx.Equal(10.0f, width);
        AssertEx.Equal(12.0f, height);
    }

    [Fact]
    public void ComputeRectGeometryFromSize_AnchorsAtTopLeft()
    {
        // 原几何 Start 在右下（负向拖拽）：以 TopLeft 为锚点改宽高（design F）。
        (Vector2 newStart, Vector2 newEnd) = ShapePropertyMath.ComputeRectGeometryFromSize(
            new Vector2(15.0f, 20.0f), new Vector2(5.0f, 8.0f), 30.0, 24.0);

        AssertEx.Equal(5.0f, newStart.X);
        AssertEx.Equal(8.0f, newStart.Y);
        AssertEx.Equal(35.0f, newEnd.X);
        AssertEx.Equal(32.0f, newEnd.Y);
    }

    [Fact]
    public void ComputeRectGeometryFromSize_PositiveDrag_KeepsTopLeftAnchor()
    {
        (Vector2 newStart, Vector2 newEnd) = ShapePropertyMath.ComputeRectGeometryFromSize(
            new Vector2(5.0f, 8.0f), new Vector2(15.0f, 20.0f), 12.0, 6.0);

        // 锚点 TopLeft 不变，End 按 TopLeft + (宽, 高) 重算。
        AssertEx.Equal(5.0f, newStart.X);
        AssertEx.Equal(8.0f, newStart.Y);
        AssertEx.Equal(17.0f, newEnd.X);
        AssertEx.Equal(14.0f, newEnd.Y);
    }
}
