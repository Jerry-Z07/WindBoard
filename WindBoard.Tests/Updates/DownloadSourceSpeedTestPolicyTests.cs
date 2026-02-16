using WindBoard.Settings;
using WindBoard.Updates;

namespace WindBoard.Tests.Updates;

public sealed class DownloadSourceSpeedTestPolicyTests
{
    [Fact]
    public void ShouldSpeedTest_Should_Return_False_For_Fixed()
    {
        bool due = DownloadSourceSpeedTestPolicy.ShouldSpeedTest(
            installKind: AppInstallKind.Installer,
            policy: DownloadSourcePolicy.Fixed,
            lastTestUtc: null,
            mode: UpdateCheckMode.Auto,
            nowUtc: DateTimeOffset.UtcNow);

        Assert.False(due);
    }

    [Fact]
    public void ShouldSpeedTest_Installer_Auto_Should_Run_Only_When_LastTest_Is_Null()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        Assert.True(DownloadSourceSpeedTestPolicy.ShouldSpeedTest(
            installKind: AppInstallKind.Installer,
            policy: DownloadSourcePolicy.Auto,
            lastTestUtc: null,
            mode: UpdateCheckMode.Manual,
            nowUtc: now));

        Assert.False(DownloadSourceSpeedTestPolicy.ShouldSpeedTest(
            installKind: AppInstallKind.Installer,
            policy: DownloadSourcePolicy.Auto,
            lastTestUtc: now.AddMinutes(-1),
            mode: UpdateCheckMode.Manual,
            nowUtc: now));
    }

    [Fact]
    public void ShouldSpeedTest_Portable_Auto_Should_Run_For_AutoMode_Only()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        Assert.True(DownloadSourceSpeedTestPolicy.ShouldSpeedTest(
            installKind: AppInstallKind.Portable,
            policy: DownloadSourcePolicy.Auto,
            lastTestUtc: now.AddMinutes(-1),
            mode: UpdateCheckMode.Auto,
            nowUtc: now));

        Assert.False(DownloadSourceSpeedTestPolicy.ShouldSpeedTest(
            installKind: AppInstallKind.Portable,
            policy: DownloadSourcePolicy.Auto,
            lastTestUtc: null,
            mode: UpdateCheckMode.Manual,
            nowUtc: now));
    }

    [Fact]
    public void ShouldSpeedTest_Unknown_InstallKind_Should_Return_False()
    {
        bool due = DownloadSourceSpeedTestPolicy.ShouldSpeedTest(
            installKind: AppInstallKind.Unknown,
            policy: DownloadSourcePolicy.Auto,
            lastTestUtc: null,
            mode: UpdateCheckMode.Auto,
            nowUtc: DateTimeOffset.UtcNow);

        Assert.False(due);
    }
}

