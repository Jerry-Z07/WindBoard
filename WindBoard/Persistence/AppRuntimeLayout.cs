using System;
using System.IO;

namespace WindBoard.Persistence
{
    /// <summary>
    /// 运行布局解析：兼容开发态/旧布局，以及发布后真实主程序位于 shared 子目录的新布局。
    /// </summary>
    internal sealed class AppRuntimeLayout
    {
        private const string SharedDirectoryName = "shared";

        internal string ProductRootDirectory { get; init; } = string.Empty;

        internal string RuntimeDirectory { get; init; } = string.Empty;

        internal string PortableDataDirectory { get; init; } = string.Empty;

        internal string LauncherExecutablePath { get; init; } = string.Empty;

        internal string CrashReporterExecutablePath { get; init; } = string.Empty;

        internal static AppRuntimeLayout Resolve(string? appBaseDirectory)
        {
            string runtimeDirectory = NormalizeDir(appBaseDirectory);
            string productRootDirectory = runtimeDirectory;

            if (!string.IsNullOrWhiteSpace(runtimeDirectory))
            {
                string? directoryName = Path.GetFileName(runtimeDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (string.Equals(directoryName, SharedDirectoryName, StringComparison.OrdinalIgnoreCase))
                {
                    string? parent = Path.GetDirectoryName(runtimeDirectory);
                    string normalizedParent = NormalizeDir(parent);
                    if (!string.IsNullOrWhiteSpace(normalizedParent))
                    {
                        productRootDirectory = normalizedParent;
                    }
                }
            }

            return new AppRuntimeLayout
            {
                ProductRootDirectory = productRootDirectory,
                RuntimeDirectory = runtimeDirectory,
                PortableDataDirectory = CombineIfPossible(productRootDirectory, "data"),
                LauncherExecutablePath = CombineIfPossible(productRootDirectory, "WindBoard.exe"),
                CrashReporterExecutablePath = CombineIfPossible(runtimeDirectory, "WindBoard.CrashReporter.exe"),
            };
        }

        private static string CombineIfPossible(string directory, string fileOrChildName)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                return string.Empty;
            }

            return Path.Combine(directory, fileOrChildName);
        }

        private static string NormalizeDir(string? dir)
        {
            if (string.IsNullOrWhiteSpace(dir))
            {
                return string.Empty;
            }

            try
            {
                string full = Path.GetFullPath(dir.Trim());
                return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return dir.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
        }
    }
}
