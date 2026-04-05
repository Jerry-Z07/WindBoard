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
}
