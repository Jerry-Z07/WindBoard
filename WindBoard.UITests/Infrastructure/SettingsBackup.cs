using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace WindBoard.UITests.Infrastructure
{
    /// <summary>
    /// 设置文件隔离（方案 B，零主工程改动）：
    /// 每个用例启动应用前备份并移除 settings.json（应用将以默认设置启动），
    /// 用例结束后恢复原文件，保证幂等且不污染用户真实设置数据。
    ///
    /// 覆盖两个候选位置：
    /// - 便携版判定命中时：{exe 目录}\data\settings.json（从构建输出启动的典型路径）
    /// - 兜底：%LocalAppData%\WindBoard\settings.json（安装版/便携 data 不可写）
    /// </summary>
    public static class SettingsBackup
    {
        private static readonly object Gate = new();
        private static Dictionary<string, byte[]>? _backups;

        public static void SaveAndClear()
        {
            lock (Gate)
            {
                _backups = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

                foreach (string path in EnumerateCandidatePaths())
                {
                    try
                    {
                        if (!File.Exists(path))
                        {
                            continue;
                        }

                        _backups[path] = File.ReadAllBytes(path);
                        File.Delete(path);
                    }
                    catch (Exception ex)
                    {
                        // 备份失败不应中断用例：记录后继续（后续恢复步骤会尽力还原）。
                        Console.Error.WriteLine($"[UITests] 备份设置失败：path='{path}', {ex.Message}");
                    }
                }
            }
        }

        public static void Restore()
        {
            lock (Gate)
            {
                if (_backups is null)
                {
                    return;
                }

                foreach ((string path, byte[] bytes) in _backups)
                {
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                        File.WriteAllBytes(path, bytes);
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[UITests] 恢复设置失败：path='{path}', {ex.Message}");
                    }
                }

                _backups = null;
            }
        }

        /// <summary>任一候选 settings.json 是否存在（用于等待设置落盘）。</summary>
        public static bool SettingsFileExists()
        {
            lock (Gate)
            {
                return EnumerateCandidatePaths().Any(File.Exists);
            }
        }

        private static IEnumerable<string> EnumerateCandidatePaths()
        {
            string? exePath = RepoRootLocator.TryFindAppExe();
            if (!string.IsNullOrWhiteSpace(exePath))
            {
                // 对齐主工程 AppRuntimeLayout：运行目录非 shared 时，产品根即运行目录。
                yield return Path.Combine(Path.GetDirectoryName(exePath)!, "data", "settings.json");
            }

            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(localAppData))
            {
                yield return Path.Combine(localAppData, "WindBoard", "settings.json");
            }
        }
    }
}
