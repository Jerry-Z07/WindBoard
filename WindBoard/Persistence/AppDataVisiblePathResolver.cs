using System;
using System.IO;
using WindBoard.Logging;
using WindBoard.Updates;

namespace WindBoard.Persistence
{
    /// <summary>
    /// 把「应用自身使用的友好路径」推导为「外部进程（资源管理器 / 默认关联程序）可见的真实路径」。
    ///
    /// 背景（MSIX 打包形态，详见 .trellis/spec/backend/packaging-guidelines.md）：
    /// - 打包进程内 <c>%LOCALAPPDATA%\WindBoard\...</c> 的新建内容会被系统重定向到包私有位置，
    ///   且「合入真实 AppData 视图」只在**应用进程内**生效（官方 windows/msix/desktop/desktop-to-uwp-behind-the-scenes：
    ///   “merged at runtime to appear in the real AppData location”）；
    /// - 外部进程看到的是真实的 <c>%LOCALAPPDATA%</c>，那里并没有这些文件，因此把友好路径交给外部进程会「找不到路径」；
    /// - 重定向落点写作 <c>&lt;LocalCache&gt;\Local\...</c>（官方 windows/msix/msix-troubleshooting-guide 的
    ///   <c>%LocalAppData%\Packages\&lt;PFN&gt;\LocalCache\Local\VFS\</c> 示例 + 本机实测日志落点）。
    ///
    /// 因此「打开 / 复制」交给外部前必须先映射；映射失败由调用方给出失败反馈，不得静默回退到友好路径。
    /// </summary>
    internal static class AppDataVisiblePathResolver
    {
        /// <summary>
        /// 重定向落点中 LocalCache 之下的固定子目录名：<c>%LOCALAPPDATA%</c> → <c>&lt;LocalCache&gt;\Local</c>。
        /// 注意：该层由「官方文档示例 + 本机实测」共同支撑，**不是 API 契约**，改变量前请先看 spec 的落点契约与真机验证结论。
        /// </summary>
        private const string LocalSubdirectoryName = "Local";

        /// <summary>
        /// 纯路径映射（不做文件系统访问，便于单测）。
        /// </summary>
        /// <param name="friendlyPath">应用内部使用的友好路径（如 <c>%LOCALAPPDATA%\WindBoard\Logs</c>）。</param>
        /// <param name="isPackaged">当前进程是否具有包身份（MSIX）；false 时友好路径即外部可见路径。</param>
        /// <param name="friendlyLocalAppDataRoot">打包进程内 <c>LocalApplicationData</c> 的取值（即重定向前的根）。</param>
        /// <param name="localCacheRoot">包私有 LocalCache 根（<c>ApplicationData.LocalCachePath</c>）；仅打包分支使用。</param>
        /// <param name="visiblePath">映射结果；失败时为空字符串。</param>
        /// <returns>映射成功返回 true；无法映射（入参为空、不在重定向根之下、缺少 LocalCache 根）返回 false。</returns>
        internal static bool TryMap(
            string friendlyPath,
            bool isPackaged,
            string friendlyLocalAppDataRoot,
            string? localCacheRoot,
            out string visiblePath)
        {
            visiblePath = string.Empty;

            if (string.IsNullOrWhiteSpace(friendlyPath))
            {
                return false;
            }

            // 未打包（便携版 / 开发运行 / 旧安装版）：友好路径就是真实路径，原样返回（仅去掉首尾空白）。
            if (!isPackaged)
            {
                visiblePath = friendlyPath.Trim();
                return true;
            }

            if (string.IsNullOrWhiteSpace(friendlyLocalAppDataRoot))
            {
                return false;
            }

            // 统一分隔符后再比较/取尾段：允许入参混用 "/" 与 "\"（Path.Combine 产出与手工拼接结果都能处理）。
            string root = TrimTrailingSeparators(NormalizeSeparators(friendlyLocalAppDataRoot.Trim()));
            string path = TrimTrailingSeparators(NormalizeSeparators(friendlyPath.Trim()));

            if (!TryGetRelativeTail(path, root, out string tail))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(localCacheRoot))
            {
                return false;
            }

            string cacheRoot = TrimTrailingSeparators(localCacheRoot.Trim());
            if (cacheRoot.Length == 0)
            {
                return false;
            }

            // 虚拟化落点：<LocalCache>\Local\<相对 %LOCALAPPDATA% 的尾段>
            visiblePath = tail.Length == 0
                ? Path.Combine(cacheRoot, LocalSubdirectoryName)
                : Path.Combine(cacheRoot, LocalSubdirectoryName, tail);
            return true;
        }

        /// <summary>
        /// 组合入口：按当前安装形态把友好路径推导为外部可见路径。
        ///
        /// 说明：
        /// - 未打包形态不触碰 WinRT / WinAppSDK，直接返回友好路径；
        /// - 打包形态才读取 <c>Microsoft.Windows.Storage.ApplicationData.GetDefault().LocalCachePath</c>
        ///   （由本工程既有依赖 <c>Microsoft.WindowsAppSDK</c> 提供，官方语义等价于
        ///   <c>Windows.Storage.ApplicationData.LocalCacheFolder</c> 的路径）；
        /// - 任何异常（例如该 API 在 mediumIL 打包桌面应用中不可用）一律降级为 false，由调用方给出失败反馈。
        /// </summary>
        internal static bool TryResolve(string friendlyPath, out string visiblePath)
        {
            try
            {
                if (!AppInstallProbe.IsPackagedProcess())
                {
                    return TryMap(
                        friendlyPath,
                        isPackaged: false,
                        friendlyLocalAppDataRoot: string.Empty,
                        localCacheRoot: null,
                        out visiblePath);
                }

                string friendlyRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string? localCacheRoot = Microsoft.Windows.Storage.ApplicationData.GetDefault().LocalCachePath;

                return TryMap(friendlyPath, isPackaged: true, friendlyRoot, localCacheRoot, out visiblePath);
            }
            catch (Exception ex)
            {
                // 降级策略：无法推导外部可见路径时返回 false（调用方不得再使用友好路径去打开/复制）。
                AppLog.Warn("Persistence", $"推导外部可见路径失败：path='{friendlyPath}'", ex);
                visiblePath = string.Empty;
                return false;
            }
        }

        /// <summary>
        /// 判断 <paramref name="path"/> 是否位于 <paramref name="root"/> 之下，并取出相对尾段。
        /// 比较不区分大小写（Windows 路径），且要求目录边界，避免 “…\LocalX” 被误判为在 “…\Local” 之下。
        /// </summary>
        private static bool TryGetRelativeTail(string path, string root, out string tail)
        {
            tail = string.Empty;

            if (root.Length == 0 || !path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (path.Length > root.Length && !IsSeparator(path[root.Length]))
            {
                return false;
            }

            // 尾段不含前导分隔符，也不带尾随分隔符，保证结果形如 <LocalCache>\Local\WindBoard\Logs。
            tail = path[root.Length..]
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return true;
        }

        private static string NormalizeSeparators(string path)
        {
            return path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        }

        private static bool IsSeparator(char c)
        {
            return c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar;
        }

        private static string TrimTrailingSeparators(string path)
        {
            string trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            // 盘符根（"C:\"）裁剪后会退化成 "C:"，拼接时会变成相对路径，这里还原。
            return trimmed.EndsWith(':') ? string.Concat(trimmed, Path.DirectorySeparatorChar) : trimmed;
        }
    }
}
