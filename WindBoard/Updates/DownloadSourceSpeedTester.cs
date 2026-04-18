using System;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using WindBoard.Logging;
using WindBoard.Settings;

namespace WindBoard.Updates
{
    /// <summary>
    /// 下载源测速：
    /// - 通过下载一个小文件（默认 latest.json）测量耗时
    /// - 用于自动选择最快镜像源
    /// </summary>
    internal static class DownloadSourceSpeedTester
    {
        private static readonly HttpClient SpeedTestClient = CreateHttpClient();

        internal static async Task<DownloadSourceSpeedTestResult[]> TestAsync(string originalUrl, CancellationToken cancellationToken)
        {
            DownloadSourceId[] sources =
            [
                DownloadSourceId.Github,
                DownloadSourceId.GhProxy,
                DownloadSourceId.ZeroSeven,
            ];

            var tasks = new Task<DownloadSourceSpeedTestResult>[sources.Length];
            for (int i = 0; i < sources.Length; i++)
            {
                DownloadSourceId source = sources[i];
                tasks[i] = TestOneAsync(originalUrl, source, cancellationToken);
            }

            return await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        internal static bool TryPickFastest(DownloadSourceSpeedTestResult[] results, out DownloadSourceId fastest)
        {
            fastest = DownloadSourceId.Github;

            if (results is null || results.Length == 0)
            {
                return false;
            }

            DownloadSourceSpeedTestResult? best = null;
            foreach (DownloadSourceSpeedTestResult r in results)
            {
                if (!r.Success)
                {
                    continue;
                }

                if (best is null || r.DurationMs < best.Value.DurationMs)
                {
                    best = r;
                }
            }

            if (best is null)
            {
                return false;
            }

            fastest = best.Value.SourceId;
            return true;
        }

        private static async Task<DownloadSourceSpeedTestResult> TestOneAsync(
            string originalUrl,
            DownloadSourceId sourceId,
            CancellationToken cancellationToken)
        {
            string url = DownloadSourceUrlRewriter.Rewrite(originalUrl, sourceId);

            // 每个源单独设置短超时，避免某个源卡死拖慢整体测速。
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(6));

            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                using HttpRequestMessage req = new(HttpMethod.Get, url);
                using HttpResponseMessage resp = await SpeedTestClient
                    .SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token)
                    .ConfigureAwait(false);

                if (!resp.IsSuccessStatusCode)
                {
                    return DownloadSourceSpeedTestResult.Fail(sourceId, (long)sw.Elapsed.TotalMilliseconds, $"HTTP {(int)resp.StatusCode}");
                }

                // 读取一小段内容即可：latest.json 很小，读完也不会明显增加耗时。
                await using var stream = await resp.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);
                byte[] buffer = new byte[16 * 1024];
                int totalRead = 0;
                while (totalRead < 64 * 1024)
                {
                    int read = await stream.ReadAsync(buffer, cts.Token).ConfigureAwait(false);
                    if (read <= 0)
                    {
                        break;
                    }

                    totalRead += read;
                }

                return DownloadSourceSpeedTestResult.SuccessResult(sourceId, (long)sw.Elapsed.TotalMilliseconds);
            }
            catch (Exception ex)
            {
                AppLog.Debug("Updates", $"下载源测速失败：source={sourceId}, url='{url}', error='{ex.GetType().Name}: {ex.Message}'");
                return DownloadSourceSpeedTestResult.Fail(sourceId, (long)sw.Elapsed.TotalMilliseconds, ex.Message);
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
                // 忽略：测速失败不影响主流程
            }

            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return client;
        }
    }

    internal readonly struct DownloadSourceSpeedTestResult
    {
        internal DownloadSourceId SourceId { get; }

        internal bool Success { get; }

        internal long DurationMs { get; }

        internal string ErrorMessage { get; }

        private DownloadSourceSpeedTestResult(DownloadSourceId sourceId, bool success, long durationMs, string errorMessage)
        {
            SourceId = sourceId;
            Success = success;
            DurationMs = durationMs;
            ErrorMessage = errorMessage ?? string.Empty;
        }

        internal static DownloadSourceSpeedTestResult SuccessResult(DownloadSourceId sourceId, long durationMs)
        {
            return new DownloadSourceSpeedTestResult(sourceId, success: true, durationMs, errorMessage: string.Empty);
        }

        internal static DownloadSourceSpeedTestResult Fail(DownloadSourceId sourceId, long durationMs, string? errorMessage)
        {
            return new DownloadSourceSpeedTestResult(sourceId, success: false, durationMs, errorMessage ?? string.Empty);
        }
    }
}
