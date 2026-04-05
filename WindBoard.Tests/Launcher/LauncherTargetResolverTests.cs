using WindBoard.Launcher;

namespace WindBoard.Tests.Launcher;

public sealed class LauncherTargetResolverTests
{
    [Fact]
    public void Resolve_TargetExecutable_UsesSharedWindBoardExecutable()
    {
        LauncherTargetInfo info = LauncherTargetResolver.Resolve(@"D:\Apps\WindBoard\");

        Assert.Equal(@"D:\Apps\WindBoard\shared\WindBoard.exe", info.TargetExecutablePath);
        Assert.Equal(@"D:\Apps\WindBoard\shared", info.WorkingDirectory);
    }

    [Fact]
    public void Resolve_TargetExecutable_ReturnsMissingPathForEmptyRoot()
    {
        LauncherTargetInfo info = LauncherTargetResolver.Resolve(string.Empty);

        Assert.Equal(string.Empty, info.TargetExecutablePath);
        Assert.Equal(string.Empty, info.WorkingDirectory);
    }
}
