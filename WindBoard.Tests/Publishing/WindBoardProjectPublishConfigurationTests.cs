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

        // 便携版主程序 publish 只剩一处（自包含）：framework-dependent 变体随 Inno 安装包停发一并下线。
        const string portablePublishCommand = "dotnet publish $project";
        Assert.Equal(1, CountOccurrences(text, portablePublishCommand));

        // 并断言 ReadyToRun 关闭标志确实落在这条 publish 命令上：
        // 只统计整份工作流里该标志出现几次，无法防止标志被挪到别的命令（例如 MSIX 产包）上。
        int commandStart = text.IndexOf(portablePublishCommand, StringComparison.Ordinal);
        int commandEnd = text.IndexOf("dotnet publish", commandStart + portablePublishCommand.Length, StringComparison.Ordinal);
        Assert.True(commandEnd > commandStart, "未找到便携版主程序 publish 之后的下一条 publish 命令，无法界定命令片段。");

        string portablePublishCommandText = text[commandStart..commandEnd];
        Assert.Contains("-p:PublishReadyToRun=false", portablePublishCommandText, StringComparison.Ordinal);
        Assert.Contains("--self-contained true", portablePublishCommandText, StringComparison.Ordinal);
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

    [Fact]
    public void InstallerScript_UsesValidCurStepChangedSignature()
    {
        DirectoryInfo? repoRoot = RepoRootLocator.Find();
        Assert.NotNull(repoRoot);

        string installerFilePath = Path.Combine(repoRoot!.FullName, "installer", "WindBoard.iss");
        Assert.True(File.Exists(installerFilePath), $"未找到安装脚本文件：{installerFilePath}");

        string text = File.ReadAllText(installerFilePath);
        Assert.Contains("procedure CurStepChanged(CurStep: TSetupStep);", text, StringComparison.Ordinal);
        Assert.DoesNotContain("TInstallStep", text, StringComparison.Ordinal);
    }

    [Fact]
    public void MainProject_BuildsCrashReporterIntoAppOutput_ForLocalBuilds()
    {
        DirectoryInfo? repoRoot = RepoRootLocator.Find();
        Assert.NotNull(repoRoot);

        string projectFilePath = Path.Combine(repoRoot!.FullName, "WindBoard", "WindBoard.csproj");
        Assert.True(File.Exists(projectFilePath), $"未找到主项目文件：{projectFilePath}");

        string text = File.ReadAllText(projectFilePath);
        Assert.Contains("<Target Name=\"WindBoard_BuildCrashReporterIntoAppOutput\" AfterTargets=\"Build\">", text, StringComparison.Ordinal);
        Assert.Contains("WindBoard.CrashReporter\\WindBoard.CrashReporter.csproj", text, StringComparison.Ordinal);
        Assert.Contains("OutDir=$(TargetDir)", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Condition=\"'$(PublishDir)' == ''\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void MainProject_MapsCrashReporterPlatform_FromRuntimeIdentifier_WhenPublishing()
    {
        DirectoryInfo? repoRoot = RepoRootLocator.Find();
        Assert.NotNull(repoRoot);

        string projectFilePath = Path.Combine(repoRoot!.FullName, "WindBoard", "WindBoard.csproj");
        Assert.True(File.Exists(projectFilePath), $"未找到主项目文件：{projectFilePath}");

        string text = File.ReadAllText(projectFilePath);
        Assert.Contains("Condition=\"'$(RuntimeIdentifier)' == 'win-x86'\"", text, StringComparison.Ordinal);
        Assert.Contains("Condition=\"'$(RuntimeIdentifier)' == 'win-x64'\"", text, StringComparison.Ordinal);
        Assert.Contains("Condition=\"'$(RuntimeIdentifier)' == 'win-arm64'\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void MainProject_GeneratesLocalizationMetadata_BeforePrepareForBuild()
    {
        DirectoryInfo? repoRoot = RepoRootLocator.Find();
        Assert.NotNull(repoRoot);

        string projectFilePath = Path.Combine(repoRoot!.FullName, "WindBoard", "WindBoard.csproj");
        Assert.True(File.Exists(projectFilePath), $"未找到主项目文件：{projectFilePath}");

        string text = File.ReadAllText(projectFilePath);
        Assert.Contains("<Target", text, StringComparison.Ordinal);
        Assert.Contains("Name=\"WindBoard_GenerateLocalizationMetadata\"", text, StringComparison.Ordinal);
        Assert.Contains("BeforeTargets=\"PrepareForBuild\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void LocalizationMetadataGeneratorScript_ExistsInRepository()
    {
        DirectoryInfo? repoRoot = RepoRootLocator.Find();
        Assert.NotNull(repoRoot);

        string scriptPath = Path.Combine(repoRoot!.FullName, "WindBoard", "Build", "GenerateLocalizationMetadata.ps1");
        Assert.True(File.Exists(scriptPath), $"未找到本地化元数据生成脚本：{scriptPath}");
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
