using System;
using System.IO;
using System.Linq;

namespace WindBoard.UITests.Infrastructure
{
    /// <summary>
    /// 定位仓库根目录与主程序 exe（供 E2E 启动真实应用）。
    /// 查找顺序：Release 优先，Debug 兜底；可用环境变量 WINDBOARD_EXE 显式覆盖。
    /// </summary>
    public static class RepoRootLocator
    {
        /// <summary>环境变量名：显式指定被测主程序 exe 路径（便于本地/CI 覆盖）。</summary>
        internal const string ExePathEnvVar = "WINDBOARD_EXE";

        /// <summary>
        /// 解析主程序 exe 路径；找不到返回 null（由调用方决定 Skip 还是失败）。
        /// </summary>
        internal static string? TryFindAppExe()
        {
            string? overridePath = Environment.GetEnvironmentVariable(ExePathEnvVar);
            if (!string.IsNullOrWhiteSpace(overridePath))
            {
                return File.Exists(overridePath) ? Path.GetFullPath(overridePath) : null;
            }

            string? repoRoot = TryFindRepoRoot();
            if (repoRoot is null)
            {
                return null;
            }

            string mainProjectBin = Path.Combine(repoRoot, "WindBoard", "bin");
            if (!Directory.Exists(mainProjectBin))
            {
                return null;
            }

            string tfm = "net10.0-windows10.0.26100.0";

            // 优先明确路径（覆盖 RID 特定与平台无关两种输出布局），找不到再全量搜索兜底。
            // 说明：主工程构建可能产出 win-x64 RID 子目录，两种都要覆盖；
            // 且必须校验 exe 同目录存在 WindBoard.dll，避免选中孤立 apphost 启动失败。
            string[] candidates =
            {
                Path.Combine(mainProjectBin, "x64", "Release", tfm, "win-x64", "WindBoard.exe"),
                Path.Combine(mainProjectBin, "x64", "Release", tfm, "WindBoard.exe"),
                Path.Combine(mainProjectBin, "x64", "Debug", tfm, "win-x64", "WindBoard.exe"),
                Path.Combine(mainProjectBin, "x64", "Debug", tfm, "WindBoard.exe"),
            };

            foreach (string candidate in candidates)
            {
                if (IsValidAppHost(candidate))
                {
                    return candidate;
                }
            }

            // 兜底：全量搜索并取“最新修改时间”的有效 exe（并行开发下存在多个构建输出目录，
            // 字典序不可靠，按时间取最新最贴近当前代码状态）。
            return Directory
                .EnumerateFiles(mainProjectBin, "WindBoard.exe", SearchOption.AllDirectories)
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                .Where(IsValidAppHost)
                .OrderByDescending(File.GetLastWriteTime)
                .FirstOrDefault();
        }

        /// <summary>exe 有效 = 存在且同目录有主程序集 WindBoard.dll（排除孤立的 apphost 桩）。</summary>
        private static bool IsValidAppHost(string exePath)
        {
            return File.Exists(exePath)
                && File.Exists(Path.Combine(Path.GetDirectoryName(exePath)!, "WindBoard.dll"));
        }

        private static string? TryFindRepoRoot()
        {
            DirectoryInfo? current = new DirectoryInfo(AppContext.BaseDirectory);

            // 从 testhost 输出目录向上最多 8 层寻找包含解决方案文件的仓库根目录。
            for (int depth = 0; current is not null && depth < 8; depth++, current = current.Parent)
            {
                if (File.Exists(Path.Combine(current.FullName, "WindBoard.slnx")))
                {
                    return current.FullName;
                }
            }

            return null;
        }
    }
}
