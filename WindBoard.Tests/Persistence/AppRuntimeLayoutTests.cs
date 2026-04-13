using WindBoard.Persistence;

namespace WindBoard.Tests.Persistence;

public sealed class AppRuntimeLayoutTests
{
    [Fact]
    public void Resolve_WithSharedRuntimeDirectory_UsesParentAsProductRoot()
    {
        AppRuntimeLayout layout = AppRuntimeLayout.Resolve(@"D:\Apps\WindBoard\shared\");

        Assert.Equal(@"D:\Apps\WindBoard", layout.ProductRootDirectory);
        Assert.Equal(@"D:\Apps\WindBoard\shared", layout.RuntimeDirectory);
        Assert.Equal(Path.Combine(@"D:\Apps\WindBoard", "data"), layout.PortableDataDirectory);
        Assert.Equal(Path.Combine(@"D:\Apps\WindBoard", "WindBoard.exe"), layout.LauncherExecutablePath);
        Assert.Equal(Path.Combine(@"D:\Apps\WindBoard\shared", "WindBoard.CrashReporter.exe"), layout.CrashReporterExecutablePath);
    }

    [Fact]
    public void Resolve_WithoutSharedRuntimeDirectory_UsesCurrentDirectoryAsProductRoot()
    {
        AppRuntimeLayout layout = AppRuntimeLayout.Resolve(@"D:\Apps\WindBoard\");

        Assert.Equal(@"D:\Apps\WindBoard", layout.ProductRootDirectory);
        Assert.Equal(@"D:\Apps\WindBoard", layout.RuntimeDirectory);
        Assert.Equal(Path.Combine(@"D:\Apps\WindBoard", "data"), layout.PortableDataDirectory);
        Assert.Equal(Path.Combine(@"D:\Apps\WindBoard", "WindBoard.exe"), layout.LauncherExecutablePath);
        Assert.Equal(Path.Combine(@"D:\Apps\WindBoard", "WindBoard.CrashReporter.exe"), layout.CrashReporterExecutablePath);
    }
}
