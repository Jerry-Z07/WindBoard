using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace WindBoard.Logging
{
    /// <summary>
    /// 写入到“按天滚动”的日志文件。
    /// 
    /// 设计目标：
    /// - 写文件失败不能影响主流程（日志系统必须“尽力而为”）
    /// - 支持最小级别过滤
    /// - 保留最近 N 天日志，避免无限增长
    /// </summary>
    internal sealed class FileLogSink : IDisposable
    {
        private const string FileNamePrefix = "windboard-";
        private const string FileNameDateFormat = "yyyyMMdd";

        private readonly object _gate = new();
        private readonly AppLogOptions _options;
        private StreamWriter? _writer;
        private string _currentDate = string.Empty;
        private string _currentFilePath = string.Empty;
        private bool _disabled;

        internal string CurrentFilePath
        {
            get
            {
                lock (_gate)
                {
                    return _currentFilePath;
                }
            }
        }

        internal FileLogSink(AppLogOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            lock (_gate)
            {
                TryOpenWriterLocked(DateTimeOffset.Now);
            }
            TryCleanupOldFiles();
        }

        internal void Write(in AppLogEntry entry)
        {
            if (_disabled)
            {
                return;
            }

            if (entry.Level < _options.MinimumLevel)
            {
                return;
            }

            try
            {
                string date = entry.Timestamp.ToString(FileNameDateFormat, CultureInfo.InvariantCulture);
                lock (_gate)
                {
                    if (_disabled)
                    {
                        return;
                    }

                    if (!string.Equals(_currentDate, date, StringComparison.Ordinal))
                    {
                        TryRotateLocked(entry.Timestamp, date);
                    }

                    if (_writer is null)
                    {
                        return;
                    }

                    string text = AppLogFormat.Format(entry);
                    _writer.WriteLine(text);
                }
            }
            catch
            {
                // 任何写入异常都必须降级为“禁用文件日志”，避免影响主流程。
                _disabled = true;
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                try
                {
                    _writer?.Dispose();
                }
                catch
                {
                    // 忽略释放失败
                }
                finally
                {
                    _writer = null;
                }
            }
        }

        private void TryRotateLocked(DateTimeOffset now, string date)
        {
            try
            {
                _writer?.Dispose();
            }
            catch
            {
                // 忽略关闭失败：继续尝试打开新文件
            }
            finally
            {
                _writer = null;
            }

            TryOpenWriterLocked(now, date);
            TryCleanupOldFiles();
        }

        private void TryOpenWriterLocked(DateTimeOffset now, string? date = null)
        {
            // 注意：该方法要求调用方已持有 _gate 锁。
            if (!_options.FileEnabled)
            {
                _disabled = true;
                return;
            }

            string normalizedDate = date ?? now.ToString(FileNameDateFormat, CultureInfo.InvariantCulture);

            try
            {
                Directory.CreateDirectory(_options.LogDirectory);

                string fileName = FileNamePrefix + normalizedDate + ".log";
                string path = Path.Combine(_options.LogDirectory, fileName);

                // 允许用户在运行时打开日志查看，因此 share ReadWrite。
                var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
                {
                    AutoFlush = true,
                    NewLine = "\n",
                };

                if (_disabled)
                {
                    writer.Dispose();
                    return;
                }

                _writer = writer;
                _currentDate = normalizedDate;
                _currentFilePath = path;
            }
            catch
            {
                _disabled = true;
            }
        }

        private void TryCleanupOldFiles()
        {
            if (_options.RetentionDays <= 0)
            {
                return;
            }

            try
            {
                if (!Directory.Exists(_options.LogDirectory))
                {
                    return;
                }

                DateTimeOffset threshold = DateTimeOffset.Now.AddDays(-_options.RetentionDays);
                foreach (string file in Directory.EnumerateFiles(_options.LogDirectory, FileNamePrefix + "*.log"))
                {
                    DateTimeOffset? date = TryParseDateFromFileName(file);
                    if (date is null)
                    {
                        continue;
                    }

                    // 以“文件名日期”为准：避免文件被复制/修改导致时间戳不准。
                    if (date.Value < threshold)
                    {
                        TryDeleteFile(file);
                    }
                }
            }
            catch
            {
                // 清理失败不影响主流程
            }
        }

        private static DateTimeOffset? TryParseDateFromFileName(string filePath)
        {
            try
            {
                string name = Path.GetFileNameWithoutExtension(filePath);
                if (!name.StartsWith(FileNamePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                string dateText = name.Substring(FileNamePrefix.Length);
                if (DateTime.TryParseExact(
                        dateText,
                        FileNameDateFormat,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out DateTime date))
                {
                    // 当天 00:00
                    return new DateTimeOffset(date, TimeZoneInfo.Local.GetUtcOffset(date));
                }
            }
            catch
            {
                return null;
            }

            return null;
        }

        private static void TryDeleteFile(string filePath)
        {
            try
            {
                File.Delete(filePath);
            }
            catch
            {
                // 忽略删除失败
            }
        }
    }
}
