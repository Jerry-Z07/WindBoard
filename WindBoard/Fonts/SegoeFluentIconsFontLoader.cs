using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using WindBoard.Logging;
using WindBoard.Updates;

namespace WindBoard.Fonts
{
    /// <summary>
    /// Segoe Fluent Icons 字体可用性处理：
    /// - Win11（Build >= 22000）：系统自带字体，直接使用。
    /// - Win10：安装版由安装器写入系统字体；便携版使用 AddFontResourceEx 进行“进程私有加载”。
    ///
    /// 重要：
    /// - 本类只负责“让字体可用 + 统一设置 SymbolThemeFontFamily 资源”，不负责具体 Glyph 映射。
    /// - 若字体不可用，需降级到 Segoe MDL2 Assets，避免 UI 出现大量空白/方块。
    /// </summary>
    internal static class SegoeFluentIconsFontLoader
    {
        internal const string FluentFontFamilyName = "Segoe Fluent Icons";
        internal const string FallbackFontFamilyName = "Segoe MDL2 Assets";

        // Windows 11 首个公开 build：22000。
        internal const int Win11BuildThreshold = 22000;

        private static readonly object Gate = new();
        private static bool _initialized;

        private static int _windowsBuildNumber;
        private static AppInstallKind _installKind;
        private static bool _privateFontLoaded;
        private static string _effectiveIconFontFamilyName = FluentFontFamilyName;

        /// <summary>
        /// 当前进程应使用的图标字体名（可能是 Fluent，也可能降级为 MDL2）。
        /// </summary>
        internal static string EffectiveIconFontFamilyName
        {
            get
            {
                EnsureInitialized();
                return _effectiveIconFontFamilyName;
            }
        }

        /// <summary>
        /// 初始化字体可用性（幂等）。
        /// 说明：应尽量在 App.InitializeComponent() 之前调用，避免 XAML/渲染层过早创建图标控件导致缓存错误字体。
        /// </summary>
        internal static void InitializeForCurrentProcess()
        {
            EnsureInitialized();
        }

        /// <summary>
        /// 把最终选择的图标字体写入应用资源：
        /// - 通过覆盖 ThemeResource Key=SymbolThemeFontFamily
        /// - 让所有 SymbolIcon / SymbolIconSource 等统一使用该字体渲染
        /// </summary>
        internal static void ApplyToResources(ResourceDictionary resources)
        {
            if (resources is null)
            {
                throw new ArgumentNullException(nameof(resources));
            }

            EnsureInitialized();

            try
            {
                resources["SymbolThemeFontFamily"] = new FontFamily(_effectiveIconFontFamilyName);
                AppLog.Info(
                    "Fonts",
                    $"已应用图标字体资源：SymbolThemeFontFamily='{_effectiveIconFontFamilyName}', build={_windowsBuildNumber}, installKind={_installKind}, privateLoaded={_privateFontLoaded}");
            }
            catch (Exception ex)
            {
                // 资源写入失败会影响全局图标展示，但不应阻断启动；这里记录日志并继续。
                AppLog.Warn("Fonts", "应用 SymbolThemeFontFamily 资源失败，将使用系统默认资源", ex);
            }
        }

        internal static bool IsWin11OrLaterBuild(int windowsBuildNumber)
        {
            return windowsBuildNumber >= Win11BuildThreshold;
        }

        /// <summary>
        /// 选择最终使用的图标字体（纯逻辑，便于单元测试覆盖）。
        /// </summary>
        internal static string DecideEffectiveIconFontFamilyName(int windowsBuildNumber, bool fluentInstalled, bool privateFontLoaded)
        {
            if (IsWin11OrLaterBuild(windowsBuildNumber))
            {
                return FluentFontFamilyName;
            }

            if (fluentInstalled || privateFontLoaded)
            {
                return FluentFontFamilyName;
            }

            return FallbackFontFamilyName;
        }

        private static void EnsureInitialized()
        {
            lock (Gate)
            {
                if (_initialized)
                {
                    return;
                }

                InitializeCore();
                _initialized = true;
            }
        }

