using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace WindBoard.UITests.Infrastructure
{
    /// <summary>
    /// Win32 顶层窗口枚举（EnumWindows）。
    /// 背景：Windows 11 的新版文件对话框（IFileDialog）不暴露在 UIA 桌面树的条件搜索结果中
    /// （desktop.FindAllDescendants 扫不到），必须经 Win32 句柄定位后再转成 UIA 元素操作。
    /// </summary>
    internal static class NativeWindowFinder
    {
        private delegate bool EnumWindowsProc(nint hWnd, nint lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(nint hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(nint hWnd, char[] lpClassName, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(nint hWnd, char[] lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern nint GetWindow(nint hWnd, uint uCmd);

        private const uint GW_OWNER = 4;

        /// <summary>枚举全部可见顶层窗口的句柄。</summary>
        public static IReadOnlyList<nint> EnumVisibleTopLevelWindows()
        {
            var handles = new List<nint>();

            EnumWindows((hWnd, _) =>
            {
                if (IsWindowVisible(hWnd))
                {
                    handles.Add(hWnd);
                }

                return true;
            }, nint.Zero);

            return handles;
        }

        public static uint GetProcessId(nint hWnd)
        {
            // 返回值（线程 id）无用途，显式丢弃以满足 CA1806。
            _ = GetWindowThreadProcessId(hWnd, out uint pid);
            return pid;
        }

        public static string GetClassName(nint hWnd)
        {
            // CA1838：P/Invoke 使用字符缓冲区而非 StringBuilder。
            var buffer = new char[256];
            _ = GetClassName(hWnd, buffer, buffer.Length);
            return BufferToString(buffer);
        }

        public static string GetWindowTitle(nint hWnd)
        {
            var buffer = new char[256];
            _ = GetWindowText(hWnd, buffer, buffer.Length);
            return BufferToString(buffer);
        }

        /// <summary>字符缓冲区按首个空字符截断为字符串（Win32 文本 API 以 '\0' 结尾）。</summary>
        private static string BufferToString(char[] buffer)
        {
            int length = Array.IndexOf(buffer, '\0');
            return length < 0 ? new string(buffer) : new string(buffer, 0, length);
        }

        /// <summary>按进程与窗口类名查找可见顶层窗口（类名不区分大小写）。</summary>
        public static IReadOnlyList<nint> FindWindows(int processId, string className)
        {
            var result = new List<nint>();

            foreach (nint hWnd in EnumVisibleTopLevelWindows())
            {
                if (GetProcessId(hWnd) != processId)
                {
                    continue;
                }

                if (!string.Equals(GetClassName(hWnd), className, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                result.Add(hWnd);
            }

            return result;
        }

        /// <summary>
        /// 枚举指定类名、排除给定窗口的可见顶层窗口。
        /// 说明：WinUI 弹层（ContentDialog 等）宿主 HWND 与主窗口类名相同，但 GW_OWNER
        /// 关系实测并不可靠（可能为 0），因此按“同 class + 排除主窗口”定位。
        /// </summary>
        public static IReadOnlyList<nint> FindWindowsByClassName(string className, nint excludeHwnd)
        {
            var result = new List<nint>();

            foreach (nint hWnd in EnumVisibleTopLevelWindows())
            {
                if (hWnd == excludeHwnd)
                {
                    continue;
                }

                if (!string.Equals(GetClassName(hWnd), className, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                result.Add(hWnd);
            }

            return result;
        }

        /// <summary>枚举 owner 为指定窗口的可见顶层窗口。</summary>
        public static IReadOnlyList<nint> FindOwnedWindows(nint ownerWindowHandle)
        {
            var result = new List<nint>();

            foreach (nint hWnd in EnumVisibleTopLevelWindows())
            {
                if (hWnd == ownerWindowHandle)
                {
                    continue;
                }

                if (GetWindow(hWnd, GW_OWNER) == ownerWindowHandle)
                {
                    result.Add(hWnd);
                }
            }

            return result;
        }

        /// <summary>
        /// 查找系统文件对话框句柄。
        /// 说明：WinUI 3（Windows App SDK）的文件选择器对话框由 Shell 侧进程承载
        /// （实测 pid 与被测应用不同），因此不能按进程过滤；改用
        /// “类名 #32770 + owner 为指定窗口”为主条件，标题匹配作兜底。
        /// </summary>
        public static nint? FindFileDialogHandle(nint ownerWindowHandle, IReadOnlyCollection<string> fallbackTitles)
        {
            var candidates = new List<(nint Handle, string Title)>();

            foreach (nint hWnd in EnumVisibleTopLevelWindows())
            {
                if (!string.Equals(GetClassName(hWnd), "#32770", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                candidates.Add((hWnd, GetWindowTitle(hWnd)));
            }

            // 1) owner 关系：IFileDialog 模态归属主窗口。
            foreach (var (handle, _) in candidates)
            {
                if (GetWindow(handle, GW_OWNER) == ownerWindowHandle)
                {
                    return handle;
                }
            }

            // 2) 标题兜底（覆盖 owner 解析差异）。
            foreach (var (handle, title) in candidates)
            {
                foreach (string fallbackTitle in fallbackTitles)
                {
                    if (string.Equals(title, fallbackTitle, StringComparison.Ordinal))
                    {
                        return handle;
                    }
                }
            }

            return null;
        }
    }
}
