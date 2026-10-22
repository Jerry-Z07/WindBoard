using WindBoard.Persistence;
using WindBoard.Updates;

namespace WindBoard.Tests.Persistence;

public sealed class AppDataVisiblePathResolverTests
{
    private const string LocalAppDataRoot = @"C:\Users\test\AppData\Local";
    private const string LocalCacheRoot = @"C:\Users\test\AppData\Local\Packages\WindBoard_test\LocalCache";

    [Fact]
    public void TryMap_NotPackaged_ReturnsFriendlyPath()
    {
        string friendly = Path.Combine(LocalAppDataRoot, "WindBoard", "Logs");

        bool ok = AppDataVisiblePathResolver.TryMap(
            friendly,
            isPackaged: false,
            friendlyLocalAppDataRoot: LocalAppDataRoot,
            localCacheRoot: LocalCacheRoot,
            out string visiblePath);

        // 便携版 / 开发运行 / 旧安装版：友好路径就是外部可见路径，不做任何映射。
        Assert.True(ok);
        Assert.Equal(friendly, visiblePath);
    }

    [Fact]
    public void TryMap_NotPackaged_BlankPath_ReturnsFalse()
    {
        bool ok = AppDataVisiblePathResolver.TryMap(
            "   ",
            isPackaged: false,
            friendlyLocalAppDataRoot: LocalAppDataRoot,
            localCacheRoot: LocalCacheRoot,
            out string visiblePath);

        Assert.False(ok);
        Assert.Equal(string.Empty, visiblePath);
    }

    [Fact]
    public void TryMap_Packaged_UnderLocalAppData_MapsIntoLocalCacheLocal()
    {
        string friendly = Path.Combine(LocalAppDataRoot, "WindBoard", "Logs", "windboard-20260923.log");

        bool ok = AppDataVisiblePathResolver.TryMap(
            friendly,
            isPackaged: true,
            friendlyLocalAppDataRoot: LocalAppDataRoot,
            localCacheRoot: LocalCacheRoot,
            out string visiblePath);

        Assert.True(ok);
        Assert.Equal(
            Path.Combine(LocalCacheRoot, "Local", "WindBoard", "Logs", "windboard-20260923.log"),
            visiblePath);
    }

    [Fact]
    public void TryMap_Packaged_LocalAppDataRootItself_MapsToLocalCacheLocal()
    {
        bool ok = AppDataVisiblePathResolver.TryMap(
            LocalAppDataRoot,
            isPackaged: true,
            friendlyLocalAppDataRoot: LocalAppDataRoot,
            localCacheRoot: LocalCacheRoot,
            out string visiblePath);

        Assert.True(ok);
        Assert.Equal(Path.Combine(LocalCacheRoot, "Local"), visiblePath);
    }

    [Fact]
    public void TryMap_Packaged_TrailingSeparators_AreNormalized()
    {
        bool ok = AppDataVisiblePathResolver.TryMap(
            LocalAppDataRoot + @"\WindBoard\Logs\",
            isPackaged: true,
            friendlyLocalAppDataRoot: LocalAppDataRoot + @"\",
            localCacheRoot: LocalCacheRoot + @"\",
            out string visiblePath);

        Assert.True(ok);
        Assert.Equal(Path.Combine(LocalCacheRoot, "Local", "WindBoard", "Logs"), visiblePath);
    }

    [Fact]
    public void TryMap_Packaged_AltSeparators_AreNormalized()
    {
        bool ok = AppDataVisiblePathResolver.TryMap(
            "C:/Users/test/AppData/Local/WindBoard/Logs",
            isPackaged: true,
            friendlyLocalAppDataRoot: LocalAppDataRoot,
            localCacheRoot: LocalCacheRoot,
            out string visiblePath);

        Assert.True(ok);
        Assert.Equal(Path.Combine(LocalCacheRoot, "Local", "WindBoard", "Logs"), visiblePath);
    }

    [Fact]
    public void TryMap_Packaged_RootComparison_IsCaseInsensitive()
    {
        bool ok = AppDataVisiblePathResolver.TryMap(
            @"c:\users\test\appdata\local\windboard\settings.json",
            isPackaged: true,
            friendlyLocalAppDataRoot: @"C:\Users\Test\AppData\Local",
            localCacheRoot: LocalCacheRoot,
            out string visiblePath);

        Assert.True(ok);
        Assert.Equal(Path.Combine(LocalCacheRoot, "Local", "windboard", "settings.json"), visiblePath);
    }

