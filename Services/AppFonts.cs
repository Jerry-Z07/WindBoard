using System;
using System.IO;
using System.Windows.Media;

namespace WindBoard.Services;

internal static class AppFonts
{
    internal const string AppFontFamilyResourceKey = "AppFontFamily";

    internal static FontFamily? TryLoadMiSansFontFamily(string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            return null;
        }

        string fontsDirectory = Path.Combine(baseDirectory, "resources", "fonts");
        if (!Directory.Exists(fontsDirectory))
        {
            return null;
        }

        string directoryPath = EnsureTrailingDirectorySeparator(fontsDirectory);
        var baseUri = new Uri(directoryPath, UriKind.Absolute);
        return new FontFamily(baseUri, "./#MiSans");
    }

    private static string EnsureTrailingDirectorySeparator(string path)
    {
        if (path.EndsWith(Path.DirectorySeparatorChar))
        {
            return path;
        }

        return path + Path.DirectorySeparatorChar;
    }
}

