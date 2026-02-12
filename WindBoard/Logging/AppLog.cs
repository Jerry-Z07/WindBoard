using System;
using System.Threading;

namespace WindBoard.Logging
{
    /// <summary>
    /// 应用全局日志入口（文件 + Debug 输出）。
    /// </summary>
    internal static class AppLog
    {
        private static readonly object Gate = new();

        private static AppLogOptions _options = AppLogOptions.CreateDefault();
        private static FileLogSink? _fileSink;
        private static int _initialized;

        internal static string LogDirectory => _options.LogDirectory;

        internal static string? CurrentLogFilePath => _fileSink?.CurrentFilePath;

        /// <summary>
        /// 初始化日志系统（可重复调用：后续调用会覆盖配置）。
        /// </summary>
        internal static void Initialize(AppLogOptions? options = null, bool writeInitLog = true)
        {
            lock (Gate)
            {
                _options = options ?? AppLogOptions.CreateDefault();

                try
                {
                    _fileSink?.Dispose();
                }
                catch
                {
                    // 忽略 Dispose 失败
                }
                finally
                {
                    _fileSink = null;
                }

                if (_options.FileEnabled)
                {
                    try
                    {
                        _fileSink = new FileLogSink(_options);
                    }
                    catch
                    {
                        _fileSink = null;
                    }
                }

                Interlocked.Exchange(ref _initialized, 1);
            }

            if (writeInitLog)
            {
                // 初始化完成后写一条信息，便于用户定位日志文件位置。
                Info(
                    "App",
                    $"日志初始化完成：minLevel={_options.MinimumLevel}, fileEnabled={_options.FileEnabled}, debugEnabled={_options.DebugOutputEnabled}, dir='{_options.LogDirectory}', file='{_fileSink?.CurrentFilePath ?? "(null)"}'");
            }
        }

        internal static void Trace(string category, string message, Exception? ex = null) => Write(AppLogLevel.Trace, category, message, ex);
        internal static void Debug(string category, string message, Exception? ex = null) => Write(AppLogLevel.Debug, category, message, ex);
        internal static void Info(string category, string message, Exception? ex = null) => Write(AppLogLevel.Information, category, message, ex);
        internal static void Warn(string category, string message, Exception? ex = null) => Write(AppLogLevel.Warning, category, message, ex);
        internal static void Error(string category, string message, Exception? ex = null) => Write(AppLogLevel.Error, category, message, ex);
        internal static void Critical(string category, string message, Exception? ex = null) => Write(AppLogLevel.Critical, category, message, ex);

        internal static void Write(AppLogLevel level, string category, string message, Exception? ex = null)
        {
            // 确保即使调用方忘了初始化，也不会直接丢失关键日志。
            if (Interlocked.CompareExchange(ref _initialized, 1, 1) == 0)
            {
                Initialize(writeInitLog: false);
            }

            string normalizedCategory = string.IsNullOrWhiteSpace(category) ? "-" : category.Trim();
            string normalizedMessage = message ?? string.Empty;

            var entry = new AppLogEntry(DateTimeOffset.Now, level, normalizedCategory, normalizedMessage, ex);

            try
            {
                _fileSink?.Write(entry);
            }
            catch
            {
                // 文件日志写入失败不应影响主流程
            }

            if (_options.DebugOutputEnabled)
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine(AppLogFormat.Format(entry));
                }
                catch
                {
                    // 忽略 Debug 输出失败
                }
            }
        }
    }
}
