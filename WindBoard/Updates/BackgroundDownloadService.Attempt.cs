using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using WindBoard.Logging;
using WindBoard.Settings;

namespace WindBoard.Updates
{
    internal static partial class BackgroundDownloadService
    {
        private static async Task<DownloadResult> DownloadFromSourceAsync(DownloadContext context, DownloadSourceId sourceId)
        {
            DownloadResult result;

            string url = DownloadSourceUrlRewriter.Rewrite(context.OriginalUrl, sourceId);
            var attemptErrors = new List<DownloadAttemptError>();

            // 断点续传：读取已有 .part 大小。
            long existingBytes = TryGetExistingPartBytes(context.PartPath, sourceId, attemptErrors);

            using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
            CancellationToken attemptToken = attemptCts.Token;

            // 空闲超时：一段时间无任何进度则视为“卡死”，主动取消并切换源。
            // 说明：镜像源偶发连接建立成功但长期无数据，此逻辑可避免无限等待。
            long lastProgressTicks = DateTimeOffset.UtcNow.Ticks;
            StartIdleTimeoutMonitor(attemptCts, context.IdleTimeout, () => Interlocked.Read(ref lastProgressTicks));

            try
            {
                SourceRequestContext requestContext = new(context.HttpClient, url, context.PartPath, sourceId);
                (HttpResponseMessage response, long resumeBytes) = await SendWithResumeFallbackAsync(requestContext, existingBytes, attemptToken)
                    .ConfigureAwait(false);

                using (response)
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        attemptErrors.Add(new DownloadAttemptError(sourceId, $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}"));
                        result = DownloadResult.Fail("下载失败", attemptErrors);
                    }
                    else
                    {
                        bool isPartial = response.StatusCode == System.Net.HttpStatusCode.PartialContent && resumeBytes > 0;
                        long? totalBytes = ComputeTotalBytes(response, resumeBytes, isPartial);

                        var reporter = new DownloadProgressReporter(
                            context.Progress,
                            sourceId,
                            totalBytes,
                            () => Interlocked.Exchange(ref lastProgressTicks, DateTimeOffset.UtcNow.Ticks));

                        FileMode mode = isPartial ? FileMode.Append : FileMode.Create;
                        long downloaded = isPartial ? resumeBytes : 0;

                        await using Stream httpStream = await response.Content.ReadAsStreamAsync(attemptToken).ConfigureAwait(false);
                        await using (var fileStream = new FileStream(
                                         context.PartPath,
                                         mode,
                                         FileAccess.Write,
                                         FileShare.Read,
                                         bufferSize: 128 * 1024,
                                         useAsync: true))
                        {
                            reporter.ReportDownloading(downloaded);
                            downloaded = await CopyToPartFileAsync(httpStream, fileStream, reporter, downloaded, attemptToken).ConfigureAwait(false);
                            await fileStream.FlushAsync(attemptToken).ConfigureAwait(false);
                        }

                        // 若能获取到总大小，则做一次长度校验，避免不完整文件被当成成功。
                        if (totalBytes is not null && downloaded != totalBytes.Value)
                        {
                            attemptErrors.Add(new DownloadAttemptError(sourceId, $"文件不完整：downloaded={downloaded}, expected={totalBytes}"));
                            result = DownloadResult.Fail("下载未完成", attemptErrors);
                        }
                        else
                        {
                            result = FinalizeSuccessfulDownload(context, sourceId, downloaded, attemptErrors, reporter);
                        }
                    }
                }
            }
            catch (OperationCanceledException oce) when (!context.CancellationToken.IsCancellationRequested)
            {
                // attemptToken 被空闲超时取消：视为该源失败，切换下一源。
                attemptErrors.Add(new DownloadAttemptError(sourceId, $"下载超时/无进度：{oce.Message}"));
                result = DownloadResult.Fail("下载超时/无进度", attemptErrors, oce);
            }
            catch (Exception ex)
            {
                attemptErrors.Add(new DownloadAttemptError(sourceId, ex.Message));
                result = DownloadResult.Fail("下载失败", attemptErrors, ex);
            }

            return result;
        }

        private static DownloadResult FinalizeSuccessfulDownload(
            DownloadContext context,
            DownloadSourceId sourceId,
            long downloaded,
            List<DownloadAttemptError> attemptErrors,
            DownloadProgressReporter reporter)
        {
            // 原子替换：.part -> 最终文件。
            try
            {
                File.Move(context.PartPath, context.DestinationPath, overwrite: true);
            }
            catch (Exception ex)
            {
                AppLog.Warn("Downloads", $"移动下载文件失败：part='{context.PartPath}', dest='{context.DestinationPath}'", ex);
                attemptErrors.Add(new DownloadAttemptError(sourceId, ex.Message));
                return DownloadResult.Fail("保存下载文件失败", attemptErrors, ex);
            }

            reporter.ReportCompleted(downloaded);
            return DownloadResult.SuccessResult(context.DestinationPath, sourceId);
        }

        private static long TryGetExistingPartBytes(string partPath, DownloadSourceId sourceId, List<DownloadAttemptError> attemptErrors)
        {
            try
            {
                if (File.Exists(partPath))
                {
                    return new FileInfo(partPath).Length;
                }
            }
            catch (Exception ex)
            {
                attemptErrors.Add(new DownloadAttemptError(sourceId, ex.Message));
            }

            return 0;
        }

