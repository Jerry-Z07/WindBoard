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

                // 未知参数：忽略
            }

            return new CrashReporterArgs
            {
                ReportPath = reportPath,
                LogsDirectory = logsDir,
                Source = source,
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

