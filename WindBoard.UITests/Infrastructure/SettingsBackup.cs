using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

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
    ///
    /// 安全加固（2026-09，实测依据：xUnit v2 在 InitializeAsync 抛出时不会调用 DisposeAsync）：
    /// - 备份同时落盘到 %TEMP%\windboard-uitests\settings-backup.json，测试进程被强杀/崩溃后仍可人工恢复；
    /// - 每次 SaveAndClear 前先恢复上次运行残留的备份文件（自愈），避免用户设置长期停留在丢失状态；
    /// - Restore 时删除“测试期间新建”的 settings.json，保证环境零残留。
    /// </summary>
    public static class SettingsBackup
    {
        private static readonly object Gate = new();

        private static Dictionary<string, byte[]>? _backups;

        /// <summary>磁盘备份文件路径（进程级崩溃后的唯一恢复依据）。</summary>
        private static string BackupFilePath => Path.Combine(
            Path.GetTempPath(),
            "windboard-uitests",
            "settings-backup.json");

        public static void SaveAndClear()
        {
            lock (Gate)
            {
                RecoverStaleBackup();

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

                PersistBackupFile();
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

                RemoveTestCreatedSettingsFiles();
                DeleteBackupFile();

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

        /// <summary>恢复上次运行异常残留的磁盘备份（幂等：无残留时不做事）。</summary>
        private static void RecoverStaleBackup()
        {
            string backupFile = BackupFilePath;
            if (!File.Exists(backupFile))
            {
                return;
            }

            try
            {
                Dictionary<string, byte[]>? stale =
                    JsonSerializer.Deserialize<Dictionary<string, byte[]>>(File.ReadAllText(backupFile));

                if (stale is not null)
                {
                    foreach ((string path, byte[] bytes) in stale)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                        File.WriteAllBytes(path, bytes);
                    }
                }

                File.Delete(backupFile);
                Console.Error.WriteLine("[UITests] 检测到上次运行残留的设置备份，已恢复后再开始本次备份。");
            }
            catch (Exception ex)
            {
                // 残留恢复失败不能阻断本次用例：保留备份文件供人工处理。
                Console.Error.WriteLine($"[UITests] 残留设置备份恢复失败：path='{backupFile}', {ex.Message}");
            }
        }

        /// <summary>把当前备份写入磁盘（进程崩溃后仍可恢复）。</summary>
        private static void PersistBackupFile()
        {
            if (_backups is null || _backups.Count == 0)
            {
                return;
            }

            try
            {
                string backupFile = BackupFilePath;
                Directory.CreateDirectory(Path.GetDirectoryName(backupFile)!);
                File.WriteAllText(backupFile, JsonSerializer.Serialize(_backups));
            }
            catch (Exception ex)
            {
                // 落盘失败不影响内存备份的常规恢复路径：记录后继续。
                Console.Error.WriteLine($"[UITests] 设置备份落盘失败：{ex.Message}");
            }
        }

        /// <summary>删除测试期间新建（备份中不存在）的 settings.json，避免环境残留。</summary>
        private static void RemoveTestCreatedSettingsFiles()
        {
            if (_backups is null)
            {
                return;
            }

            foreach (string path in EnumerateCandidatePaths())
            {
                if (_backups.ContainsKey(path) || !File.Exists(path))
                {
                    continue;
                }

                try
                {
                    File.Delete(path);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[UITests] 清理测试新建的设置文件失败：path='{path}', {ex.Message}");
                }
            }
        }

        /// <summary>恢复完成后删除磁盘备份（失败则保留供人工处理）。</summary>
        private static void DeleteBackupFile()
        {
            try
            {
                if (File.Exists(BackupFilePath))
                {
                    File.Delete(BackupFilePath);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[UITests] 设置备份文件清理失败：path='{BackupFilePath}', {ex.Message}");
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
