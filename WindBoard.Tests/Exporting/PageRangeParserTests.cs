using System.Collections.Generic;
using WindBoard.Exporting;
using Xunit;

namespace WindBoard.Tests.Exporting;

public sealed class PageRangeParserTests
{
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
        Assert.Equal(new[] { 0 }, indices);
    }

    [Fact]
    public void TryParse_ParsesMixedTokens()
    {
        bool ok = PageRangeParser.TryParse("1, 3-5 , 2", pageCount: 6, out List<int> indices, out string error);
        Assert.True(ok, error);
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, indices);
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

