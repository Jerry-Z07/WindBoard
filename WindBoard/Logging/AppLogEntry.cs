using System;

namespace WindBoard.Logging
{
    /// <summary>
    /// 一条日志记录。
    /// </summary>
    internal readonly struct AppLogEntry
    {
        internal DateTimeOffset Timestamp { get; }

        internal AppLogLevel Level { get; }

        internal string Category { get; }

        internal string Message { get; }

        internal Exception? Exception { get; }

        internal AppLogEntry(DateTimeOffset timestamp, AppLogLevel level, string category, string message, Exception? exception)
        {
            Timestamp = timestamp;
            Level = level;
            Category = category;
            Message = message;
            Exception = exception;
        }
    }
}