        private static void InitializeCore()
        {
            _windowsBuildNumber = TryGetWindowsBuildNumber(out int build) ? build : 0;

            // 探测安装形态：决定 Win10 下是否需要“私有加载 ttf”。
            AppInstallProbeResult install = AppInstallProbe.ProbeNoLog();
            _installKind = install.Kind;

            bool isWin11OrLater = IsWin11OrLaterBuild(_windowsBuildNumber);
            if (isWin11OrLater)
            {
                _effectiveIconFontFamilyName = DecideEffectiveIconFontFamilyName(_windowsBuildNumber, fluentInstalled: false, privateFontLoaded: false);
                AppLog.Info("Fonts", $"检测到 Win11+（build={_windowsBuildNumber}），将直接使用系统图标字体：{FluentFontFamilyName}");
                return;
            }

            // Win10：优先看系统是否已安装（安装器应当负责安装；但这里做兜底）。
            bool fluentInstalled = IsFontInstalledNoThrow(FluentFontFamilyName);
            if (fluentInstalled)
            {
                _effectiveIconFontFamilyName = DecideEffectiveIconFontFamilyName(_windowsBuildNumber, fluentInstalled: true, privateFontLoaded: false);
                AppLog.Info("Fonts", $"检测到系统已安装图标字体：{FluentFontFamilyName}（build={_windowsBuildNumber}, installKind={_installKind}）");
                return;
            }

            // Win10：系统未安装 Fluent 图标字体。
            // - 便携版：按需求“加载内置 ttf”（进程私有，不写系统）
            // - 安装版：理论上安装器已处理；若仍缺失，这里也尝试私有加载做兜底，避免 UI 变成方块
            if (_installKind == AppInstallKind.Installer)
            {
                AppLog.Warn(
                    "Fonts",
                    $"Win10 安装版检测到系统未安装 '{FluentFontFamilyName}'，将尝试从 Assets 私有加载做兜底：build={_windowsBuildNumber}, installDir='{install.InstallDir}'");
            }

            if (TryLoadPrivateFontFromAssets(out string? error))
            {
                _privateFontLoaded = true;
                _effectiveIconFontFamilyName = DecideEffectiveIconFontFamilyName(_windowsBuildNumber, fluentInstalled: false, privateFontLoaded: true);
                AppLog.Info(
                    "Fonts",
                    $"已私有加载图标字体：family='{FluentFontFamilyName}', build={_windowsBuildNumber}, installKind={_installKind}, assets='{GetAssetsFontFilePath()}'");
                return;
            }

            string err = string.IsNullOrWhiteSpace(error) ? "(unknown)" : error;
            AppLog.Warn(
                "Fonts",
                $"Segoe Fluent Icons 不可用，将降级为 '{FallbackFontFamilyName}'：build={_windowsBuildNumber}, installKind={_installKind}, error='{err}'");
            _effectiveIconFontFamilyName = DecideEffectiveIconFontFamilyName(_windowsBuildNumber, fluentInstalled: false, privateFontLoaded: false);
        }

        private static bool IsFontInstalledNoThrow(string fontFamilyName)
        {
            try
            {
                // System.Drawing 仅在 Windows 上可用，本项目已声明 UseSystemDrawing=true。
                using var installed = new System.Drawing.Text.InstalledFontCollection();
                return installed.Families.Any(f => string.Equals(f.Name, fontFamilyName, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                // 字体枚举失败不应阻断启动：记录日志并按“未安装”处理。
                AppLog.Warn("Fonts", $"检测系统字体列表失败，将按未安装处理：family='{fontFamilyName}'", ex);
                return false;
            }
        }

        private static string GetAssetsFontFilePath()
        {
            return Path.Combine(AppContext.BaseDirectory, "Assets", "Segoe Fluent Icons.ttf");
        }

        private static bool TryLoadPrivateFontFromAssets(out string? error)
        {
            error = null;

            string path = GetAssetsFontFilePath();
            if (!File.Exists(path))
            {
                error = $"font file not found: '{path}'";
                return false;
            }

            try
            {
                // FR_PRIVATE：仅当前进程可见；避免污染系统字体列表。
                // FR_NOT_ENUM：避免枚举（可选），降低其它枚举 API 的干扰。
                const uint flags = FR_PRIVATE | FR_NOT_ENUM;
                int added = AddFontResourceEx(path, flags, IntPtr.Zero);

                if (added <= 0)
                {
                    int lastError = Marshal.GetLastWin32Error();
                    error = $"AddFontResourceEx failed: added={added}, lastError={lastError}";
                    return false;
                }

                // 通知字体变化：某些组件可能在进程内缓存字体集合。
                // 说明：FR_PRIVATE 理论上只影响当前进程，但发送 WM_FONTCHANGE 对当前进程也有正向作用。
                _ = SendMessage(new IntPtr(HWND_BROADCAST), WM_FONTCHANGE, IntPtr.Zero, IntPtr.Zero);

                return true;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        private static bool TryGetWindowsBuildNumber(out int buildNumber)
        {
            buildNumber = 0;

            try
            {
                OSVERSIONINFOEX info = new()
                {
                    dwOSVersionInfoSize = (uint)Marshal.SizeOf<OSVERSIONINFOEX>(),
                };

                int status = RtlGetVersion(ref info);
                if (status == 0)
                {
                    buildNumber = unchecked((int)info.dwBuildNumber);
                    return buildNumber > 0;
                }
            }
            catch
            {
                // ignore
            }

            // 兜底：Environment.OSVersion 可能受 manifest/兼容性影响，但比“完全没有”强。
            try
            {
                buildNumber = Environment.OSVersion.Version.Build;
                return buildNumber > 0;
            }
            catch
            {
                return false;
            }
        }

        // -------- Win32 / NT API --------

        private const uint FR_PRIVATE = 0x10;
        private const uint FR_NOT_ENUM = 0x20;

        private const int WM_FONTCHANGE = 0x001D;
        private const int HWND_BROADCAST = 0xFFFF;

        [DllImport("gdi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern int AddFontResourceEx(string lpszFilename, uint fl, IntPtr pdv);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("ntdll.dll", CharSet = CharSet.Unicode)]
        private static extern int RtlGetVersion(ref OSVERSIONINFOEX versionInfo);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct OSVERSIONINFOEX
        {
            internal uint dwOSVersionInfoSize;
            internal uint dwMajorVersion;
            internal uint dwMinorVersion;
            internal uint dwBuildNumber;
            internal uint dwPlatformId;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            internal string szCSDVersion;

            internal ushort wServicePackMajor;
            internal ushort wServicePackMinor;
            internal ushort wSuiteMask;
            internal byte wProductType;
            internal byte wReserved;
        }
    }
}
