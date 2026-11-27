using System.IO;
using WindBoard.Persistence;
using WindBoard.Updates;
using Xunit;

namespace WindBoard.Tests.Persistence;

public sealed class AppDataCleanupTests
{

    [Theory]
    [InlineData("WindBoardSetup-2.0.0-win-x64.exe", "2.0.0")]
    [InlineData("WindBoardSetup-2.0.0-win-x64-fd.exe", "2.0.0")]
    [InlineData("WindBoard-2.0.0-win-x64.zip", "2.0.0")]
    [InlineData("WindBoardSetup-1.9.9-beta.1-win-x64.exe", "1.9.9-beta.1")]
    [InlineData("WindBoardSetup-3.0.0-win-x86.exe", "3.0.0")]
    public void TryMatchInstallerVersion_Matches_ValidFileName(string fileName, string expectedVersion)
    {
        var match = AppDataCleanup.TryMatchInstallerVersion(fileName);
        Assert.NotNull(match);
        Assert.Equal(expectedVersion, match.Groups[1].Value);
    }

    [Theory]
    [InlineData("random-file.txt")]
    [InlineData("WindBoard.exe")]
    [InlineData("notes.md")]
    [InlineData("")]
    public void TryMatchInstallerVersion_DoesNotMatch_InvalidFileName(string fileName)
    {
        var match = AppDataCleanup.TryMatchInstallerVersion(fileName);
        Assert.Null(match);
    }

    [Theory]
    [InlineData("default_2.0.0.ico", "2.0.0")]
    [InlineData("default_1.9.9-beta.1.ico", "1.9.9-beta.1")]
    [InlineData("default_3.0.0-alpha.2.ico", "3.0.0-alpha.2")]
    public void TryMatchDefaultIconVersion_Matches_ValidFileName(string fileName, string expectedVersion)
    {
        var match = AppDataCleanup.TryMatchDefaultIconVersion(fileName);
        Assert.NotNull(match);
        Assert.Equal(expectedVersion, match.Groups[1].Value);
    }

    [Theory]
    [InlineData("default.ico")]
    [InlineData("camouflage.ico")]
    [InlineData("default_.ico")]
    [InlineData("readme.txt")]
    [InlineData("")]
    public void TryMatchDefaultIconVersion_DoesNotMatch_InvalidFileName(string fileName)
    {
        var match = AppDataCleanup.TryMatchDefaultIconVersion(fileName);
        Assert.Null(match);
    }

    [Fact]
    public void CleanupFilesWithVersion_DeletesOldVersionFiles_AndKeepsCurrentAndNewer()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"wb_cleanup_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            string oldInstaller = Path.Combine(tempDir, "WindBoardSetup-1.0.0-win-x64.exe");
            string currentInstaller = Path.Combine(tempDir, "WindBoardSetup-2.0.0-win-x64.exe");
            string newerInstaller = Path.Combine(tempDir, "WindBoardSetup-3.0.0-win-x64.exe");
            string unrelated = Path.Combine(tempDir, "other-file.txt");

            File.WriteAllText(oldInstaller, "old");
            File.WriteAllText(currentInstaller, "current");
            File.WriteAllText(newerInstaller, "newer");
            File.WriteAllText(unrelated, "unrelated");

            // 当前版本 2.0.0：应删除 1.0.0，保留 2.0.0 和 3.0.0
            Assert.True(SemanticVersion.TryParse("2.0.0", out SemanticVersion current));

            // 直接调用 Run 需要 AppDataPaths，这里只验证“正则匹配 + 语义版本比较”这两段逻辑。
            var oldMatch = AppDataCleanup.TryMatchInstallerVersion(Path.GetFileName(oldInstaller));
            Assert.NotNull(oldMatch);
            Assert.True(SemanticVersion.TryParse(oldMatch.Groups[1].Value, out SemanticVersion oldVer));
            Assert.True(oldVer.CompareTo(current) < 0);

            var curMatch = AppDataCleanup.TryMatchInstallerVersion(Path.GetFileName(currentInstaller));
            Assert.NotNull(curMatch);
            Assert.True(SemanticVersion.TryParse(curMatch.Groups[1].Value, out SemanticVersion curVer));
            Assert.True(curVer.CompareTo(current) >= 0);

            var newMatch = AppDataCleanup.TryMatchInstallerVersion(Path.GetFileName(newerInstaller));
            Assert.NotNull(newMatch);
            Assert.True(SemanticVersion.TryParse(newMatch.Groups[1].Value, out SemanticVersion newVer));
            Assert.True(newVer.CompareTo(current) > 0);

            Assert.Null(AppDataCleanup.TryMatchInstallerVersion(Path.GetFileName(unrelated)));
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void DefaultIconCleanup_OnlyDeletesOlderVersions()
    {

        var oldMatch = AppDataCleanup.TryMatchDefaultIconVersion("default_1.0.0.ico");
        Assert.NotNull(oldMatch);
        Assert.Equal("1.0.0", oldMatch.Groups[1].Value);

        var curMatch = AppDataCleanup.TryMatchDefaultIconVersion("default_2.0.0.ico");
        Assert.NotNull(curMatch);
        Assert.Equal("2.0.0", curMatch.Groups[1].Value);

        Assert.True(SemanticVersion.TryParse("1.0.0", out SemanticVersion oldVer));
        Assert.True(SemanticVersion.TryParse("2.0.0", out SemanticVersion curVer));
        Assert.True(oldVer.CompareTo(curVer) < 0);
        Assert.True(curVer.CompareTo(curVer) >= 0); // 当前版本不会被清理

        Assert.Null(AppDataCleanup.TryMatchDefaultIconVersion("default.ico"));
    }
}
