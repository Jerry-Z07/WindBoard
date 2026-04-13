using System;
using System.IO;
using System.Text.RegularExpressions;
using WindBoard.Logging;
using WindBoard.Updates;

namespace WindBoard.Persistence
{
    /// <summary>
    /// 应用数据自动清理：在启动时清理过期的下载安装包与伪装图标缓存。
    /// </summary>
    internal static class AppDataCleanup
    {
        /// <summary>
        /// 安装包文件名中的版本号提取正则。
        /// 匹配格式：WindBoardSetup-2.0.0-win-x64.exe / WindBoard-2.0.0-win-x64.zip 等。
        /// </summary>
        private static readonly Regex InstallerVersionPattern = new(
            @"^WindBoard(?:Setup)?-(\d+\.\d+\.\d+(?:-[0-9A-Za-z\-.]+)?)-win-",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        /// <summary>
        /// 默认图标缓存文件名中的版本号提取正则。
        /// 匹配格式：default_2.0.0.ico / default_2.0.0-beta.1.ico 等。
        /// </summary>
        private static readonly Regex DefaultIconVersionPattern = new(
            @"^default_(\d+\.\d+\.\d+(?:-[0-9A-Za-z\-.]+)?)\.ico$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        /// <summary>
        /// 执行启动时清理。
        /// </summary>
        internal static void Run()
        {
            string currentVersion = AppInfo.Version;
            if (string.IsNullOrWhiteSpace(currentVersion) || string.Equals(currentVersion, "unknown", StringComparison.OrdinalIgnoreCase))
            {
                // 版本号不可用时不执行清理，避免误删。
                return;
            }

            if (!SemanticVersion.TryParse(currentVersion, out SemanticVersion current))
            {
                return;
            }

            CleanupOldInstallerDownloads(current);
            CleanupOldDefaultIconCache(current);
        }

        /// <summary>
        /// 清理 downloads 目录中版本号小于当前版本的安装包/压缩包。
        /// </summary>
        private static void CleanupOldInstallerDownloads(SemanticVersion currentVersion)
        {
            string downloadsDir = AppDataPaths.DownloadsDirectory;
            if (string.IsNullOrWhiteSpace(downloadsDir))
            {
                return;
            }

            CleanupFilesWithVersion(downloadsDir, InstallerVersionPattern, currentVersion, "Downloads");
        }

        /// <summary>
        /// 清理 camouflage 目录中旧版本的默认图标缓存（default_x.x.x.ico），仅保留当前版本。
        /// </summary>
        private static void CleanupOldDefaultIconCache(SemanticVersion currentVersion)
        {
            string cacheDir = AppDataPaths.CamouflageCacheDirectory;
            if (string.IsNullOrWhiteSpace(cacheDir))
            {
                return;
            }

            CleanupFilesWithVersion(cacheDir, DefaultIconVersionPattern, currentVersion, "Camouflage");
        }

        /// <summary>
        /// 通用清理逻辑：扫描目录下匹配正则的文件，若版本号严格小于当前版本则删除。
        /// </summary>
        private static void CleanupFilesWithVersion(string directory, Regex versionPattern, SemanticVersion currentVersion, string logCategory)
        {
            try
            {
                if (!Directory.Exists(directory))
                {
                    return;
                }

                int deletedCount = 0;
                foreach (string file in Directory.EnumerateFiles(directory))
                {
                    string fileName = Path.GetFileName(file);
                    Match match = versionPattern.Match(fileName);
                    if (!match.Success)
                    {
                        continue;
                    }

                    string versionText = match.Groups[1].Value;
                    if (!SemanticVersion.TryParse(versionText, out SemanticVersion fileVersion))
                    {
                        continue;
                    }

                    // 仅清理严格小于当前版本的文件；等于当前版本的不清理。
                    if (fileVersion.CompareTo(currentVersion) >= 0)
                    {
                        continue;
                    }

                    try
                    {
                        File.Delete(file);
                        deletedCount++;
                    }
                    catch
                    {
                        // 删除失败（文件占用/权限等）不影响后续清理。
                    }
                }

                if (deletedCount > 0)
                {
                    AppLog.Info(logCategory, $"已清理 {deletedCount} 个旧版本文件：directory='{directory}', currentVersion={currentVersion}");
                }
            }
            catch (Exception ex)
            {
                // 清理失败不影响主流程。
                AppLog.Warn(logCategory, $"清理旧版本文件失败：directory='{directory}'", ex);
            }
        }

        // ---- 以下方法供单元测试直接调用 ----

        internal static Match? TryMatchInstallerVersion(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return null;
            }

            Match match = InstallerVersionPattern.Match(fileName);
            return match.Success ? match : null;
        }

        internal static Match? TryMatchDefaultIconVersion(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return null;
            }

            Match match = DefaultIconVersionPattern.Match(fileName);
            return match.Success ? match : null;
        }
    }
}
