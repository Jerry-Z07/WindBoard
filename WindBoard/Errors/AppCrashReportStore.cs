using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using WindBoard.Logging;
using WindBoard.Persistence;

namespace WindBoard.Errors
{
    /// <summary>
    /// 崩溃报告存储（落盘到日志目录下的 Crashes/）。
    ///
    /// 设计目标：
    /// - 任何时候都尽量“写得出来”，失败也不能再抛异常；
    /// - 可注入目录，便于单元测试；
    /// - 报告为纯文本，便于用户复制/分享。
    /// </summary>
    internal static class AppCrashReportStore
    {
        internal static bool TryWriteCrashReport(
            AppCrashSource source,
            Exception? exception,
            object? exceptionObject,
            bool? isTerminating,
            out AppCrashReport report,
            out Exception? error)
        {
            return TryWriteCrashReport(
                source,
                exception,
                exceptionObject,
                isTerminating,
                logDirectoryOverride: null,
                out report,
                out error);
        }

        internal static bool TryWriteCrashReport(
            AppCrashSource source,
            Exception? exception,
            object? exceptionObject,
            bool? isTerminating,
            string? logDirectoryOverride,
            out AppCrashReport report,
            out Exception? error)
        {
            report = new AppCrashReport();
            error = null;

            try
            {
                string crashDir = GetCrashDirectory(logDirectoryOverride);
                if (string.IsNullOrWhiteSpace(crashDir))
                {
                    return false;
                }

                Directory.CreateDirectory(crashDir);

                DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
                int pid = TryGetProcessId();

                string stamp = nowUtc.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
                string fileName = $"crash_{stamp}_{pid}.txt";
                string filePath = Path.Combine(crashDir, fileName);

                string text = BuildReportText(source, nowUtc, pid, exception, exceptionObject, isTerminating, filePath, crashDir);
                File.WriteAllText(filePath, text, Encoding.UTF8);

                report = new AppCrashReport
                {
                    ReportFilePath = filePath,
                    ReportText = text,
                };

                return true;
            }
            catch (Exception ex)
            {
                // 注意：此处只返回错误，不要再向外抛（全局异常处理链路必须“吞得住”）。
                error = ex;
                return false;
            }
        }

        private static int TryGetProcessId()
        {
            try
            {
                return Environment.ProcessId;
            }
            catch
            {
                try
                {
                    return Process.GetCurrentProcess().Id;
                }
                catch
                {
                    return 0;
                }
            }
        }

        private static string GetCrashDirectory(string? logDirectoryOverride)
        {
            // 说明：
            // - 优先使用注入目录（测试使用）；
            // - 其次使用当前日志目录（便携版/安装版由 AppDataPaths 决定）；
            // - 最后回退到 LocalAppData 旧策略，避免空路径导致写到意外位置。

            if (!string.IsNullOrWhiteSpace(logDirectoryOverride))
            {
                return Path.Combine(logDirectoryOverride.Trim(), "Crashes");
            }

            try
            {
                string dir = (AppLog.LogDirectory ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    return Path.Combine(dir, "Crashes");
                }
            }
            catch
            {
                // 忽略：继续走兜底
            }

            try
            {
                string dir = (AppDataPaths.LogsDirectory ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    return Path.Combine(dir, "Crashes");
                }
            }
            catch
            {
                // 忽略：继续走兜底
            }

            try
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "WindBoard",
                    "Logs",
                    "Crashes");
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string BuildReportText(
            AppCrashSource source,
            DateTimeOffset occurredAtUtc,
            int processId,
            Exception? exception,
            object? exceptionObject,
            bool? isTerminating,
            string reportFilePath,
            string crashDirectory)
        {
            // 说明：报告内容应稳定、可读、可复制；避免依赖 JSON 以降低出错概率。
            var sb = new StringBuilder(capacity: 4096);

            sb.AppendLine("WindBoard Crash Report");
            sb.AppendLine("======================");
            sb.AppendLine();

            sb.AppendLine($"OccurredAtUtc: {occurredAtUtc:O}");
            sb.AppendLine($"OccurredAtLocal: {occurredAtUtc.ToLocalTime():O}");
            sb.AppendLine($"Source: {source}");
            if (isTerminating.HasValue)
            {
                sb.AppendLine($"IsTerminating: {isTerminating.Value}");
            }

            sb.AppendLine();

            sb.AppendLine($"AppVersion: {SafeGet(() => AppInfo.Version)}");
            sb.AppendLine($"ProcessId: {processId}");
            sb.AppendLine($"Architecture: {SafeGet(() => RuntimeInformation.ProcessArchitecture.ToString())}");
            sb.AppendLine($"Framework: {SafeGet(() => RuntimeInformation.FrameworkDescription)}");
            sb.AppendLine($"OS: {SafeGet(() => RuntimeInformation.OSDescription)}");

            sb.AppendLine();

            sb.AppendLine($"InstallKind: {SafeGet(() => AppDataPaths.InstallKind.ToString())}");
            sb.AppendLine($"InstallDir: {SafeGet(() => AppDataPaths.InstallDir)}");
            sb.AppendLine($"DataRoot: {SafeGet(() => AppDataPaths.RootDirectory)}");
            sb.AppendLine($"LogDirectory: {SafeGet(() => AppLog.LogDirectory)}");
            sb.AppendLine($"CurrentLogFile: {SafeGet(() => AppLog.CurrentLogFilePath ?? string.Empty)}");
            sb.AppendLine($"CrashDirectory: {crashDirectory}");
            sb.AppendLine($"ReportFilePath: {reportFilePath}");

            sb.AppendLine();
            sb.AppendLine("Exception");
            sb.AppendLine("---------");

            if (exception is not null)
            {
                sb.AppendLine($"ExceptionType: {exception.GetType().FullName}");
                sb.AppendLine($"ExceptionMessage: {exception.Message}");
                sb.AppendLine();
                sb.AppendLine(exception.ToString());
            }
            else if (exceptionObject is not null)
            {
                // AppDomain.UnhandledException 允许抛出非 Exception 对象。
                sb.AppendLine($"ExceptionObjectType: {exceptionObject.GetType().FullName}");
                sb.AppendLine();
                try
                {
                    sb.AppendLine(exceptionObject.ToString() ?? string.Empty);
                }
                catch
                {
                    sb.AppendLine("(ExceptionObject.ToString() failed)");
                }
            }
            else
            {
                sb.AppendLine("(null)");
            }

            sb.AppendLine();
            sb.AppendLine("End");
            return sb.ToString();
        }

        private static string SafeGet(Func<string> getter)
        {
            try
            {
                string v = getter();
                return v ?? string.Empty;
            }
            catch (Exception ex)
            {
                // 兜底：报告文本构造阶段也不能再抛异常；将异常类型落入字段，便于排查。
                return $"(error:{ex.GetType().Name})";
            }
        }
    }
}

