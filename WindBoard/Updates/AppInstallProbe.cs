using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using WindBoard.Logging;
using WindBoard.Persistence;

namespace WindBoard.Updates
{
    /// <summary>
    /// 当前运行实例的安装形态探测：
    /// - 优先做“包身份”探测（MSIX/Store 版）：packaged 进程直接判定，注册表/卸载器探测在包内不可用
    /// - 其次读取安装包写入的注册表标记（可区分 -fd）
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

        // Win32 GetCurrentPackageFullName 在非打包进程中的返回值（appmodel.h: APPMODEL_ERROR_NO_PACKAGE）。
        private const int AppModelErrorNoPackage = 15700;

        // ERROR_INSUFFICIENT_BUFFER：传入的缓冲区不足，说明包身份存在。
        private const int ErrorInsufficientBuffer = 122;

        private static readonly object PackagedGate = new();
        private static bool? _isPackagedProcess;

        /// <summary>
        /// 当前进程是否具有包身份（MSIX/packaged）。
        ///
        /// 说明：必须用 Win32 <c>GetCurrentPackageFullName</c> 判定，不能用 <c>Windows.ApplicationModel.Package.Current</c>：
        /// 本探测会经 <see cref="ProbeNoLog"/> 在“极早期”（AppLog 尚未配置）被 AppDataPaths 调用，
        /// 此时拉起 WinRT 激活有初始化顺序风险；Win32 API 无激活、开销极低（非打包进程返回 APPMODEL_ERROR_NO_PACKAGE）。
        /// 结果缓存：该探测会被多次调用。
        /// </summary>
        internal static bool IsPackagedProcess()
        {
            lock (PackagedGate)
            {
                _isPackagedProcess ??= ProbeIsPackagedProcess();
                return _isPackagedProcess.Value;
            }
        }

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

        /// <summary>
        /// 读取旧 Inno 安装版写入的安装目录标记（<c>HKLM\SOFTWARE\WindBoard\InstallDir</c>）。
        ///
        /// 说明：
        /// - 打包（MSIX）进程读 HKLM 得到的是“包内 hive + 系统 hive”的合并视图，允许读取；
        /// - 但该值**不能**作为形态判定依据（同 <see cref="ProbeCore"/> 的取舍），仅用于迁移提示里展示旧安装路径；
        /// - 读取失败只影响展示：记录日志后返回空字符串。
        /// </summary>
        internal static string TryReadInstallerRegistryInstallDir()
        {
            try
            {
                using RegistryKey? key = Registry.LocalMachine.OpenSubKey(RegistryPath, writable: false);
                return key is null
                    ? string.Empty
                    : NormalizeDir(key.GetValue(ValueInstallDir) as string);
            }
            catch (Exception ex)
            {
                AppLog.Warn("Updates", "读取旧安装版安装目录标记失败", ex);
                return string.Empty;
            }
        }

        private static AppInstallProbeResult ProbeCore(bool enableLogging)
        {
            string baseDir = NormalizeDir(AppContext.BaseDirectory);
            AppRuntimeLayout layout = AppRuntimeLayout.Resolve(baseDir);
            string productRootDirectory = layout.ProductRootDirectory;

            // 1) 包身份优先：MSIX（Store）版进程下，注册表标记与卸载器文件都不存在/不可用，
            //    直接判定为 Msix，跳过后续 I/O。
            bool isPackagedProcess = IsPackagedProcess();
            if (isPackagedProcess)
            {
                return ComputeProbeResult(
                    productRootDirectory: productRootDirectory,
                    isPackagedProcess: true,
                    registryInstallDir: string.Empty,
                    registryInstallKind: string.Empty,
                    registryInstallVariant: string.Empty,
                    hasUninstallerInProductRoot: false);
            }

            string registryInstallDir = string.Empty;
            string registryKind = string.Empty;
            string registryVariant = string.Empty;

            // 2) 注册表标记：由安装包写入，能够区分自包含与 -fd 变体。
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

            // 3) 兜底：Inno Setup 安装目录通常会包含卸载器（unins*.exe）。
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
                isPackagedProcess: isPackagedProcess,
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

        /// <summary>
        /// 安装形态判定（纯逻辑，便于单元测试）。
        /// </summary>
        /// <param name="isPackagedProcess">
        /// 是否具有包身份（MSIX/packaged）。该值为探测所得的“已发生事实”，由调用方注入，
        /// 以便单测覆盖 <see cref="AppInstallKind.Msix"/> 分支（真实 API 结果无法在测试中伪造）。
        /// </param>
        internal static AppInstallProbeResult ComputeProbeResult(
            string productRootDirectory,
            bool isPackagedProcess,
            string registryInstallDir,
            string registryInstallKind,
            string registryInstallVariant,
            bool hasUninstallerInProductRoot)
        {
            // ProbeCore 已完成布局解析与 I/O；这里仅保留纯判定逻辑，便于单测。

            // 最高优先：packaged 进程即 MSIX（Store）版。此时注册表标记/卸载器兜底均无意义。
            // Variant 保持 Unknown：Store 只分发 self-contained，无需区分变体。
            if (isPackagedProcess)
            {
                return new AppInstallProbeResult
                {
                    Kind = AppInstallKind.Msix,
                    Variant = AppInstallVariant.Unknown,
                    Evidence = "package-identity",
                    InstallDir = productRootDirectory,
                };
            }

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

        private static bool ProbeIsPackagedProcess()
        {
            try
            {
                // 传空缓冲区只为取判定结果：
                // - packaged 进程：返回 ERROR_SUCCESS 或 ERROR_INSUFFICIENT_BUFFER（需要缓冲区）
                // - 非打包进程：返回 APPMODEL_ERROR_NO_PACKAGE
                uint length = 0;
                int result = GetCurrentPackageFullName(ref length, IntPtr.Zero);
                if (result == AppModelErrorNoPackage)
                {
                    return false;
                }

                return result == 0 || result == ErrorInsufficientBuffer;
            }
            catch (Exception)
            {
                // 极端环境下 API 不可用时按“非打包进程”处理，保持改动前的行为（→ 便携版/安装版链路）。
                // 这里不记日志：本方法会经 ProbeNoLog 在 AppLog 就绪前被调用。
                return false;
            }
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

        // Win32：判定当前进程是否具有包身份（MSIX）。kernel32 自 Windows 8 起提供，始终可用。
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int GetCurrentPackageFullName(ref uint packageFullNameLength, IntPtr packageFullName);
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
        /// 探测证据（package-identity/registry/uninstaller-file/fallback）。
        /// </summary>
        internal string Evidence { get; init; } = string.Empty;
    }

    internal enum AppInstallKind
    {
        Unknown,
        Installer,
        Portable,

        /// <summary>
        /// 通过 Microsoft Store 分发的 MSIX 打包版（packaged）。
        /// </summary>
        Msix,
    }

    internal enum AppInstallVariant
    {
        Unknown,
        SelfContained,
        FrameworkDependent,
    }
}

