namespace WindBoard.Errors
{
    /// <summary>
    /// 崩溃报告（落盘路径 + 文本内容）。
    /// </summary>
    internal sealed class AppCrashReport
    {
        internal string ReportFilePath { get; init; } = string.Empty;

        internal string ReportText { get; init; } = string.Empty;
    }
}

