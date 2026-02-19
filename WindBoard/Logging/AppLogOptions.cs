using System;
using System.IO;
using WindBoard.Persistence;

namespace WindBoard.Logging
{
    /// <summary>
    /// 日志初始化选项。
    /// </summary>
    internal sealed class AppLogOptions
    {
        /// <summary>
        /// 最低输出级别（低于该级别的日志将被过滤）。
        /// </summary>
        internal AppLogLevel MinimumLevel { get; init; } = GetDefaultMinimumLevel();

        /// <summary>
        /// 是否启用写入到文件。
        /// </summary>
        internal bool FileEnabled { get; init; } = true;

        /// <summary>
        /// 是否同时写入 Debug 输出（便于开发调试）。
        /// </summary>
        internal bool DebugOutputEnabled { get; init; } = GetDefaultDebugOutputEnabled();

        /// <summary>
        /// 日志目录（默认：%LocalAppData%\WindBoard\Logs）。
        /// </summary>
        internal string LogDirectory { get; init; } = GetDefaultLogDirectory();

        /// <summary>
        /// 日志文件保留天数（<=0 表示不清理）。
        /// </summary>
        internal int RetentionDays { get; init; } = 14;

        internal static AppLogOptions CreateDefault()
        {
            return new AppLogOptions();
        }

        private static string GetDefaultLogDirectory()
        {
            // 统一由 AppDataPaths 决定日志目录（安装版/便携版不同策略）。
            string dir = AppDataPaths.LogsDirectory;
            if (!string.IsNullOrWhiteSpace(dir))
            {
                return dir;
            }

            // 极端兜底：路径解析失败时回退旧策略，避免丢日志或启动异常。
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WindBoard",
                "Logs");
        }

        private static AppLogLevel GetDefaultMinimumLevel()
        {
#if DEBUG
            return AppLogLevel.Debug;
#else
            return AppLogLevel.Information;
#endif
        }

        private static bool GetDefaultDebugOutputEnabled()
        {
#if DEBUG
            return true;
#else
            return false;
#endif
        }
    }
}

