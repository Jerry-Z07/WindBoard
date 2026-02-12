using System;

namespace WindBoard.Logging
{
    /// <summary>
    /// 日志级别解析与归一化。
    /// </summary>
    internal static class AppLogLevelParser
    {
        internal static bool TryParse(string? text, out AppLogLevel level)
        {
            level = AppLogLevel.Information;

            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string value = text.Trim();

            // 允许配置使用常见缩写/别名，降低手动编辑 settings.json 的心智负担。
            if (value.Equals("trace", StringComparison.OrdinalIgnoreCase) || value.Equals("trc", StringComparison.OrdinalIgnoreCase))
            {
                level = AppLogLevel.Trace;
                return true;
            }

            if (value.Equals("debug", StringComparison.OrdinalIgnoreCase) || value.Equals("dbg", StringComparison.OrdinalIgnoreCase))
            {
                level = AppLogLevel.Debug;
                return true;
            }

            if (value.Equals("info", StringComparison.OrdinalIgnoreCase)
                || value.Equals("information", StringComparison.OrdinalIgnoreCase)
                || value.Equals("inf", StringComparison.OrdinalIgnoreCase))
            {
                level = AppLogLevel.Information;
                return true;
            }

            if (value.Equals("warn", StringComparison.OrdinalIgnoreCase)
                || value.Equals("warning", StringComparison.OrdinalIgnoreCase)
                || value.Equals("wrn", StringComparison.OrdinalIgnoreCase))
            {
                level = AppLogLevel.Warning;
                return true;
            }

            if (value.Equals("error", StringComparison.OrdinalIgnoreCase) || value.Equals("err", StringComparison.OrdinalIgnoreCase))
            {
                level = AppLogLevel.Error;
                return true;
            }

            if (value.Equals("critical", StringComparison.OrdinalIgnoreCase)
                || value.Equals("fatal", StringComparison.OrdinalIgnoreCase)
                || value.Equals("crt", StringComparison.OrdinalIgnoreCase))
            {
                level = AppLogLevel.Critical;
                return true;
            }

            return false;
        }
    }
}

