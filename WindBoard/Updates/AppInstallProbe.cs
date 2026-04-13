using System;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using WindBoard.Logging;
using WindBoard.Persistence;

namespace WindBoard.Updates
{
    /// <summary>
    /// 当前运行实例的安装形态探测：
    /// - 优先读取安装包写入的注册表标记（最可靠，可区分 -fd）
    /// - 若标记缺失（旧版本安装包），则尝试通过 Inno Setup 卸载器文件做兜底判断
    /// - 都无法判断时，视为便携版（可下载 zip）
    /// </summary>
    internal static class AppInstallProbe
    {
        private const string RegistryPath = @"SOFTWARE\WindBoard";

        private const string ValueInstallKind = "InstallKind";
        private const string ValueInstallVariant = "InstallVariant";
        private const string ValueInstallDir = "InstallDir";

        private const string KindInstaller = "installer";
        private const string VariantSelfContained = "self-contained";
        private const string VariantFrameworkDependent = "framework-dependent";

        internal static AppInstallProbeResult Probe()
        {
            // 默认探测：允许记录日志（便于排查安装标记异常/注册表访问问题）。
            return ProbeCore(enableLogging: true);
        }

        /// <summary>
        /// 探测安装形态（不输出日志）。
        /// 
        /// 说明：
        /// - 该方法用于“非常早期”的路径选择场景（例如日志/设置默认路径），避免在 AppLog 尚未完成配置时发生递归初始化。
        /// - 发生异常时会直接走兜底策略，不记录 AppLog。
        /// </summary>
        internal static AppInstallProbeResult ProbeNoLog()
        {
            return ProbeCore(enableLogging: false);
        }

        private static AppInstallProbeResult ProbeCore(bool enableLogging)
        {
            string baseDir = NormalizeDir(AppContext.BaseDirectory);
            AppRuntimeLayout layout = AppRuntimeLayout.Resolve(baseDir);
            string productRootDirectory = layout.ProductRootDirectory;

            string registryInstallDir = string.Empty;
            string registryKind = string.Empty;
            string registryVariant = string.Empty;

            // 1) 注册表标记：由安装包写入，能够区分自包含与 -fd 变体。
            try
            {
                using RegistryKey? key = Registry.LocalMachine.OpenSubKey(RegistryPath, writable: false);
                if (key is not null)
                {
                    registryInstallDir = NormalizeDir(key.GetValue(ValueInstallDir) as string);
                    registryKind = (key.GetValue(ValueInstallKind) as string ?? string.Empty).Trim();
                    registryVariant = (key.GetValue(ValueInstallVariant) as string ?? string.Empty).Trim();
                }
            }
            catch (Exception ex)
            {
                // 探测失败不影响主流程：记录一次日志，后续走兜底策略。
                if (enableLogging)
                {
                    AppLog.Warn("Updates", "读取安装标记失败，将使用兜底探测", ex);
                }
            }

            bool hasUninstallerInProductRoot = false;

            // 2) 兜底：Inno Setup 安装目录通常会包含卸载器（unins*.exe）。
            try
            {
                if (Directory.Exists(productRootDirectory))
                {
                    hasUninstallerInProductRoot = Directory.EnumerateFiles(productRootDirectory, "unins*.exe", SearchOption.TopDirectoryOnly).Any();
                }
            }
            catch (Exception ex)
            {
                if (enableLogging)
                {
                    AppLog.Warn("Updates", "兜底探测卸载器失败，将按便携版处理", ex);
                }
            }

            AppInstallProbeResult result = ComputeProbeResult(
                productRootDirectory: productRootDirectory,
                registryInstallDir: registryInstallDir,
                registryInstallKind: registryKind,
                registryInstallVariant: registryVariant,
                hasUninstallerInProductRoot: hasUninstallerInProductRoot);

            if (enableLogging
                && !string.IsNullOrWhiteSpace(registryInstallDir)
                && !string.Equals(result.Evidence, "registry", StringComparison.OrdinalIgnoreCase)
                && !IsSameDirectory(registryInstallDir, productRootDirectory))
            {
                AppLog.Debug("Updates", $"检测到安装标记但路径不匹配，将忽略：installDir='{registryInstallDir}', productRoot='{productRootDirectory}'");
            }

            return result;
        }

        internal static AppInstallProbeResult ComputeProbeResult(
            string productRootDirectory,
            string registryInstallDir,
            string registryInstallKind,
            string registryInstallVariant,
            bool hasUninstallerInProductRoot)
        {
            // ProbeCore 已完成布局解析与 I/O；这里仅保留纯判定逻辑，便于单测。
            if (IsSameDirectory(registryInstallDir, productRootDirectory)
                && string.Equals((registryInstallKind ?? string.Empty).Trim(), KindInstaller, StringComparison.OrdinalIgnoreCase))
            {
                AppInstallVariant variant = NormalizeVariant(registryInstallVariant);
                return new AppInstallProbeResult
                {
                    Kind = AppInstallKind.Installer,
                    Variant = variant,
                    Evidence = "registry",
                    InstallDir = productRootDirectory,
                };
            }

            if (hasUninstallerInProductRoot)
            {
                return new AppInstallProbeResult
                {
                    Kind = AppInstallKind.Installer,
                    Variant = AppInstallVariant.Unknown,
                    Evidence = "uninstaller-file",
                    InstallDir = productRootDirectory,
                };
            }

            return new AppInstallProbeResult
            {
                Kind = AppInstallKind.Portable,
                Variant = AppInstallVariant.Unknown,
                Evidence = "fallback",
                InstallDir = productRootDirectory,
            };
        }

        private static AppInstallVariant NormalizeVariant(string? variant)
        {
            string value = (variant ?? string.Empty).Trim();
            return value.Equals(VariantFrameworkDependent, StringComparison.OrdinalIgnoreCase)
                ? AppInstallVariant.FrameworkDependent
                : value.Equals(VariantSelfContained, StringComparison.OrdinalIgnoreCase)
                    ? AppInstallVariant.SelfContained
                    : AppInstallVariant.Unknown;
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

            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }

    internal sealed class AppInstallProbeResult
    {
        internal AppInstallKind Kind { get; init; } = AppInstallKind.Unknown;

        internal AppInstallVariant Variant { get; init; } = AppInstallVariant.Unknown;

        /// <summary>
        /// 当前探测到的安装目录（或当前运行目录）。
        /// </summary>
        internal string InstallDir { get; init; } = string.Empty;

        /// <summary>
        /// 探测证据（registry/uninstaller-file/fallback）。
        /// </summary>
        internal string Evidence { get; init; } = string.Empty;
    }

    internal enum AppInstallKind
    {
        Unknown,
        Installer,
        Portable,
    }

    internal enum AppInstallVariant
    {
        Unknown,
        SelfContained,
        FrameworkDependent,
    }
}

