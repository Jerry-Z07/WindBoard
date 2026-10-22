using System;

namespace WindBoard.CrashReporter
{
    /// <summary>
    /// CrashReporter 命令行参数解析。
    /// 设计原则：
    /// - 解析不因缺参/未知参而抛异常；
    /// - 参数尽量显式，便于未来扩展。
    /// </summary>
    internal sealed class CrashReporterArgs
    {
        internal string ReportPath { get; init; } = string.Empty;

        internal string LogsDirectory { get; init; } = string.Empty;

        internal string Source { get; init; } = string.Empty;

        /// <summary>
        /// 崩溃发生时间（主程序以环回格式 ISO 8601 传入的本地时间字符串）。
        /// </summary>
        internal string OccurredAt { get; init; } = string.Empty;

        /// <summary>
        /// 异常类型全名（AppDomain 未处理异常为非 Exception 对象时为其对象类型）。
        /// </summary>
        internal string ExceptionType { get; init; } = string.Empty;

        /// <summary>
        /// 异常消息（主程序侧已折叠为单行并截断，仅用于摘要展示）。
        /// </summary>
        internal string ExceptionMessage { get; init; } = string.Empty;

        internal static CrashReporterArgs Parse(string[] args)
        {
            // 说明：args 来自 Main(string[] args)，理论上不会为 null；
            // 但这里仍做防御，确保任何情况下都不抛异常。
            if (args is null || args.Length == 0)
            {
                return new CrashReporterArgs();
            }

            string reportPath = string.Empty;
            string logsDir = string.Empty;
            string source = string.Empty;
            string occurredAt = string.Empty;
            string exceptionType = string.Empty;
            string exceptionMessage = string.Empty;

            // 简单顺序解析：--key value
            // 注意：不要使用复杂的解析库，减少依赖与出错概率。
            for (int i = 0; i < args.Length; i++)
            {
                string key = args[i] ?? string.Empty;
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                if (IsKey(key, "--report") && TryGetValue(args, i, out string? report))
                {
                    reportPath = report ?? string.Empty;
                    i++;
                    continue;
                }

                if (IsKey(key, "--logs-dir") && TryGetValue(args, i, out string? logs))
                {
                    logsDir = logs ?? string.Empty;
                    i++;
                    continue;
                }

                if (IsKey(key, "--source") && TryGetValue(args, i, out string? s))
                {
                    source = s ?? string.Empty;
                    i++;
                    continue;
                }

                if (IsKey(key, "--occurred-at") && TryGetValue(args, i, out string? occurred))
                {
                    occurredAt = occurred ?? string.Empty;
                    i++;
                    continue;
                }

                if (IsKey(key, "--exception-type") && TryGetValue(args, i, out string? exceptionTypeName))
                {
                    exceptionType = exceptionTypeName ?? string.Empty;
                    i++;
                    continue;
                }

                if (IsKey(key, "--exception-message") && TryGetValue(args, i, out string? exceptionText))
                {
                    exceptionMessage = exceptionText ?? string.Empty;
                    i++;
                    continue;
                }

                // 未知参数：忽略（旧版 CrashReporter 遇到新版主程序新增参数时即走此分支，无副作用）
            }

            return new CrashReporterArgs
            {
                ReportPath = reportPath,
                LogsDirectory = logsDir,
                Source = source,
                OccurredAt = occurredAt,
                ExceptionType = exceptionType,
                ExceptionMessage = exceptionMessage,
            };
        }

        private static bool IsKey(string value, string expected)
        {
            return string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryGetValue(string[] args, int keyIndex, out string? value)
        {
            value = null;
            int i = keyIndex + 1;
            if (i < 0 || i >= args.Length)
            {
                return false;
            }

            value = args[i];
            return true;
        }
    }
}

