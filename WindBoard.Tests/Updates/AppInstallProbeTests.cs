using WindBoard.Updates;

namespace WindBoard.Tests.Updates;

public sealed class AppInstallProbeTests
{
    [Fact]
    public void ComputeProbeResult_WithMatchingRegistryInstallDir_IsInstaller()
    {
        AppInstallProbeResult result = AppInstallProbe.ComputeProbeResult(
            productRootDirectory: @"C:\Program Files\WindBoard",
            isPackagedProcess: false,
            registryInstallDir: @"C:\Program Files\WindBoard",
            registryInstallKind: "installer",
            registryInstallVariant: "framework-dependent",
            hasUninstallerInProductRoot: false);

        Assert.Equal(AppInstallKind.Installer, result.Kind);
        Assert.Equal(AppInstallVariant.FrameworkDependent, result.Variant);
        Assert.Equal("registry", result.Evidence);
        Assert.Equal(@"C:\Program Files\WindBoard", result.InstallDir);
    }

    [Fact]
    public void ComputeProbeResult_WithUninstallerInProductRoot_IsInstaller()
    {
        AppInstallProbeResult result = AppInstallProbe.ComputeProbeResult(
            productRootDirectory: @"C:\Program Files\WindBoard",
            isPackagedProcess: false,
            registryInstallDir: string.Empty,
            registryInstallKind: string.Empty,
            registryInstallVariant: string.Empty,
            hasUninstallerInProductRoot: true);

        Assert.Equal(AppInstallKind.Installer, result.Kind);
        Assert.Equal(AppInstallVariant.Unknown, result.Variant);
        Assert.Equal("uninstaller-file", result.Evidence);
        Assert.Equal(@"C:\Program Files\WindBoard", result.InstallDir);
    }

    [Fact]
    public void ComputeProbeResult_WithoutAnyEvidence_IsPortable()
    {
        AppInstallProbeResult result = AppInstallProbe.ComputeProbeResult(
            productRootDirectory: @"D:\WindBoard",
            isPackagedProcess: false,
            registryInstallDir: string.Empty,
            registryInstallKind: string.Empty,
            registryInstallVariant: string.Empty,
            hasUninstallerInProductRoot: false);

        Assert.Equal(AppInstallKind.Portable, result.Kind);
        Assert.Equal(AppInstallVariant.Unknown, result.Variant);
        Assert.Equal("fallback", result.Evidence);
    }

    [Fact]
    public void ComputeProbeResult_WhenPackagedProcess_IsMsix()
    {
        AppInstallProbeResult result = AppInstallProbe.ComputeProbeResult(
            productRootDirectory: @"C:\Program Files\WindowsApps\WindBoard",
            isPackagedProcess: true,
            registryInstallDir: string.Empty,
            registryInstallKind: string.Empty,
            registryInstallVariant: string.Empty,
            hasUninstallerInProductRoot: false);

        Assert.Equal(AppInstallKind.Msix, result.Kind);
        Assert.Equal(AppInstallVariant.Unknown, result.Variant);
        Assert.Equal("package-identity", result.Evidence);
        Assert.Equal(@"C:\Program Files\WindowsApps\WindBoard", result.InstallDir);
    }

    [Fact]
    public void ComputeProbeResult_WhenPackagedProcess_TakesPrecedenceOverInstallerEvidence()
    {
        // 包身份是最高优先级证据：即使注册表/卸载器痕迹同时存在（例如同机装了旧 Inno 版），也判定为 Msix。
        AppInstallProbeResult result = AppInstallProbe.ComputeProbeResult(
            productRootDirectory: @"C:\Program Files\WindowsApps\WindBoard",
            isPackagedProcess: true,
            registryInstallDir: @"C:\Program Files\WindowsApps\WindBoard",
            registryInstallKind: "installer",
            registryInstallVariant: "self-contained",
            hasUninstallerInProductRoot: true);

        Assert.Equal(AppInstallKind.Msix, result.Kind);
        Assert.Equal("package-identity", result.Evidence);
    }
}