    [Fact]
    public void TryMap_Packaged_SiblingDirectoryWithSamePrefix_ReturnsFalse()
    {
        bool ok = AppDataVisiblePathResolver.TryMap(
            @"C:\Users\test\AppData\LocalOther\WindBoard",
            isPackaged: true,
            friendlyLocalAppDataRoot: LocalAppDataRoot,
            localCacheRoot: LocalCacheRoot,
            out string visiblePath);

        Assert.False(ok);
        Assert.Equal(string.Empty, visiblePath);
    }

    [Fact]
    public void TryMap_Packaged_PathOutsideLocalAppData_ReturnsFalse()
    {
        bool ok = AppDataVisiblePathResolver.TryMap(
            @"D:\WindBoard\data\Logs",
            isPackaged: true,
            friendlyLocalAppDataRoot: LocalAppDataRoot,
            localCacheRoot: LocalCacheRoot,
            out string visiblePath);

        Assert.False(ok);
        Assert.Equal(string.Empty, visiblePath);
    }

    [Theory]
    [InlineData(@"WindBoard\Logs")]
    [InlineData(@"\WindBoard\Logs")]
    public void TryMap_Packaged_RelativePath_ReturnsFalse(string relativePath)
    {
        bool ok = AppDataVisiblePathResolver.TryMap(
            relativePath,
            isPackaged: true,
            friendlyLocalAppDataRoot: LocalAppDataRoot,
            localCacheRoot: LocalCacheRoot,
            out string visiblePath);

        Assert.False(ok);
        Assert.Equal(string.Empty, visiblePath);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryMap_Packaged_MissingLocalCacheRoot_ReturnsFalse(string? localCacheRoot)
    {
        bool ok = AppDataVisiblePathResolver.TryMap(
            Path.Combine(LocalAppDataRoot, "WindBoard"),
            isPackaged: true,
            friendlyLocalAppDataRoot: LocalAppDataRoot,
            localCacheRoot: localCacheRoot,
            out string visiblePath);

        Assert.False(ok);
        Assert.Equal(string.Empty, visiblePath);
    }

    [Fact]
    public void TryMap_Packaged_BlankFriendlyLocalAppDataRoot_ReturnsFalse()
    {
        bool ok = AppDataVisiblePathResolver.TryMap(
            Path.Combine(LocalAppDataRoot, "WindBoard"),
            isPackaged: true,
            friendlyLocalAppDataRoot: string.Empty,
            localCacheRoot: LocalCacheRoot,
            out string visiblePath);

        Assert.False(ok);
        Assert.Equal(string.Empty, visiblePath);
    }

    [Fact]
    public void TryMap_Packaged_MapsToLayoutOnDisk()
    {
        // 自建自清：验证映射结果确实指向「外部进程在真实磁盘上能看到」的位置。
        string tempRoot = Path.Combine(Path.GetTempPath(), "WindBoard-Tests", Guid.NewGuid().ToString("N"));
        string friendlyLocalAppDataRoot = Path.Combine(tempRoot, "Local");
        string localCacheRoot = Path.Combine(tempRoot, "Packages", "WindBoard_test", "LocalCache");
        string realLogsDirectory = Path.Combine(localCacheRoot, "Local", "WindBoard", "Logs");

        try
        {
            Directory.CreateDirectory(realLogsDirectory);

            bool ok = AppDataVisiblePathResolver.TryMap(
                Path.Combine(friendlyLocalAppDataRoot, "WindBoard", "Logs"),
                isPackaged: true,
                friendlyLocalAppDataRoot: friendlyLocalAppDataRoot,
                localCacheRoot: localCacheRoot,
                out string visiblePath);

            Assert.True(ok);
            Assert.Equal(realLogsDirectory, visiblePath);
            Assert.True(Directory.Exists(visiblePath));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void TryResolve_UnpackagedTestHost_ReturnsFriendlyPathUnchanged()
    {
        // 单测宿主是 unpackaged 进程（dotnet test）：组合入口不得触碰 WinAppSDK/WinRT，直接返回友好路径。
        Assert.False(AppInstallProbe.IsPackagedProcess());

        string friendly = Path.Combine(LocalAppDataRoot, "WindBoard", "Logs");

        bool ok = AppDataVisiblePathResolver.TryResolve(friendly, out string visiblePath);

        Assert.True(ok);
        Assert.Equal(friendly, visiblePath);
    }
}
