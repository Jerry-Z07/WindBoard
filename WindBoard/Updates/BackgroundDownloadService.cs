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
    /// <summary>
    /// 后台下载服务：
    /// - 支持进度回调
    /// - 失败时按下载源顺序轮询切换
    /// - 支持 .part 断点续传（Range）
    /// </summary>
    internal static class BackgroundDownloadService
    {
        private static readonly HttpClient DownloadClient = CreateHttpClient();

        internal static async Task<DownloadResult> DownloadWithFailoverAsync(
            DownloadRequest request,
            IProgress<DownloadProgress>? progress,
            CancellationToken cancellationToken)
        {
            if (request is null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            string originalUrl = (request.OriginalUrl ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(originalUrl))
            {
                return DownloadResult.Fail("OriginalUrl 不能为空。");
            }

            string destinationPath = (request.DestinationPath ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(destinationPath))
            {
                return DownloadResult.Fail("DestinationPath 不能为空。");
            }

            try
            {
                string? dir = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    Directory.CreateDirectory(dir);
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("Downloads", $"创建下载目录失败：path='{destinationPath}'", ex);
                return DownloadResult.Fail("创建下载目录失败。", ex);
            }

            IReadOnlyList<DownloadSourceId> order = request.FailoverOrder is not null && request.FailoverOrder.Count > 0
                ? request.FailoverOrder
                : DownloadSourceUrlRewriter.BuildFailoverOrder(request.PreferredSourceId);

            string partPath = destinationPath + ".part";
            var attemptErrors = new List<DownloadAttemptError>();

            int maxCycles = Math.Max(1, request.MaxCycles);
            for (int cycle = 1; cycle <= maxCycles; cycle++)
            {
                foreach (DownloadSourceId source in order)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        DownloadResult result = await DownloadFromSourceAsync(
                                originalUrl,
                                destinationPath,
                                partPath,
                                source,
                                progress,
                                cancellationToken)
                            .ConfigureAwait(false);

                        if (result.Success)
                        {
                            return result;
                        }

                        attemptErrors.AddRange(result.AttemptErrors);
                    }
                    catch (Exception ex)
                    {
                        attemptErrors.Add(new DownloadAttemptError(source, ex.Message));
                        AppLog.Warn("Downloads", $"下载失败，将切换下载源：source={source}", ex);
                    }
                }

                // 一轮都失败：退避后再试下一轮（镜像源可能短暂不可用）。
                if (cycle < maxCycles)
                {
                    TimeSpan backoff = TimeSpan.FromMilliseconds(350 * cycle);
                    try
                    {
                        await Task.Delay(backoff, cancellationToken).ConfigureAwait(false);
                    }
                    catch
                    {
                        // 忽略：取消会在下一轮开始前抛出
                    }
                }
            }

            return DownloadResult.Fail("下载失败（已轮询切换下载源）", attemptErrors);
        }

        private static async Task<DownloadResult> DownloadFromSourceAsync(
            string originalUrl,
            string destinationPath,
            string partPath,
            DownloadSourceId sourceId,
            IProgress<DownloadProgress>? progress,
            CancellationToken cancellationToken)
        {
            string url = DownloadSourceUrlRewriter.Rewrite(originalUrl, sourceId);
            var attemptErrors = new List<DownloadAttemptError>();

            // 断点续传：读取已有 .part 大小。
            long existingBytes = 0;
            try
            {
                if (File.Exists(partPath))
                {
                    existingBytes = new FileInfo(partPath).Length;
                }
            }
            catch (Exception ex)
            {
                attemptErrors.Add(new DownloadAttemptError(sourceId, ex.Message));
                existingBytes = 0;
            }

            using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            CancellationToken attemptToken = attemptCts.Token;

            // 空闲超时：一段时间无任何进度则视为“卡死”，主动取消并切换源。
            // 说明：镜像源偶发连接建立成功但长期无数据，此逻辑可避免无限等待。
            const int idleTimeoutSeconds = 20;
            long lastProgressTicks = DateTimeOffset.UtcNow.Ticks;
            _ = Task.Run(async () =>
            {
                try
                {
                    while (!attemptToken.IsCancellationRequested)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(idleTimeoutSeconds), attemptToken).ConfigureAwait(false);
                        long last = Interlocked.Read(ref lastProgressTicks);
                        TimeSpan idle = DateTimeOffset.UtcNow - new DateTimeOffset(last, TimeSpan.Zero);
                        if (idle >= TimeSpan.FromSeconds(idleTimeoutSeconds))
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

            try
            {
                using HttpRequestMessage req = new(HttpMethod.Get, url);
                if (existingBytes > 0)
                {
                    req.Headers.Range = new RangeHeaderValue(existingBytes, null);
                }

                using HttpResponseMessage resp = await DownloadClient
                    .SendAsync(req, HttpCompletionOption.ResponseHeadersRead, attemptToken)
                    .ConfigureAwait(false);

                if ((int)resp.StatusCode == 416)
                {
                    // Range 不可用：可能远端文件变更或本地 .part 损坏，删除后从头重下。
                    TryDeletePartFile(partPath);
                    existingBytes = 0;
                }
                else if (!resp.IsSuccessStatusCode)
                {
                    attemptErrors.Add(new DownloadAttemptError(sourceId, $"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}"));
                    return DownloadResult.Fail("下载失败", attemptErrors);
                }

                bool isPartial = resp.StatusCode == System.Net.HttpStatusCode.PartialContent && existingBytes > 0;

                // 计算总大小（用于进度百分比）。
                long? totalBytes = null;
                if (resp.Content.Headers.ContentRange?.Length is long totalFromRange && totalFromRange > 0)
                {
                    totalBytes = totalFromRange;
                }
                else if (resp.Content.Headers.ContentLength is long contentLength && contentLength > 0)
                {
                    totalBytes = isPartial ? existingBytes + contentLength : contentLength;
                }

                FileMode mode = isPartial ? FileMode.Append : FileMode.Create;
                await using var httpStream = await resp.Content.ReadAsStreamAsync(attemptToken).ConfigureAwait(false);
                await using var fileStream = new FileStream(
                    partPath,
                    mode,
                    FileAccess.Write,
                    FileShare.Read,
                    bufferSize: 128 * 1024,
                    useAsync: true);

                byte[] buffer = new byte[128 * 1024];
                long downloaded = existingBytes;

                ReportProgress(progress, sourceId, downloaded, totalBytes, status: DownloadProgressStatus.Downloading);

                while (true)
                {
                    int read = await httpStream.ReadAsync(buffer, attemptToken).ConfigureAwait(false);
                    if (read <= 0)
                    {
                        break;
                    }

                    await fileStream.WriteAsync(buffer.AsMemory(0, read), attemptToken).ConfigureAwait(false);
                    downloaded += read;
                    Interlocked.Exchange(ref lastProgressTicks, DateTimeOffset.UtcNow.Ticks);

                    ReportProgress(progress, sourceId, downloaded, totalBytes, status: DownloadProgressStatus.Downloading);
                }

                await fileStream.FlushAsync(attemptToken).ConfigureAwait(false);

                // 若能获取到总大小，则做一次长度校验，避免不完整文件被当成成功。
                if (totalBytes is not null && downloaded != totalBytes.Value)
                {
                    attemptErrors.Add(new DownloadAttemptError(sourceId, $"文件不完整：downloaded={downloaded}, expected={totalBytes}"));
                    return DownloadResult.Fail("下载未完成", attemptErrors);
                }

                // 原子替换：.part -> 最终文件。
                try
                {
                    File.Move(partPath, destinationPath, overwrite: true);
                }
                catch (Exception ex)
                {
                    AppLog.Warn("Downloads", $"移动下载文件失败：part='{partPath}', dest='{destinationPath}'", ex);
                    attemptErrors.Add(new DownloadAttemptError(sourceId, ex.Message));
                    return DownloadResult.Fail("保存下载文件失败", attemptErrors);
                }

                ReportProgress(progress, sourceId, downloaded, totalBytes, status: DownloadProgressStatus.Completed);
                AppLog.Info("Downloads", $"下载完成：source={sourceId}, path='{destinationPath}', bytes={downloaded}");
                return DownloadResult.SuccessResult(destinationPath, sourceId);
            }
            catch (OperationCanceledException oce) when (!cancellationToken.IsCancellationRequested)
            {
                // attemptToken 被空闲超时取消：视为该源失败，切换下一源。
                attemptErrors.Add(new DownloadAttemptError(sourceId, $"下载超时/无进度：{oce.Message}"));
                return DownloadResult.Fail("下载超时/无进度", attemptErrors, oce);
            }
            catch (Exception ex)
            {
                attemptErrors.Add(new DownloadAttemptError(sourceId, ex.Message));
                return DownloadResult.Fail("下载失败", attemptErrors, ex);
            }
        }

        private static void ReportProgress(
            IProgress<DownloadProgress>? progress,
            DownloadSourceId sourceId,
            long downloadedBytes,
            long? totalBytes,
            DownloadProgressStatus status)
        {
            progress?.Report(new DownloadProgress
            {
                SourceId = sourceId,
                DownloadedBytes = downloadedBytes,
                TotalBytes = totalBytes,
                Status = status,
            });
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

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient
            {
                Timeout = Timeout.InfiniteTimeSpan,
            };

            // GitHub 与部分镜像对 User-Agent 有要求，这里统一添加。
            try
            {
                client.DefaultRequestHeaders.UserAgent.Clear();
                client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("WindBoard", AppInfo.Version));
            }
            catch
            {
                // 忽略：下载失败会由轮询兜底
            }

            return client;
        }
    }

    internal sealed class DownloadRequest
    {
        public string OriginalUrl { get; init; } = string.Empty;

        public string DestinationPath { get; init; } = string.Empty;

        public DownloadSourceId PreferredSourceId { get; init; } = DownloadSourceId.Github;

        public IReadOnlyList<DownloadSourceId>? FailoverOrder { get; init; }

        /// <summary>
        /// 最多轮询次数（每轮会按 FailoverOrder 依次尝试）。
        /// </summary>
        public int MaxCycles { get; init; } = 2;
    }

    internal sealed class DownloadProgress
    {
        public required DownloadSourceId SourceId { get; init; }

        public required long DownloadedBytes { get; init; }

        public required long? TotalBytes { get; init; }

        public required DownloadProgressStatus Status { get; init; }
    }

    internal enum DownloadProgressStatus
    {
        Downloading,
        Completed,
    }

    internal sealed class DownloadResult
    {
        public bool Success { get; init; }

        public string FilePath { get; init; } = string.Empty;

        public DownloadSourceId SourceId { get; init; } = DownloadSourceId.Github;

        public string ErrorMessage { get; init; } = string.Empty;

        public Exception? Error { get; init; }

        public IReadOnlyList<DownloadAttemptError> AttemptErrors { get; init; } = Array.Empty<DownloadAttemptError>();

        public static DownloadResult SuccessResult(string filePath, DownloadSourceId sourceId)
        {
            return new DownloadResult
            {
                Success = true,
                FilePath = filePath ?? string.Empty,
                SourceId = sourceId,
            };
        }

        public static DownloadResult Fail(string errorMessage, Exception? error = null)
        {
            return new DownloadResult
            {
                Success = false,
                ErrorMessage = errorMessage ?? string.Empty,
                Error = error,
            };
        }

        public static DownloadResult Fail(string errorMessage, IReadOnlyList<DownloadAttemptError> attemptErrors, Exception? error = null)
        {
            return new DownloadResult
            {
                Success = false,
                ErrorMessage = errorMessage ?? string.Empty,
                AttemptErrors = attemptErrors ?? Array.Empty<DownloadAttemptError>(),
                Error = error,
            };
        }
    }

    internal readonly struct DownloadAttemptError
    {
        internal DownloadSourceId SourceId { get; }

        internal string Message { get; }

        internal DownloadAttemptError(DownloadSourceId sourceId, string? message)
        {
            SourceId = sourceId;
            Message = message ?? string.Empty;
        }
    }
}

