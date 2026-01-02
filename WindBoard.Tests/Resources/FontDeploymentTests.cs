using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Windows;
using System.Windows.Media;
using WindBoard;
using WindBoard.Services;
using Xunit;

namespace WindBoard.Tests.Resources;

public sealed class FontDeploymentTests
{
    [Fact]
    public void WindBoardAssembly_GetCompiledResources_DoesNotContainTtfFonts()
    {
        Assembly assembly = typeof(App).Assembly;

        string? gResourcesName = assembly
            .GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(".g.resources", StringComparison.OrdinalIgnoreCase));

        if (gResourcesName is null)
        {
            Assert.DoesNotContain(
                assembly.GetManifestResourceNames(),
                name => name.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase));
            return;
        }

        using Stream? stream = assembly.GetManifestResourceStream(gResourcesName);
        Assert.NotNull(stream);

        using var reader = new ResourceReader(stream);
        foreach (DictionaryEntry entry in reader)
        {
            string key = Assert.IsType<string>(entry.Key);
            Assert.DoesNotContain(".ttf", key, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void FontFiles_GetInstalledLayout_ArePresentInOutput()
    {
        string fontsDirectory = Path.Combine(AppContext.BaseDirectory, "resources", "fonts");
        Assert.True(Directory.Exists(fontsDirectory), $"Missing fonts directory: {fontsDirectory}");

        string[] expectedFiles =
        [
            "MiSans-Bold.ttf",
            "MiSans-Medium.ttf",
            "MiSans-Regular.ttf",
            "MiSans-Semibold.ttf"
        ];

        foreach (string fileName in expectedFiles)
        {
            string path = Path.Combine(fontsDirectory, fileName);
            Assert.True(File.Exists(path), $"Missing font file: {path}");
        }
    }

    [StaFact]
    public void FontFamily_LoadMiSansFromAppDirectory_CanResolveGlyphTypeface()
    {
        FontFamily fontFamily = AppFonts.TryLoadMiSansFontFamily(AppContext.BaseDirectory)
            ?? throw new InvalidOperationException("Failed to load MiSans from resources/fonts.");

        var typeface = new Typeface(fontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        Assert.True(typeface.TryGetGlyphTypeface(out GlyphTypeface? glyphTypeface));
        Assert.NotNull(glyphTypeface);

        Assert.Contains(
            glyphTypeface.FamilyNames.Values,
            name => name.Contains("MiSans", StringComparison.OrdinalIgnoreCase));
    }
}
