using System.Text.RegularExpressions;
using WindBoard.Features.Camouflage.Services;
using Xunit;

namespace WindBoard.Tests.Features.Camouflage;

public sealed class CamouflageServiceTests
{
    [Fact]
    public void ComputeCamouflageShortcutSettingsSignature_ReturnsUppercaseHexSha256()
    {
        string signature = CamouflageService.ComputeCamouflageShortcutSettingsSignature(
            enabled: true,
            title: "Title",
            sourcePath: "C:\\Temp\\a.png",
            iconCachePath: "C:\\Temp\\camouflage.ico");

        Assert.Equal(64, signature.Length);
        Assert.Matches(new Regex("^[0-9A-F]{64}$"), signature);
    }

    [Fact]
    public void ComputeCamouflageShortcutSettingsSignature_ChangesWhenAnyFieldChanges()
    {
        string baseSig = CamouflageService.ComputeCamouflageShortcutSettingsSignature(
            enabled: true,
            title: "Title",
            sourcePath: "a",
            iconCachePath: "b");

        string disabledSig = CamouflageService.ComputeCamouflageShortcutSettingsSignature(
            enabled: false,
            title: "Title",
            sourcePath: "a",
            iconCachePath: "b");

        string titleSig = CamouflageService.ComputeCamouflageShortcutSettingsSignature(
            enabled: true,
            title: "Title2",
            sourcePath: "a",
            iconCachePath: "b");

        string sourceSig = CamouflageService.ComputeCamouflageShortcutSettingsSignature(
            enabled: true,
            title: "Title",
            sourcePath: "a2",
            iconCachePath: "b");

        string cacheSig = CamouflageService.ComputeCamouflageShortcutSettingsSignature(
            enabled: true,
            title: "Title",
            sourcePath: "a",
            iconCachePath: "b2");

        Assert.NotEqual(baseSig, disabledSig);
        Assert.NotEqual(baseSig, titleSig);
        Assert.NotEqual(baseSig, sourceSig);
        Assert.NotEqual(baseSig, cacheSig);
    }
}
