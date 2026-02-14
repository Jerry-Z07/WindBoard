using WindBoard.Importing;

namespace WindBoard.Tests.Importing;

public sealed class ImportUrlNormalizerTests
{
    [Fact]
    public void TryNormalizeHttpUrl_NoScheme_AddsHttps()
    {
        Assert.True(ImportUrlNormalizer.TryNormalizeHttpUrl("example.com", out string normalized));
        Assert.Equal("https://example.com/", normalized);
    }

    [Fact]
    public void TryNormalizeHttpUrl_HttpScheme_Kept()
    {
        Assert.True(ImportUrlNormalizer.TryNormalizeHttpUrl("http://example.com", out string normalized));
        Assert.Equal("http://example.com/", normalized);
    }

    [Fact]
    public void TryNormalizeHttpUrl_RejectsNonHttpSchemes()
    {
        Assert.False(ImportUrlNormalizer.TryNormalizeHttpUrl("ftp://example.com", out _));
    }

    [Fact]
    public void ParseAndNormalizeLinkLines_DedupesAndSkipsInvalid()
    {
        string input = "example.com\n\nhttps://example.com\nftp://bad\n  http://a.com  ";
        var urls = ImportUrlNormalizer.ParseAndNormalizeLinkLines(input);

        Assert.Equal(2, urls.Count);
        Assert.Contains("https://example.com/", urls);
        Assert.Contains("http://a.com/", urls);
    }
}