        private static void StartIdleTimeoutMonitor(CancellationTokenSource attemptCts, TimeSpan idleTimeout, Func<long> getLastProgressTicks)
        {
            if (idleTimeout <= TimeSpan.Zero || idleTimeout == Timeout.InfiniteTimeSpan)
            {
                return;
            }

            CancellationToken attemptToken = attemptCts.Token;
            _ = Task.Run(async () =>
            {
                try
                {
                    while (!attemptToken.IsCancellationRequested)
                    {
                        await Task.Delay(idleTimeout, attemptToken).ConfigureAwait(false);
                        long last = getLastProgressTicks();
                        TimeSpan idle = DateTimeOffset.UtcNow - new DateTimeOffset(last, TimeSpan.Zero);
                        if (idle >= idleTimeout)
                        {
                            attemptCts.Cancel();
                            return;
                        }
                    }
                }
                catch
                {
                    // 忽略：取消/异常由主流程处理
                }
            }, CancellationToken.None);
        }

        private static async Task<(HttpResponseMessage Response, long ResumeBytes)> SendWithResumeFallbackAsync(
            SourceRequestContext context,
            long existingBytes,
            CancellationToken cancellationToken)
        {
            HttpResponseMessage resp = await SendDownloadRequestAsync(context, existingBytes, cancellationToken)
                .ConfigureAwait(false);

            if ((int)resp.StatusCode == 416 && existingBytes > 0)
            {
                // Range 不可用：可能远端文件变更或本地 .part 损坏，删除后从头重下。
                resp.Dispose();
                TryDeletePartFile(context.PartPath);

                existingBytes = 0;
                resp = await SendDownloadRequestAsync(context, existingBytes, cancellationToken).ConfigureAwait(false);
            }

            if (existingBytes > 0 && resp.StatusCode == System.Net.HttpStatusCode.OK)
            {
                // 部分镜像源可能忽略 Range 并返回 200，此时只能从头重下，否则会导致文件拼接错误。
                resp.Dispose();
                TryDeletePartFile(context.PartPath);

                existingBytes = 0;
                resp = await SendDownloadRequestAsync(context, existingBytes, cancellationToken).ConfigureAwait(false);
            }

            return (resp, existingBytes);
        }

        private static async Task<HttpResponseMessage> SendDownloadRequestAsync(
            SourceRequestContext context,
            long rangeStart,
            CancellationToken cancellationToken)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, context.Url);
            if (rangeStart > 0)
            {
                req.Headers.Range = new RangeHeaderValue(rangeStart, null);
            }

            return await context.HttpClient
                .SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
        }

        private static long? ComputeTotalBytes(HttpResponseMessage response, long resumeBytes, bool isPartial)
        {
            if (response.Content.Headers.ContentRange?.Length is long totalFromRange && totalFromRange > 0)
            {
                return totalFromRange;
            }

            if (response.Content.Headers.ContentLength is long contentLength && contentLength > 0)
            {
                return isPartial ? resumeBytes + contentLength : contentLength;
            }

            return null;
        }

        private static async Task<long> CopyToPartFileAsync(
            Stream httpStream,
            FileStream fileStream,
            DownloadProgressReporter reporter,
            long downloaded,
            CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[128 * 1024];

            while (true)
            {
                int read = await httpStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read <= 0)
                {
                    break;
                }

                await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                downloaded += read;
                reporter.ReportDownloading(downloaded);
            }

            return downloaded;
        }

        private static void TryDeletePartFile(string partPath)
        {
            try
            {
                if (File.Exists(partPath))
                {
                    File.Delete(partPath);
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn("Downloads", $"删除 .part 文件失败：path='{partPath}'", ex);
            }
        }

        private readonly struct SourceRequestContext
        {
            internal HttpClient HttpClient { get; }

            internal string Url { get; }

            internal string PartPath { get; }

            internal DownloadSourceId SourceId { get; }

            internal SourceRequestContext(HttpClient httpClient, string url, string partPath, DownloadSourceId sourceId)
            {
                HttpClient = httpClient;
                Url = url ?? string.Empty;
                PartPath = partPath ?? string.Empty;
                SourceId = sourceId;
            }
        }

        private sealed class DownloadProgressReporter
        {
            private readonly IProgress<DownloadProgress>? _progress;
            private readonly DownloadSourceId _sourceId;
            private readonly long? _totalBytes;
            private readonly Action _touchProgress;

            internal DownloadProgressReporter(
                IProgress<DownloadProgress>? progress,
                DownloadSourceId sourceId,
                long? totalBytes,
                Action touchProgress)
            {
                _progress = progress;
                _sourceId = sourceId;
                _totalBytes = totalBytes;
                _touchProgress = touchProgress ?? (() => { });
            }

            internal void ReportDownloading(long downloadedBytes)
            {
                _touchProgress();
                _progress?.Report(new DownloadProgress
                {
                    SourceId = _sourceId,
                    DownloadedBytes = downloadedBytes,
                    TotalBytes = _totalBytes,
                    Status = DownloadProgressStatus.Downloading,
                });
            }

            internal void ReportCompleted(long downloadedBytes)
            {
                _touchProgress();
                _progress?.Report(new DownloadProgress
                {
                    SourceId = _sourceId,
                    DownloadedBytes = downloadedBytes,
                    TotalBytes = _totalBytes,
                    Status = DownloadProgressStatus.Completed,
                });
            }
        }
    }
}
