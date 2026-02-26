using WindBoard.Features.Import.Services;

namespace WindBoard.Tests.Features.Import;

public sealed class ImportFileTypeResolverTests
{
    [Theory]
    [InlineData("a.WBIX", "Wbix")]
    [InlineData("b.wbi", "Wbi")]
    [InlineData("c.png", "Image")]
    [InlineData("d.mp3", "Audio")]
    [InlineData("e.webm", "Video")]
    [InlineData("f.md", "Text")]
    [InlineData("g.url", "UrlShortcut")]
    [InlineData("h.pdf", "Other")]
    public void Resolve_ByExtension_ReturnsExpectedKind(string fileName, string expected)
    {
        ImportFileContentKind kind = ImportFileTypeResolver.Resolve(fileName);
        Assert.Equal(expected, kind.ToString());
    }
}
