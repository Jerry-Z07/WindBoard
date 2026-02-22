using System;
using System.IO;
using System.Text;

namespace WindBoard.CrashReporter
{
    /// <summary>
    /// CrashReporter 自身日志（尽力落盘）。
    /// 说明：不要依赖主程序日志系统，避免循环依赖与 UI/运行时不一致导致再次失败。
    /// </summary>
    internal static class CrashReporterLog
    {
        private const string LogFileName = "CrashReporter.log";

        internal static void Info(string logsDirectory, string message)
        {
            TryAppendLine(logsDirectory, "INFO", message, ex: null);
        }

        internal static void Warn(string logsDirectory, string message, Exception? ex = null)
        {
            TryAppendLine(logsDirectory, "WARN", message, ex);
        }

        internal static void Error(string logsDirectory, string message, Exception? ex = null)
        {
            TryAppendLine(logsDirectory, "ERROR", message, ex);
        }

        private static void TryAppendLine(string logsDirectory, string level, string message, Exception? ex)
        {
            // 关键原则：写日志失败不能影响主流程，必须全程吞异常。
            try
            {
                string dir = ResolveLogDirectory(logsDirectory);
                Directory.CreateDirectory(dir);

                string path = Path.Combine(dir, LogFileName);
                string line = BuildLine(level, message, ex);
                File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // 忽略：兜底也失败时，交给系统。
            }
        }

        private static string ResolveLogDirectory(string logsDirectory)
        {
            try
            {
                string dir = (logsDirectory ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    return dir;
                }
            }
            catch
            {
                // 忽略：继续走兜底
            }

            // 兜底：临时目录
            try
            {
                string temp = Path.Combine(Path.GetTempPath(), "WindBoard");
                return temp;
            }
            catch
            {
                return ".";
            }
        }

        private static string BuildLine(string level, string message, Exception? ex)
        {
            var sb = new StringBuilder(capacity: 256);
            sb.Append(DateTimeOffset.Now.ToString("O"));
            sb.Append(' ');
            sb.Append('[').Append(level).Append(']');
            sb.Append(' ');
            sb.Append(message ?? string.Empty);
            if (ex is not null)
            {
                sb.Append(" | ").Append(ex.GetType().Name).Append(": ").Append(ex.Message);
            }

            return sb.ToString();
        }
    }
}

