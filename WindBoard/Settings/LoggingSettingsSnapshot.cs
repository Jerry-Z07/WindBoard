using WindBoard.Logging;

namespace WindBoard.Settings
{
    /// <summary>
    /// 日志设置的只读快照（供 UI/启动流程使用，避免直接暴露可变引用）。
    /// </summary>
    internal sealed class LoggingSettingsSnapshot
    {
        public required bool FileEnabled { get; init; }

        public required AppLogLevel MinimumLevel { get; init; }

        public required int RetentionDays { get; init; }
    }
}

