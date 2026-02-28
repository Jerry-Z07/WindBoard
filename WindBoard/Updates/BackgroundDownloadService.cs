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
    internal static partial class BackgroundDownloadService
    {
        private static readonly HttpClient DownloadClient = CreateHttpClient();

        internal static Task<DownloadResult> DownloadWithFailoverAsync(
            DownloadRequest request,
            IProgress<DownloadProgress>? progress,
            CancellationToken cancellationToken)
        {
            return DownloadWithFailoverAsync(request, progress, cancellationToken, DownloadClient);
        }

        /// <summary>
        /// 带可注入 HttpClient 的下载入口（主要用于单元测试）。
        /// </summary>
        internal static async Task<DownloadResult> DownloadWithFailoverAsync(
            DownloadRequest request,
            IProgress<DownloadProgress>? progress,
            CancellationToken cancellationToken,
            HttpClient httpClient)
        {
            if (request is null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (httpClient is null)
            {
                throw new ArgumentNullException(nameof(httpClient));
            }

            DownloadResult? failure = TryCreateContext(request, progress, cancellationToken, httpClient, out DownloadContext context);
            if (failure is not null)
            {
                return failure;
            }

            return await DownloadWithFailoverCoreAsync(context).ConfigureAwait(false);
        }

        private static DownloadResult? TryCreateContext(
            DownloadRequest request,
            IProgress<DownloadProgress>? progress,
            CancellationToken cancellationToken,
            HttpClient httpClient,
            out DownloadContext context)
        {
            context = null!;

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

            context = new DownloadContext
            {
                HttpClient = httpClient,
                OriginalUrl = originalUrl,
                DestinationPath = destinationPath,
                PartPath = destinationPath + ".part",
                Order = order,
                MaxCycles = Math.Max(1, request.MaxCycles),
                IdleTimeout = request.IdleTimeout,
                Progress = progress,
                CancellationToken = cancellationToken,
            };

            return null;
        }

        private static async Task<DownloadResult> DownloadWithFailoverCoreAsync(DownloadContext context)
        {
            var attemptErrors = new List<DownloadAttemptError>();

            for (int cycle = 1; cycle <= context.MaxCycles; cycle++)
            {
                DownloadResult? success = await TryDownloadCycleAsync(context, attemptErrors).ConfigureAwait(false);
                if (success is not null)
                {
                    return success;
                }

                if (cycle < context.MaxCycles)
                {
                    await DelayBackoffAsync(context, cycle).ConfigureAwait(false);
                }
            }

            return DownloadResult.Fail("下载失败（已轮询切换下载源）", attemptErrors);
        }

        private static async Task<DownloadResult?> TryDownloadCycleAsync(DownloadContext context, List<DownloadAttemptError> attemptErrors)
        {
            foreach (DownloadSourceId source in context.Order)
            {
                context.CancellationToken.ThrowIfCancellationRequested();

                DownloadResult? success = await TryDownloadFromSourceAsync(context, source, attemptErrors).ConfigureAwait(false);
                if (success is not null)
                {
                    return success;
                }
            }

            return null;
        }

        private static async Task<DownloadResult?> TryDownloadFromSourceAsync(
            DownloadContext context,
            DownloadSourceId sourceId,
            List<DownloadAttemptError> attemptErrors)
        {
            try
            {
                DownloadResult result = await DownloadFromSourceAsync(context, sourceId).ConfigureAwait(false);

                if (result.Success)
                {
                    return result;
                }

                attemptErrors.AddRange(result.AttemptErrors);
                return null;
            }
            catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
            {
                // 外部取消：直接向上抛出，避免被当成“下载失败”记录。
                throw;
            }
            catch (Exception ex)
            {
                attemptErrors.Add(new DownloadAttemptError(sourceId, ex.Message));
                AppLog.Warn("Downloads", $"下载失败，将切换下载源：source={sourceId}", ex);
                return null;
            }
        }

        private static async Task DelayBackoffAsync(DownloadContext context, int cycle)
        {
            // 一轮都失败：退避后再试下一轮（镜像源可能短暂不可用）。
            TimeSpan backoff = TimeSpan.FromMilliseconds(350 * cycle);
            try
            {
                await Task.Delay(backoff, context.CancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // 忽略：取消会在下一轮开始前抛出
            }
        }

        private sealed class DownloadContext
        {
            internal required HttpClient HttpClient { get; init; }

            internal required string OriginalUrl { get; init; }

            internal required string DestinationPath { get; init; }

            internal required string PartPath { get; init; }

            internal required IReadOnlyList<DownloadSourceId> Order { get; init; }

            internal required int MaxCycles { get; init; }

            internal required TimeSpan IdleTimeout { get; init; }

            internal IProgress<DownloadProgress>? Progress { get; init; }

            internal required CancellationToken CancellationToken { get; init; }
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
}

