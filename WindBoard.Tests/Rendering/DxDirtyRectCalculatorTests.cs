using WindBoard.Rendering;
using Vortice.Mathematics;
using Xunit;

namespace WindBoard.Tests.Rendering;

public sealed class DxDirtyRectCalculatorTests
{
    // 无偏移返回空数组
    [Fact]
    public void CreatePanDirtyRectsPixels_ReturnsEmptyArray_WhenNoOffset()
    {
        RectI[] rects = DxDirtyRectCalculator.CreatePanDirtyRectsPixels(width: 100, height: 50, dxPixels: 0, dyPixels: 0);
        Assert.Empty(rects);
    }

    // 水平偏移会生成竖向脏区
    [Fact]
    public void CreatePanDirtyRectsPixels_CreatesVerticalDirtyRect_WhenHorizontalOffset()
    {
        RectI[] rects = DxDirtyRectCalculator.CreatePanDirtyRectsPixels(width: 100, height: 50, dxPixels: 10, dyPixels: 0);

        Assert.Single(rects);
        Assert.Equal(new RectI(0, 0, 10, 50), rects[0]);
    }

    // 负水平偏移会生成右侧竖向脏区
    [Fact]
    public void CreatePanDirtyRectsPixels_CreatesRightVerticalDirtyRect_WhenNegativeHorizontalOffset()
    {
        RectI[] rects = DxDirtyRectCalculator.CreatePanDirtyRectsPixels(width: 100, height: 50, dxPixels: -10, dyPixels: 0);

        Assert.Single(rects);
        Assert.Equal(new RectI(90, 0, 10, 50), rects[0]);
    }

    // 垂直偏移会生成横向脏区
    [Fact]
    public void CreatePanDirtyRectsPixels_CreatesHorizontalDirtyRect_WhenVerticalOffset()
    {
        RectI[] rects = DxDirtyRectCalculator.CreatePanDirtyRectsPixels(width: 100, height: 50, dxPixels: 0, dyPixels: 5);

        Assert.Single(rects);
        Assert.Equal(new RectI(0, 0, 100, 5), rects[0]);
    }

    // 水平加垂直偏移会返回两个脏区
    [Fact]
    public void CreatePanDirtyRectsPixels_ReturnsTwoDirtyRects_WhenHorizontalAndVerticalOffset()
    {
        RectI[] rects = DxDirtyRectCalculator.CreatePanDirtyRectsPixels(width: 100, height: 50, dxPixels: 10, dyPixels: 5);

        Assert.Equal(2, rects.Length);
        Assert.Equal(new RectI(0, 0, 10, 50), rects[0]);
        Assert.Equal(new RectI(0, 0, 100, 5), rects[1]);
    }
}
