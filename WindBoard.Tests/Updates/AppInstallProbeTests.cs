using WindBoard.Updates;

namespace WindBoard.Tests.Updates;

public sealed class AppInstallProbeTests
{
    [Fact]
    public void ComputeProbeResult_WithMatchingRegistryInstallDir_IsInstaller()
    {
        AppInstallProbeResult result = AppInstallProbe.ComputeProbeResult(
            productRootDirectory: @"C:\Program Files\WindBoard",
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
            registryInstallDir: string.Empty,
            registryInstallKind: string.Empty,
            registryInstallVariant: string.Empty,
            hasUninstallerInProductRoot: true);

        Assert.Equal(AppInstallKind.Installer, result.Kind);
        Assert.Equal(AppInstallVariant.Unknown, result.Variant);
        Assert.Equal("uninstaller-file", result.Evidence);
        Assert.Equal(@"C:\Program Files\WindBoard", result.InstallDir);
    }
}
