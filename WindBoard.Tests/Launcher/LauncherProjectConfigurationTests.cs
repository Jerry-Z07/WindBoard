using System.Xml.Linq;
using WindBoard.Tests;
using Xunit;

namespace WindBoard.Tests.Launcher;

public sealed class LauncherProjectConfigurationTests
{
    [Fact]
    public void LauncherProject_UsesPortableTargetFramework_WithoutWindowsTargeting()
    {
        DirectoryInfo? repoRoot = RepoRootLocator.Find();
        Assert.NotNull(repoRoot);

        string projectFilePath = Path.Combine(repoRoot!.FullName, "WindBoard.Launcher", "WindBoard.Launcher.csproj");
        Assert.True(File.Exists(projectFilePath), $"未找到启动器项目文件：{projectFilePath}");

        XDocument document = XDocument.Load(projectFilePath);
        XElement? propertyGroup = document.Root?.Elements("PropertyGroup").FirstOrDefault();
        Assert.NotNull(propertyGroup);

        Assert.Equal("net10.0", GetPropertyValue(propertyGroup!, "TargetFramework"));
        Assert.Equal("true", GetPropertyValue(propertyGroup!, "PublishAot"));
        Assert.Null(GetPropertyValue(propertyGroup!, "EnableWindowsTargeting"));
        Assert.Null(GetPropertyValue(propertyGroup!, "TargetPlatformMinVersion"));
    }

    private static string? GetPropertyValue(XElement propertyGroup, string propertyName)
    {
        return propertyGroup.Elements(propertyName).Select(static element => element.Value.Trim()).FirstOrDefault();
    }

}
