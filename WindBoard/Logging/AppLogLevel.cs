namespace WindBoard.Logging
{
    /// <summary>
    /// WindBoard 内部日志级别（越往后越严重）。
    /// </summary>
    internal enum AppLogLevel
    {
        Trace = 0,
        Debug = 1,
        Information = 2,
        Warning = 3,
        Error = 4,
        Critical = 5,
    }
}

