using System;
using System.IO;
using WindBoard.Updates;

namespace WindBoard.Persistence
{
    /// <summary>
    /// 应用数据路径入口：根据安装形态（MSIX/安装版/便携版）决定数据落盘目录。
    ///
    /// 约定：
    /// - MSIX（Store）版与安装版：%LocalAppData%\WindBoard（MSIX 下的“包私有重定向”由系统虚拟化处理，路径表达式不变）
    /// - 便携版：{AppContext.BaseDirectory}\data
    /// - 便携版但 data 目录不可写：自动回退到 %LocalAppData%\WindBoard（避免启动失败）
    /// </summary>
    internal static class AppDataPaths
    {
        private static readonly object Gate = new();
        private static AppDataPathsSnapshot? _cached;

        internal static string RootDirectory => GetSnapshot().RootDirectory;
        internal static string OwnDataDirectory => GetSnapshot().OwnDataDirectory;
        internal static string LegacyInstallerDataDirectory => GetSnapshot().LegacyInstallerDataDirectory;
        internal static string SettingsFilePath => GetSnapshot().SettingsFilePath;
        internal static string LogsDirectory => GetSnapshot().LogsDirectory;
        internal static string CamouflageCacheDirectory => GetSnapshot().CamouflageCacheDirectory;
        internal static string DownloadsDirectory => GetSnapshot().DownloadsDirectory;

        internal static AppInstallKind InstallKind => GetSnapshot().InstallKind;
        internal static string InstallEvidence => GetSnapshot().InstallEvidence;
        internal static string InstallDir => GetSnapshot().InstallDir;

        internal static bool UsingPortableDataDirectory => GetSnapshot().UsingPortableDataDirectory;
        internal static string PortableDataDirectory => GetSnapshot().PortableDataDirectory;
        internal static bool PortableDataDirectoryWritable => GetSnapshot().PortableDataDirectoryWritable;
        internal static string? PortableDataDirectoryWriteTestError => GetSnapshot().PortableDataDirectoryWriteTestError;

        internal static AppDataPathsSnapshot GetSnapshot()
        {
            lock (Gate)
            {
                _cached ??= ComputeSnapshot(
                    install: AppInstallProbe.ProbeNoLog(),
                    appBaseDirectory: AppContext.BaseDirectory,
                    localAppDataDirectory: Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    localAppDataEnvironmentValue: Environment.GetEnvironmentVariable("LOCALAPPDATA"),
                    tryEnsureWritable: TryEnsureDirectoryWritable);

                return _cached;
            }
        }

