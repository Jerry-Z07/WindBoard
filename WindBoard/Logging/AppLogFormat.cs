using System;
using System.Globalization;
using System.Text;

namespace WindBoard.Logging
{
    /// <summary>
    /// 日志格式化（集中管理，便于后续统一调整输出格式）。
    /// </summary>
    internal static class AppLogFormat
    {
        internal static string Format(in AppLogEntry entry)
        {
            // 格式示例：
            // 2026-02-12 20:06:01.234 +08:00 [INF] [Import] message
            // System.Exception: ...
            var sb = new StringBuilder(capacity: 256);

            sb.Append(entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture));
            sb.Append(' ');
            sb.Append('[').Append(ToLevelToken(entry.Level)).Append(']');
            sb.Append(' ');
            sb.Append('[').Append(entry.Category).Append(']');
            sb.Append(' ');
            sb.Append(entry.Message);

            if (entry.Exception is not null)
            {
                sb.Append('\n');
                sb.Append(entry.Exception);
            }

            return sb.ToString();
        }

        private static string ToLevelToken(AppLogLevel level)
        {
            return level switch
            {
                AppLogLevel.Trace => "TRC",
                AppLogLevel.Debug => "DBG",
                AppLogLevel.Information => "INF",
                AppLogLevel.Warning => "WRN",
                AppLogLevel.Error => "ERR",
                AppLogLevel.Critical => "CRT",
                _ => "UNK",
            };
        }
    }
}

