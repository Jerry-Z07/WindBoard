using WindBoard.Updates;

namespace WindBoard.Tests.Updates;

public sealed class UpdateAssetSelectorTests
{
    [Fact]
    public void Select_Should_Recommend_SelfContained_Installer_For_Installer_SelfContained()
    {
        var assets = CreateAssets();

        var install = new AppInstallProbeResult
        {
            Kind = AppInstallKind.Installer,
            Variant = AppInstallVariant.SelfContained,
        };

        UpdateAssetRecommendation rec = UpdateAssetSelector.Select(assets, "x64", install);

        Assert.NotNull(rec.Recommended);
        Assert.Equal(UpdateAssetKind.InstallerSelfContained, rec.Recommended!.Kind);
        Assert.Equal(UpdateAssetKind.InstallerSelfContained, rec.Alternatives[0].Kind);
    }

    [Fact]
    public void Select_Should_Recommend_FrameworkDependent_Installer_For_Installer_FrameworkDependent()
    {
        var assets = CreateAssets();

        var install = new AppInstallProbeResult
        {
            Kind = AppInstallKind.Installer,
            Variant = AppInstallVariant.FrameworkDependent,
        };

        UpdateAssetRecommendation rec = UpdateAssetSelector.Select(assets, "x64", install);

        Assert.NotNull(rec.Recommended);
        Assert.Equal(UpdateAssetKind.InstallerFrameworkDependent, rec.Recommended!.Kind);
        Assert.Equal(UpdateAssetKind.InstallerFrameworkDependent, rec.Alternatives[0].Kind);
    }

    [Fact]
    public void Select_Should_Recommend_Zip_For_Portable()
    {
        var assets = CreateAssets();

        var install = new AppInstallProbeResult
        {
            Kind = AppInstallKind.Portable,
            Variant = AppInstallVariant.Unknown,
        };

        UpdateAssetRecommendation rec = UpdateAssetSelector.Select(assets, "x64", install);

        Assert.NotNull(rec.Recommended);
        Assert.Equal(UpdateAssetKind.PortableZip, rec.Recommended!.Kind);
        Assert.Equal(UpdateAssetKind.PortableZip, rec.Alternatives[0].Kind);
    }

    [Fact]
    public void Select_Should_Not_Force_Recommend_When_Installer_Variant_Unknown()
    {
        var assets = CreateAssets();

        var install = new AppInstallProbeResult
        {
            Kind = AppInstallKind.Installer,
            Variant = AppInstallVariant.Unknown,
        };

        UpdateAssetRecommendation rec = UpdateAssetSelector.Select(assets, "x64", install);

        Assert.Null(rec.Recommended);
        Assert.Equal(UpdateAssetKind.InstallerSelfContained, rec.Alternatives[0].Kind);
        Assert.Equal(UpdateAssetKind.InstallerFrameworkDependent, rec.Alternatives[1].Kind);
        Assert.Equal(UpdateAssetKind.PortableZip, rec.Alternatives[2].Kind);
    }

    private static List<LatestReleaseAsset> CreateAssets()
    {
        // 仅构造用于选择逻辑的最小字段：Arch + FileName + DownloadUrl。
        return new List<LatestReleaseAsset>
        {
            new()
            {
                Arch = "x64",
                FileName = "WindBoardSetup-2.0.0-win-x64.exe",
                DownloadUrl = "https://example.test/sc.exe",
            },
            new()
            {
                Arch = "x64",
                FileName = "WindBoardSetup-2.0.0-win-x64-fd.exe",
                DownloadUrl = "https://example.test/fd.exe",
            },
            new()
            {
                Arch = "x64",
                FileName = "WindBoard-2.0.0-win-x64.zip",
                DownloadUrl = "https://example.test/portable.zip",
            },
            new()
            {
                Arch = "x86",
                FileName = "WindBoardSetup-2.0.0-win-x86.exe",
                DownloadUrl = "https://example.test/other.exe",
            },
        };
    }
}

