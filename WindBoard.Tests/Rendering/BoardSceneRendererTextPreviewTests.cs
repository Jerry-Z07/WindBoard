using WindBoard.Rendering.Board;
using Xunit;

namespace WindBoard.Tests.Rendering;

public sealed class BoardSceneRendererTextPreviewTests
{
    [Fact]
    public void BuildTextElementPreview_WhenTextLongerThanOldLimit_KeepsVisibleContentBeyond160Chars()
    {
        string input = new string('a', 220);

        string preview = BoardSceneRenderer.BuildTextElementPreview(input);

        Assert.Equal(input, preview);
    }

    [Fact]
    public void BuildTextElementPreview_WhenTextExceedsRendererCap_AppendsEllipsis()
    {
        string input = new string('b', 5000);

        string preview = BoardSceneRenderer.BuildTextElementPreview(input);

        Assert.Equal(4097, preview.Length);
        Assert.EndsWith("…", preview);
        Assert.Equal(new string('b', 4096), preview[..^1]);
    }
}
