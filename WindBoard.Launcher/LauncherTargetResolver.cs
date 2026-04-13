using System.IO;

namespace WindBoard.Launcher;

public static class LauncherTargetResolver
{
    public static LauncherTargetInfo Resolve(string? productRootDirectory)
    {
        string root = NormalizeDir(productRootDirectory);
        if (string.IsNullOrWhiteSpace(root))
        {
            return new LauncherTargetInfo(string.Empty, string.Empty);
        }

        string workingDirectory = Path.Combine(root, "shared");
        string targetExecutablePath = Path.Combine(workingDirectory, "WindBoard.exe");
        return new LauncherTargetInfo(targetExecutablePath, workingDirectory);
    }

    private static string NormalizeDir(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return string.Empty;
        }

        try
        {
            string full = Path.GetFullPath(directory.Trim());
            return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return directory.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}

public sealed record LauncherTargetInfo(string TargetExecutablePath, string WorkingDirectory);
