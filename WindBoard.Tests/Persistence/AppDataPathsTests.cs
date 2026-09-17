using WindBoard.Persistence;
using WindBoard.Updates;

namespace WindBoard.Tests.Persistence;

public sealed class AppDataPathsTests
{
    [Fact]
    public void ComputeSnapshot_Installer_UsesLocalAppData()
    {
        int ensureWritableCalls = 0;

        var install = new AppInstallProbeResult
        {
            Kind = AppInstallKind.Installer,
            Evidence = "registry",
            InstallDir = @"C:\Program Files\WindBoard",
        };

        AppDataPathsSnapshot snapshot = AppDataPaths.ComputeSnapshot(
            install,
            appBaseDirectory: @"C:\Program Files\WindBoard\",
            localAppDataDirectory: @"C:\Users\test\AppData\Local\",
            localAppDataEnvironmentValue: null,
            tryEnsureWritable: _ =>
            {
                ensureWritableCalls++;
                return (ok: true, errorMessage: null);
            });

        Assert.Equal(0, ensureWritableCalls);
        Assert.Equal(AppInstallKind.Installer, snapshot.InstallKind);
        Assert.Equal(Path.Combine(@"C:\Users\test\AppData\Local", "WindBoard"), snapshot.RootDirectory);
        Assert.False(snapshot.UsingPortableDataDirectory);
        Assert.Equal(Path.Combine(snapshot.RootDirectory, "settings.json"), snapshot.SettingsFilePath);
        Assert.Equal(Path.Combine(snapshot.RootDirectory, "Logs"), snapshot.LogsDirectory);
        Assert.Equal(Path.Combine(snapshot.RootDirectory, "downloads"), snapshot.DownloadsDirectory);
    }

    [Fact]
    public void ComputeSnapshot_Portable_Writable_UsesBaseData()
    {
        int ensureWritableCalls = 0;

        var install = new AppInstallProbeResult
        {
            Kind = AppInstallKind.Portable,
            Evidence = "fallback",
            InstallDir = @"D:\WindBoard",
        };

        AppDataPathsSnapshot snapshot = AppDataPaths.ComputeSnapshot(
            install,
            appBaseDirectory: @"D:\WindBoard\",
            localAppDataDirectory: @"C:\Users\test\AppData\Local\",
            localAppDataEnvironmentValue: null,
            tryEnsureWritable: dir =>
            {
                ensureWritableCalls++;
                Assert.Equal(@"D:\WindBoard\data", dir);
                return (ok: true, errorMessage: null);
            });

        Assert.Equal(1, ensureWritableCalls);
        Assert.Equal(Path.Combine(@"D:\WindBoard", "data"), snapshot.RootDirectory);
        Assert.True(snapshot.UsingPortableDataDirectory);
        Assert.True(snapshot.PortableDataDirectoryWritable);
    }

    [Fact]
    public void ComputeSnapshot_Portable_NotWritable_FallsBackToLocalAppData()
    {
        int ensureWritableCalls = 0;

        var install = new AppInstallProbeResult
        {
            Kind = AppInstallKind.Portable,
            Evidence = "fallback",
            InstallDir = @"D:\WindBoard",
        };

        AppDataPathsSnapshot snapshot = AppDataPaths.ComputeSnapshot(
            install,
            appBaseDirectory: @"D:\WindBoard\",
            localAppDataDirectory: @"C:\Users\test\AppData\Local\",
            localAppDataEnvironmentValue: null,
            tryEnsureWritable: dir =>
            {
                ensureWritableCalls++;
                Assert.Equal(@"D:\WindBoard\data", dir);
                return (ok: false, errorMessage: "AccessDenied");
            });

        Assert.Equal(1, ensureWritableCalls);
        Assert.Equal(Path.Combine(@"C:\Users\test\AppData\Local", "WindBoard"), snapshot.RootDirectory);
        Assert.False(snapshot.UsingPortableDataDirectory);
        Assert.False(snapshot.PortableDataDirectoryWritable);
        Assert.Equal("AccessDenied", snapshot.PortableDataDirectoryWriteTestError);
    }

