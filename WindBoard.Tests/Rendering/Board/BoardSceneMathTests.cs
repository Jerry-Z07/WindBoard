using System.Numerics;
using WindBoard.Board;
using WindBoard.Rendering.Board;
using Xunit;

namespace WindBoard.Tests.Rendering.Board;

public sealed class BoardSceneMathTests
{
    [Theory]
    [InlineData(1.0f, 40.0f)]
    [InlineData(0.1f, 320.0f)]
    [InlineData(10.0f, 5.0f)]
    public void GetAdaptiveGridStepWorld_会根据缩放自适应(float zoom, float expectedStep)
    {
        float step = BoardSceneMath.GetAdaptiveGridStepWorld(zoom);
        AssertEx.Equal(expectedStep, step, tolerance: 0.0001f);
    }

    [Theory]
    [InlineData(-1.0f, 0.1f)]
    [InlineData(0.0f, 0.1f)]
    [InlineData(0.05f, 0.1f)]
    [InlineData(0.1f, 0.1f)]
    [InlineData(0.5f, 0.5f)]
    [InlineData(2.0f, 1.0f)]
    public void GetStrokeWidthFactor_会钳制到范围(float normalizedPressure, float expectedFactor)
    {
        float factor = BoardSceneMath.GetStrokeWidthFactor(normalizedPressure);
        AssertEx.Equal(expectedFactor, factor, tolerance: 0.000001f);
    }

    [Fact]
    public void IntersectsAabb_重叠返回True_分离返回False()
    {
        Assert.True(BoardSceneMath.IntersectsAabb(
            new Vector2(0.0f, 0.0f),
            new Vector2(1.0f, 1.0f),
            new Vector2(0.5f, 0.5f),
            new Vector2(2.0f, 2.0f)));

        Assert.False(BoardSceneMath.IntersectsAabb(
            new Vector2(0.0f, 0.0f),
            new Vector2(1.0f, 1.0f),
            new Vector2(2.0f, 2.0f),
            new Vector2(3.0f, 3.0f)));
    }

    [Fact]
    public void IsStrokeVisible_无点不可见_无Bounds则默认可见()
    {
        var stroke = new Stroke();
        Assert.False(BoardSceneMath.IsStrokeVisible(stroke, new Vector2(-1.0f, -1.0f), new Vector2(1.0f, 1.0f)));

        // 只塞 Points，不调用 ExpandBounds => HasBounds=false
        stroke.Points.Add(new StrokePoint(new Vector2(100.0f, 100.0f), 1.0f));
        Assert.False(stroke.HasBounds);
        Assert.True(BoardSceneMath.IsStrokeVisible(stroke, new Vector2(-1.0f, -1.0f), new Vector2(1.0f, 1.0f)));
    }

    [Fact]
    public void IsStrokeVisible_有Bounds时按Aabb判断()
    {
        var stroke = new Stroke
        {
            BaseSize = 10.0f,
            EnablePressure = false,
        };
        stroke.Points.Add(new StrokePoint(new Vector2(0.0f, 0.0f), 1.0f));
        stroke.ExpandBounds(new Vector2(0.0f, 0.0f), 1.0f);

        Assert.True(BoardSceneMath.IsStrokeVisible(stroke, new Vector2(-10.0f, -10.0f), new Vector2(10.0f, 10.0f)));
        Assert.False(BoardSceneMath.IsStrokeVisible(stroke, new Vector2(100.0f, 100.0f), new Vector2(200.0f, 200.0f)));
    }
}

