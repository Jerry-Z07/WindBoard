using WindBoard.Settings;
using WindBoard.Updates;

namespace WindBoard.Tests.Updates;

public sealed class DownloadSourceUrlRewriterTests
{
    [Fact]
    public void Rewrite_Should_Return_Original_For_Github_Source()
    {
        const string original = "https://github.com/Jerry-Z07/WindBoard/releases/latest/download/latest.json";
        string rewritten = DownloadSourceUrlRewriter.Rewrite(original, DownloadSourceId.Github);
        Assert.Equal(original, rewritten);
    }

    [Fact]
    public void Rewrite_Should_Prefix_Mirror_For_Github_Url()
    {
        const string original = "https://github.com/Jerry-Z07/WindBoard/releases/download/v1.2.3/file.exe";

        string a = DownloadSourceUrlRewriter.Rewrite(original, DownloadSourceId.GhProxy);
        Assert.Equal("https://gh-proxy.top/" + original, a);

        string b = DownloadSourceUrlRewriter.Rewrite(original, DownloadSourceId.Felicity);
        Assert.Equal("https://gh.felicity.ac.cn/" + original, b);

        string c = DownloadSourceUrlRewriter.Rewrite(original, DownloadSourceId.ZeroSeven);
        Assert.Equal("https://ghm.078465.xyz/" + original, c);
    }

    [Fact]
    public void Rewrite_Should_Not_Change_Non_Github_Url()
    {
        const string original = "https://example.com/file.bin";
        string rewritten = DownloadSourceUrlRewriter.Rewrite(original, DownloadSourceId.GhProxy);
        Assert.Equal(original, rewritten);
    }

    [Fact]
    public void BuildFailoverOrder_Should_Start_With_Preferred_And_End_With_Github()
    {
        IReadOnlyList<DownloadSourceId> order = DownloadSourceUrlRewriter.BuildFailoverOrder(DownloadSourceId.Felicity);
        Assert.Equal(DownloadSourceId.Felicity, order[0]);
        Assert.Equal(DownloadSourceId.Github, order[^1]);
    }
}

