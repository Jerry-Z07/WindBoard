using System.Xml.Linq;
using WindBoard.Tests;
using Xunit;

namespace WindBoard.Tests.Publishing;

public sealed class WindBoardProjectPublishConfigurationTests
{
    [Fact]
    public void ReleaseWorkflow_DisablesReadyToRun_ForPortablePublishes()
    {
        DirectoryInfo? repoRoot = RepoRootLocator.Find();
        Assert.NotNull(repoRoot);

        string workflowFilePath = Path.Combine(repoRoot!.FullName, ".github", "workflows", "release.yml");
        Assert.True(File.Exists(workflowFilePath), $"未找到发布工作流文件：{workflowFilePath}");

        string text = File.ReadAllText(workflowFilePath);
        const string readyToRunDisableFlag = "-p:PublishReadyToRun=false";
        int count = CountOccurrences(text, readyToRunDisableFlag);

        Assert.Equal(2, count);
    }

    [Fact]
    public void InstallerScript_UsesSharedAssetsFontSource()
    {
        DirectoryInfo? repoRoot = RepoRootLocator.Find();
        Assert.NotNull(repoRoot);

        string installerFilePath = Path.Combine(repoRoot!.FullName, "installer", "WindBoard.iss");
        Assert.True(File.Exists(installerFilePath), $"未找到安装脚本文件：{installerFilePath}");

        string text = File.ReadAllText(installerFilePath);
        Assert.Contains(@"Source: ""{#MySourceDir}\shared\Assets\Segoe Fluent Icons.ttf""", text, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string text, string value)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(value))
        {
            return 0;
        }

        int count = 0;
        int startIndex = 0;
        while (true)
        {
            int index = text.IndexOf(value, startIndex, StringComparison.Ordinal);
            if (index < 0)
            {
                return count;
            }

            count++;
            startIndex = index + value.Length;
        }
    }

}
