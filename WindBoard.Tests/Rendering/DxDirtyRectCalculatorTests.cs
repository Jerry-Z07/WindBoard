using WindBoard.Rendering;
using Vortice.Mathematics;
using Xunit;

namespace WindBoard.Tests.Rendering;

public sealed class DxDirtyRectCalculatorTests
{
    [Fact]
    public void CreatePanDirtyRectsPixels_无偏移返回空数组()
    {
        RectI[] rects = DxDirtyRectCalculator.CreatePanDirtyRectsPixels(width: 100, height: 50, dxPixels: 0, dyPixels: 0);
        Assert.Empty(rects);
    }

    [Fact]
    public void CreatePanDirtyRectsPixels_水平偏移会生成竖向脏区()
    {
        RectI[] rects = DxDirtyRectCalculator.CreatePanDirtyRectsPixels(width: 100, height: 50, dxPixels: 10, dyPixels: 0);

        Assert.Single(rects);
        Assert.Equal(new RectI(0, 0, 10, 50), rects[0]);
    }

    [Fact]
    public void CreatePanDirtyRectsPixels_负水平偏移会生成右侧竖向脏区()
    {
        RectI[] rects = DxDirtyRectCalculator.CreatePanDirtyRectsPixels(width: 100, height: 50, dxPixels: -10, dyPixels: 0);

        Assert.Single(rects);
        Assert.Equal(new RectI(90, 0, 10, 50), rects[0]);
    }

    [Fact]
    public void CreatePanDirtyRectsPixels_垂直偏移会生成横向脏区()
    {
        RectI[] rects = DxDirtyRectCalculator.CreatePanDirtyRectsPixels(width: 100, height: 50, dxPixels: 0, dyPixels: 5);

        Assert.Single(rects);
        Assert.Equal(new RectI(0, 0, 100, 5), rects[0]);
    }

    [Fact]
    public void CreatePanDirtyRectsPixels_水平加垂直偏移会返回两个脏区()
    {
        RectI[] rects = DxDirtyRectCalculator.CreatePanDirtyRectsPixels(width: 100, height: 50, dxPixels: 10, dyPixels: 5);

        Assert.Equal(2, rects.Length);
        Assert.Equal(new RectI(0, 0, 10, 50), rects[0]);
        Assert.Equal(new RectI(0, 0, 100, 5), rects[1]);
    }
}

