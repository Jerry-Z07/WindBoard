using System;
using System.Numerics;
using WindBoard.Board;
using WindBoard.Board.Items;
using WindBoard.Tests.Board.Editing;
using Xunit;

namespace WindBoard.Tests.Board;

public sealed class StrokeInkItemContractTests
{
    // Id：创建时生成且全局唯一（用于笔迹层条目标识）
    [Fact]
    public void Id_IsUnique_AcrossStrokes()
    {
        var a = new Stroke();
        var b = new Stroke();

        Assert.NotEqual(Guid.Empty, a.Id);
        Assert.NotEqual(Guid.Empty, b.Id);
        Assert.NotEqual(a.Id, b.Id);
    }

    // BoundsWorld：与 BoundsMin/BoundsMax 保持一致（无 Bounds 时为空矩形）
    [Fact]
    public void BoundsWorld_MatchesBoundsMinAndMax_WhenComputed()
    {
        var stroke = new Stroke { BaseSize = 0.0f, EnablePressure = false };
        stroke.Points.Add(new StrokePoint(new Vector2(1.0f, 2.0f), 1.0f));
        stroke.ExpandBounds(new Vector2(1.0f, 2.0f), 1.0f);

        Assert.True(stroke.HasBounds);
        Assert.Equal(stroke.BoundsMin.X, stroke.BoundsWorld.Left);
        Assert.Equal(stroke.BoundsMin.Y, stroke.BoundsWorld.Top);
        Assert.Equal(stroke.BoundsMax.X, stroke.BoundsWorld.Right);
        Assert.Equal(stroke.BoundsMax.Y, stroke.BoundsWorld.Bottom);
    }

    // BoundsWorld：无有效 Bounds 时返回空矩形
    [Fact]
    public void BoundsWorld_ReturnsEmptyRect_WhenBoundsMissing()
    {
        var stroke = new Stroke();

        Assert.False(stroke.HasBounds);
        Assert.Equal(0.0f, stroke.BoundsWorld.Width);
        Assert.Equal(0.0f, stroke.BoundsWorld.Height);
    }

    // Translate：接口契约与 Stroke.Translate 行为一致（Points 与 Bounds 同步平移）
    [Fact]
    public void Translate_MovesPointsAndBounds_ViaInterface()
    {
        Stroke stroke = StrokeTestFactory.CreateStroke(
            new Vector2(0.0f, 0.0f),
            new Vector2(10.0f, 0.0f));

        // 先记录平移前的 Bounds，避免断言中出现双重平移计算。
        Vector2 beforeMin = stroke.BoundsMin;
        Vector2 beforeMax = stroke.BoundsMax;

        IBoardInkItem item = stroke;
        item.Translate(new Vector2(3.0f, -4.0f));

        Assert.Equal(new Vector2(3.0f, -4.0f), stroke.Points[0].Position);
        Assert.Equal(new Vector2(13.0f, -4.0f), stroke.Points[1].Position);
        Assert.Equal(beforeMin.X + 3.0f, stroke.BoundsWorld.Left);
        Assert.Equal(beforeMax.Y - 4.0f, stroke.BoundsWorld.Bottom);
    }
}
