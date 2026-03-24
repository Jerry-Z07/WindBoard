using System.Linq;
using WindBoard.Updates;
using Xunit;

namespace WindBoard.Tests.Updates;

public sealed class MarkdownViewDocumentBuilderTests
{
    [Fact]
    public void Build_ParsesHeadingListParagraphAndCodeBlock()
    {
        const string markdown = """
            ## 更新内容

            - feat: 支持 **粗体**
            - docs: 打开 [发布页](https://example.com)

            `inline`

            ```txt
            code
            ```
            """;

        MarkdownViewDocument document = MarkdownViewDocumentBuilder.Build(markdown);

        Assert.Equal(4, document.Blocks.Count);

        var heading = Assert.IsType<MarkdownHeadingBlock>(document.Blocks[0]);
        Assert.Equal(2, heading.Level);
        Assert.Equal("更新内容", heading.Text);

        var list = Assert.IsType<MarkdownListBlock>(document.Blocks[1]);
        Assert.False(list.IsOrdered);
        Assert.Equal(2, list.Items.Count);

        var firstItemParagraph = Assert.IsType<MarkdownParagraphBlock>(Assert.Single(list.Items[0].Blocks));
        Assert.Collection(
            firstItemParagraph.Inlines,
            inline => Assert.Equal("feat: 支持 ", Assert.IsType<MarkdownTextPart>(inline).Text),
            inline =>
            {
                var strong = Assert.IsType<MarkdownStrongPart>(inline);
                Assert.Collection(
                    strong.Children,
                    child => Assert.Equal("粗体", Assert.IsType<MarkdownTextPart>(child).Text));
            });

        var secondItemParagraph = Assert.IsType<MarkdownParagraphBlock>(Assert.Single(list.Items[1].Blocks));
        MarkdownLinkPart link = secondItemParagraph.Inlines.OfType<MarkdownLinkPart>().Single();
        Assert.Equal("发布页", link.Text);
        Assert.Equal("https://example.com", link.Url);

        var paragraph = Assert.IsType<MarkdownParagraphBlock>(document.Blocks[2]);
        MarkdownCodePart inlineCode = Assert.IsType<MarkdownCodePart>(Assert.Single(paragraph.Inlines));
        Assert.Equal("inline", inlineCode.Text);

        var codeBlock = Assert.IsType<MarkdownCodeBlock>(document.Blocks[3]);
        Assert.Equal("txt", codeBlock.Language);
        Assert.Equal("code", codeBlock.Code);
    }

    [Fact]
    public void Build_BlankMarkdown_ReturnsEmptyDocument()
    {
        MarkdownViewDocument document = MarkdownViewDocumentBuilder.Build("   \r\n  ");

        Assert.Empty(document.Blocks);
    }
}
