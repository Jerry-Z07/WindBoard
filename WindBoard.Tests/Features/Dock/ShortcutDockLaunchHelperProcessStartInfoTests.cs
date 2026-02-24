using System.Diagnostics;
using WindBoard.Features.Dock.Services;
using Xunit;

namespace WindBoard.Tests.Features.Dock;

public sealed class ShortcutDockLaunchHelperProcessStartInfoTests
{
    [Fact]
    public void CreateProgramProcessStartInfo_Exe_UsesCreateProcess()
    {
        ProcessStartInfo info = ShortcutDockLaunchHelper.CreateProgramProcessStartInfo(
            @"C:\Program Files\MyApp\app.exe",
            "  --foo bar  ");

        Assert.False(info.UseShellExecute);
        Assert.Equal(@"C:\Program Files\MyApp\app.exe", info.FileName);
        Assert.Equal("--foo bar", info.Arguments);
        Assert.Equal(@"C:\Program Files\MyApp", info.WorkingDirectory);
    }

    [Fact]
    public void CreateProgramProcessStartInfo_Bat_UsesCmdExe()
    {
        ProcessStartInfo info = ShortcutDockLaunchHelper.CreateProgramProcessStartInfo(
            @"C:\Temp\run.cmd",
            "--a 1");

        Assert.False(info.UseShellExecute);
        Assert.Equal("cmd.exe", info.FileName);
        Assert.Contains("/c", info.Arguments);
        Assert.Equal(@"C:\Temp", info.WorkingDirectory);
    }

    [Fact]
    public void CreateProgramProcessStartInfo_Lnk_UsesShellExecute()
    {
        ProcessStartInfo info = ShortcutDockLaunchHelper.CreateProgramProcessStartInfo(
            @"C:\Temp\app.lnk",
            null);

        Assert.True(info.UseShellExecute);
        Assert.Equal(@"C:\Temp\app.lnk", info.FileName);
    }
}
