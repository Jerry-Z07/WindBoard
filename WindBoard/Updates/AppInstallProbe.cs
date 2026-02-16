using System;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using WindBoard.Logging;

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
            string baseDir = NormalizeDir(AppContext.BaseDirectory);

            // 1) 注册表标记：由安装包写入，能够区分自包含与 -fd 变体。
            try
            {
                using RegistryKey? key = Registry.LocalMachine.OpenSubKey(RegistryPath, writable: false);
                if (key is not null)
                {
                    string installDir = NormalizeDir(key.GetValue(ValueInstallDir) as string);
                    string kind = (key.GetValue(ValueInstallKind) as string ?? string.Empty).Trim();
                    string variant = (key.GetValue(ValueInstallVariant) as string ?? string.Empty).Trim();

                    if (IsSameDirectory(installDir, baseDir) && kind.Equals(KindInstaller, StringComparison.OrdinalIgnoreCase))
                    {
                        AppInstallVariant v = variant.Equals(VariantFrameworkDependent, StringComparison.OrdinalIgnoreCase)
                            ? AppInstallVariant.FrameworkDependent
                            : variant.Equals(VariantSelfContained, StringComparison.OrdinalIgnoreCase)
                                ? AppInstallVariant.SelfContained
                                : AppInstallVariant.Unknown;

                        return new AppInstallProbeResult
                        {
                            Kind = AppInstallKind.Installer,
                            Variant = v,
                            Evidence = "registry",
                            InstallDir = installDir,
                        };
                    }

                    // 标记存在但与当前路径不匹配：可能是旧安装残留，避免误判。
                    if (!string.IsNullOrWhiteSpace(installDir) && !IsSameDirectory(installDir, baseDir))
                    {
                        AppLog.Debug("Updates", $"检测到安装标记但路径不匹配，将忽略：installDir='{installDir}', baseDir='{baseDir}'");
                    }
                }
            }
            catch (Exception ex)
            {
                // 探测失败不影响主流程：记录一次日志，后续走兜底策略。
                AppLog.Warn("Updates", "读取安装标记失败，将使用兜底探测", ex);
            }

            // 2) 兜底：Inno Setup 安装目录通常会包含卸载器（unins*.exe）。
            try
            {
                if (Directory.Exists(baseDir))
                {
                    bool hasUninstaller = Directory.EnumerateFiles(baseDir, "unins*.exe", SearchOption.TopDirectoryOnly).Any();
                    if (hasUninstaller)
                    {
                        return new AppInstallProbeResult
                        {
                            Kind = AppInstallKind.Installer,
                            Variant = AppInstallVariant.Unknown,
                            Evidence = "uninstaller-file",
                            InstallDir = baseDir,
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn("Updates", "兜底探测卸载器失败，将按便携版处理", ex);
            }

            return new AppInstallProbeResult
            {
                Kind = AppInstallKind.Portable,
                Variant = AppInstallVariant.Unknown,
                Evidence = "fallback",
                InstallDir = baseDir,
            };
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

