using System.Numerics;
using WindBoard.Board;
using Xunit;

namespace WindBoard.Tests.Board;

public sealed class StrokeTests
{
    [Fact]
    public void ExpandBounds_首次调用后会生成有效包围盒()
    {
        var stroke = new Stroke
        {
            BaseSize = 10.0f,
            EnablePressure = true,
        };

        Assert.False(stroke.HasBounds);

        stroke.ExpandBounds(new Vector2(0.0f, 0.0f), normalizedPressure: 1.0f);

        Assert.True(stroke.HasBounds);
        AssertEx.Equal(new Vector2(-5.0f, -5.0f), stroke.BoundsMin);
        AssertEx.Equal(new Vector2(5.0f, 5.0f), stroke.BoundsMax);
    }

    [Fact]
    public void ExpandBounds_压力会影响笔迹宽度并被钳制()
    {
        var stroke = new Stroke
        {
            BaseSize = 10.0f,
            EnablePressure = true,
        };

        // 压力会被钳制到 [0.1, 1.0]，因此 halfWidth = max(0.25, 10 * 0.1 / 2) = 0.5
        stroke.ExpandBounds(new Vector2(0.0f, 0.0f), normalizedPressure: 0.0f);
        AssertEx.Equal(new Vector2(-0.5f, -0.5f), stroke.BoundsMin);
        AssertEx.Equal(new Vector2(0.5f, 0.5f), stroke.BoundsMax);

        // 压力 1.0 时 halfWidth = 5.0，包围盒应扩展到包含第二个点的 padding。
        stroke.ExpandBounds(new Vector2(1.0f, 1.0f), normalizedPressure: 1.0f);
        AssertEx.Equal(new Vector2(-4.0f, -4.0f), stroke.BoundsMin);
        AssertEx.Equal(new Vector2(6.0f, 6.0f), stroke.BoundsMax);
    }

    [Fact]
    public void ExpandBounds_关闭压力后宽度不随压力变化()
    {
        var stroke = new Stroke
        {
            BaseSize = 8.0f,
            EnablePressure = false,
        };

        // EnablePressure=false 时 widthFactor 固定为 1.0，halfWidth = 4.0
        stroke.ExpandBounds(new Vector2(2.0f, -3.0f), normalizedPressure: 0.1f);
        AssertEx.Equal(new Vector2(-2.0f, -7.0f), stroke.BoundsMin);
        AssertEx.Equal(new Vector2(6.0f, 1.0f), stroke.BoundsMax);
    }
}

