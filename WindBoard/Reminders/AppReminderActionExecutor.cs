using System;
using System.Diagnostics;
using System.IO;
using WindBoard.Logging;
using WindBoard.Persistence;

namespace WindBoard.Reminders
{
    /// <summary>
    /// 提醒点击动作执行器：
    /// - 动作本身应“尽力而为”，失败只记录日志，不影响主流程。
    /// </summary>
    internal static class AppReminderActionExecutor
    {
        internal static void TryExecute(AppReminderClickAction action)
        {
            try
            {
                switch (action)
                {
                    case AppReminderClickAction.OpenAppDataRootDirectory:
                        TryOpenDirectory(AppDataPaths.RootDirectory, name: "AppDataRoot");
                        return;
                    case AppReminderClickAction.None:
                    default:
                        return;
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn("Reminders", $"执行提醒点击动作失败：action={action}", ex);
            }
        }

        private static void TryOpenDirectory(string directory, string name)
        {
            string dir = (directory ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(dir))
            {
                AppLog.Warn("Reminders", $"无法打开目录（路径为空）：name={name}");
                return;
            }

            try
            {
                // 说明：
                // - 有些路径在第一次保存设置/写日志前可能尚未创建；
                // - 创建失败也不影响“尝试打开”，因为 Explorer 仍可能定位到父目录。
                Directory.CreateDirectory(dir);
            }
            catch (Exception ex)
            {
                AppLog.Warn("Reminders", $"创建目录失败，将继续尝试打开：path='{dir}', name={name}", ex);
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = dir,
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                AppLog.Warn("Reminders", $"打开目录失败：path='{dir}', name={name}", ex);
            }
        }
    }
}

