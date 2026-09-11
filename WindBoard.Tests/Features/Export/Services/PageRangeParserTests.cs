using System.Collections.Generic;
using WindBoard.Features.Export.Services;
using Xunit;

namespace WindBoard.Tests.Features.Export.Services;

public sealed class PageRangeParserTests
{
    // CA1861：常量数组提为 static readonly，避免断言路径重复分配。
    private static readonly int[] SinglePageIndex = { 0 };
    private static readonly int[] MixedPageIndices = { 0, 1, 2, 3, 4 };

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParse_ReturnsFalse_WhenTextEmpty(string? text)
    {
        bool ok = PageRangeParser.TryParse(text, pageCount: 3, out _, out _);
        Assert.False(ok);
    }

    [Fact]
    public void TryParse_ParsesSinglePage()
    {
        bool ok = PageRangeParser.TryParse("1", pageCount: 5, out List<int> indices, out string error);
        Assert.True(ok, error);
        Assert.Equal(SinglePageIndex, indices);
    }

    [Fact]
    public void TryParse_ParsesMixedTokens()
    {
        bool ok = PageRangeParser.TryParse("1, 3-5 , 2", pageCount: 6, out List<int> indices, out string error);
        Assert.True(ok, error);
        Assert.Equal(MixedPageIndices, indices);
    }

    [Fact]
    public void TryParse_ReturnsFalse_WhenOutOfRange()
    {
        bool ok = PageRangeParser.TryParse("1,7", pageCount: 6, out _, out _);
        Assert.False(ok);
    }

    [Fact]
    public void TryParse_ReturnsFalse_WhenReversedRange()
    {
        bool ok = PageRangeParser.TryParse("5-3", pageCount: 6, out _, out _);
        Assert.False(ok);
    }
}