        /// <summary>
        /// 计算数据目录快照（便于单元测试注入 install/baseDir/localAppData 与可写性探测）。
        /// </summary>
        /// <param name="localAppDataDirectory"><c>SpecialFolder.LocalApplicationData</c> 的取值（自身读写落点用）。</param>
        /// <param name="localAppDataEnvironmentValue">
        /// 环境变量 <c>LOCALAPPDATA</c> 的原始取值，仅用于计算“旧安装版数据目录”（迁移只读来源）。
        /// 打包进程内 <c>GetFolderPath</c> 的返回值不确定（design.md §1.2 需实测项 V5），因此优先信任环境变量。
        /// </param>
        internal static AppDataPathsSnapshot ComputeSnapshot(
            AppInstallProbeResult install,
            string appBaseDirectory,
            string localAppDataDirectory,
            string? localAppDataEnvironmentValue,
            Func<string, (bool ok, string? errorMessage)> tryEnsureWritable)
        {
            ArgumentNullException.ThrowIfNull(install);

            ArgumentNullException.ThrowIfNull(tryEnsureWritable);

            string baseDir = NormalizeDir(appBaseDirectory);
            string localAppData = NormalizeDir(localAppDataDirectory);
            AppRuntimeLayout layout = AppRuntimeLayout.Resolve(baseDir);

            // “自身读写落点”的路径表达式：安装版与 MSIX 版同为 %LocalAppData%\WindBoard。
            // MSIX 下新建文件由系统重定向到包私有位置、读取先私有后回落，这里不引入 opt-out、不改写路径算法。
            string localRoot = Path.Combine(localAppData, "WindBoard");

            // 旧 Inno 安装版的真实数据目录（仅迁移时读取，不写回）：
            // 优先用环境变量 LOCALAPPDATA 计算（打包进程内 GetFolderPath 的返回值不确定），取不到时回退注入值。
            string legacyLocalAppData = NormalizeDir(
                string.IsNullOrWhiteSpace(localAppDataEnvironmentValue) ? localAppData : localAppDataEnvironmentValue);
            string legacyLocalRoot = string.IsNullOrWhiteSpace(legacyLocalAppData)
                ? string.Empty
                : Path.Combine(legacyLocalAppData, "WindBoard");

            string portableRoot = layout.PortableDataDirectory;

            bool attemptedPortable = install.Kind == AppInstallKind.Portable;
            bool portableWritable = false;
            string? portableError = null;

            string root;
            if (install.Kind == AppInstallKind.Installer || install.Kind == AppInstallKind.Msix)
            {
                root = localRoot;
            }
            else if (!attemptedPortable)
            {
                // 兜底：未知类型按“安装版”处理，避免误把数据写进安装目录。
                root = localRoot;
            }
            else if (string.IsNullOrWhiteSpace(portableRoot))
            {
                root = localRoot;
                portableError = "AppContext.BaseDirectory 为空或不可用。";
            }
            else
            {
                (portableWritable, portableError) = tryEnsureWritable(portableRoot);
                root = portableWritable ? portableRoot : localRoot;
            }

            // 极端环境兜底：LocalAppData 为空时，尽量回落到 base\data。
            if (string.IsNullOrWhiteSpace(root))
            {
                root = portableRoot;
            }

            string settingsPath = Path.Combine(root, "settings.json");
            string logsDir = Path.Combine(root, "Logs");
            string camouflageDir = Path.Combine(root, "camouflage");
            string downloadsDir = Path.Combine(root, "downloads");

            return new AppDataPathsSnapshot
            {
                InstallKind = install.Kind,
                InstallEvidence = (install.Evidence ?? string.Empty).Trim(),
                InstallDir = (install.InstallDir ?? string.Empty).Trim(),
                AppBaseDirectory = baseDir,

                LocalAppDataRootDirectory = localRoot,
                LocalAppDataSettingsFilePath = Path.Combine(localRoot, "settings.json"),

                OwnDataDirectory = root,
                LegacyInstallerDataDirectory = legacyLocalRoot,

                PortableDataDirectory = portableRoot,
                PortableDataDirectoryAttempted = attemptedPortable,
                PortableDataDirectoryWritable = portableWritable,
                PortableDataDirectoryWriteTestError = portableError,

                RootDirectory = root,
                UsingPortableDataDirectory = attemptedPortable && portableWritable && IsSameDirectory(root, portableRoot),

                SettingsFilePath = settingsPath,
                LogsDirectory = logsDir,
                CamouflageCacheDirectory = camouflageDir,
                DownloadsDirectory = downloadsDir,
            };
        }
        private static (bool ok, string? errorMessage) TryEnsureDirectoryWritable(string directory)
        {
            string dir = (directory ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(dir))
            {
                return (false, "目录为空。");
            }

            string probeFile = Path.Combine(dir, $".wb_write_test_{Environment.ProcessId}_{Guid.NewGuid():N}.tmp");
            try
            {
                Directory.CreateDirectory(dir);

                // 通过“创建 + 写入 + 删除”探测可写性，避免仅凭 Directory.Exists 误判。
                using (var fs = new FileStream(probeFile, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    fs.WriteByte(0);
                    fs.Flush(flushToDisk: true);
                }

                try
                {
                    File.Delete(probeFile);
                }
                catch
                {
                    // 删除失败不等于不可写：忽略，避免影响主流程。
                }

                return (true, null);
            }
            catch (Exception ex)
            {
                try
                {
                    if (File.Exists(probeFile))
                    {
                        File.Delete(probeFile);
                    }
                }
                catch
                {
                    // 忽略清理异常
                }

                return (false, ex.GetType().Name + ": " + ex.Message);
            }
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

        private static bool IsSameDirectory(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            {
                return false;
            }

            return string.Equals(a.TrimEnd('\\', '/'), b.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
        }
    }

    internal sealed class AppDataPathsSnapshot
    {
        internal AppInstallKind InstallKind { get; init; } = AppInstallKind.Unknown;
        internal string InstallEvidence { get; init; } = string.Empty;
        internal string InstallDir { get; init; } = string.Empty;
        internal string AppBaseDirectory { get; init; } = string.Empty;

        internal string LocalAppDataRootDirectory { get; init; } = string.Empty;
        internal string LocalAppDataSettingsFilePath { get; init; } = string.Empty;

        /// <summary>
        /// 自身读写落点（= <see cref="AppDataPaths.RootDirectory"/>，语义化别名，供迁移/诊断对照）。
        /// </summary>
        internal string OwnDataDirectory { get; init; } = string.Empty;

        /// <summary>
        /// 旧 Inno 安装版的真实数据目录（真实 %LOCALAPPDATA%\WindBoard）；仅迁移时读取，不写回。
        /// </summary>
        internal string LegacyInstallerDataDirectory { get; init; } = string.Empty;

        internal string PortableDataDirectory { get; init; } = string.Empty;
        internal bool PortableDataDirectoryAttempted { get; init; }
        internal bool PortableDataDirectoryWritable { get; init; }
        internal string? PortableDataDirectoryWriteTestError { get; init; }

        internal string RootDirectory { get; init; } = string.Empty;
        internal bool UsingPortableDataDirectory { get; init; }

        internal string SettingsFilePath { get; init; } = string.Empty;
        internal string LogsDirectory { get; init; } = string.Empty;
        internal string CamouflageCacheDirectory { get; init; } = string.Empty;
        internal string DownloadsDirectory { get; init; } = string.Empty;
    }
}