    [Fact]
    public void ComputeSnapshot_Portable_SharedLayout_UsesProductRootDataDirectory()
    {
        int ensureWritableCalls = 0;

        var install = new AppInstallProbeResult
        {
            Kind = AppInstallKind.Portable,
            Evidence = "fallback",
            InstallDir = @"D:\WindBoard",
        };

        AppDataPathsSnapshot snapshot = AppDataPaths.ComputeSnapshot(
            install,
            appBaseDirectory: @"D:\WindBoard\shared\",
            localAppDataDirectory: @"C:\Users\test\AppData\Local\",
            localAppDataEnvironmentValue: null,
            tryEnsureWritable: dir =>
            {
                ensureWritableCalls++;
                Assert.Equal(@"D:\WindBoard\data", dir);
                return (ok: true, errorMessage: null);
            });

        Assert.Equal(1, ensureWritableCalls);
        Assert.Equal(Path.Combine(@"D:\WindBoard", "data"), snapshot.RootDirectory);
        Assert.True(snapshot.UsingPortableDataDirectory);
        Assert.Equal(@"D:\WindBoard", snapshot.InstallDir);
        Assert.Equal(@"D:\WindBoard\shared", snapshot.AppBaseDirectory);
    }

    [Fact]
    public void ComputeSnapshot_Msix_UsesLocalAppData_AndNotPortableWritabilityProbe()
    {
        int ensureWritableCalls = 0;

        var install = new AppInstallProbeResult
        {
            Kind = AppInstallKind.Msix,
            Evidence = "package-identity",
            InstallDir = @"C:\Program Files\WindowsApps\WindBoard_1.0.0.0_x64__abc",
        };

        AppDataPathsSnapshot snapshot = AppDataPaths.ComputeSnapshot(
            install,
            appBaseDirectory: @"C:\Program Files\WindowsApps\WindBoard_1.0.0.0_x64__abc\",
            localAppDataDirectory: @"C:\Users\test\AppData\Local\",
            localAppDataEnvironmentValue: @"C:\Users\test\AppData\Local",
            tryEnsureWritable: _ =>
            {
                ensureWritableCalls++;
                return (ok: true, errorMessage: null);
            });

        // MSIX 形态不走便携版可写性探测（数据落点由系统虚拟化处理）。
        Assert.Equal(0, ensureWritableCalls);
        Assert.Equal(AppInstallKind.Msix, snapshot.InstallKind);
        Assert.Equal(Path.Combine(@"C:\Users\test\AppData\Local", "WindBoard"), snapshot.RootDirectory);
        Assert.Equal(snapshot.RootDirectory, snapshot.OwnDataDirectory);
        Assert.False(snapshot.UsingPortableDataDirectory);
        Assert.Equal(Path.Combine(snapshot.RootDirectory, "settings.json"), snapshot.SettingsFilePath);
        Assert.Equal(Path.Combine(snapshot.RootDirectory, "Logs"), snapshot.LogsDirectory);
    }

    [Fact]
    public void ComputeSnapshot_LegacyInstallerDataDirectory_PrefersLocalAppDataEnvironmentValue()
    {
        var install = new AppInstallProbeResult
        {
            Kind = AppInstallKind.Msix,
            Evidence = "package-identity",
        };

        AppDataPathsSnapshot snapshot = AppDataPaths.ComputeSnapshot(
            install,
            appBaseDirectory: @"C:\Program Files\WindowsApps\WindBoard_1.0.0.0_x64__abc\",
            // 模拟打包进程内 GetFolderPath 返回值不确定：与真实环境变量不一致时以环境变量为准。
            localAppDataDirectory: @"C:\Users\test\AppData\Local\Packages\WindBoard_abc\LocalCache\Local",
            localAppDataEnvironmentValue: @"C:\Users\test\AppData\Local\\",
            tryEnsureWritable: _ => (ok: true, errorMessage: null));

        Assert.Equal(Path.Combine(@"C:\Users\test\AppData\Local", "WindBoard"), snapshot.LegacyInstallerDataDirectory);
        Assert.Equal(Path.Combine(@"C:\Users\test\AppData\Local\Packages\WindBoard_abc\LocalCache\Local", "WindBoard"), snapshot.OwnDataDirectory);
    }

    [Fact]
    public void ComputeSnapshot_LegacyInstallerDataDirectory_FallsBackToLocalAppDataDirectory()
    {
        var install = new AppInstallProbeResult
        {
            Kind = AppInstallKind.Installer,
            Evidence = "registry",
        };

        AppDataPathsSnapshot snapshot = AppDataPaths.ComputeSnapshot(
            install,
            appBaseDirectory: @"C:\Program Files\WindBoard\",
            localAppDataDirectory: @"C:\Users\test\AppData\Local\",
            localAppDataEnvironmentValue: "   ",
            tryEnsureWritable: _ => (ok: true, errorMessage: null));

        Assert.Equal(Path.Combine(@"C:\Users\test\AppData\Local", "WindBoard"), snapshot.LegacyInstallerDataDirectory);
    }
}
