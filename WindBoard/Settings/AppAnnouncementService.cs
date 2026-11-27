using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using WindBoard.Logging;
using WindBoard.Updates;

namespace WindBoard.Settings
{
    /// <summary>
    /// 公告来源：单条请求的结果。
    /// </summary>
    internal enum AppAnnouncementFetchOutcome
    {
        Success,
        NotModified,
        Failure,
    }

    /// <summary>
    /// 单条公告请求（URL 已按下载源改写，条件请求头来自上一次成功拉取）。
    /// </summary>
    internal sealed record AppAnnouncementFetchRequest(string Url, string? ETag, string? LastModified);

    /// <summary>
    /// 单条公告请求的结果。
    /// </summary>
    internal sealed record AppAnnouncementFetchResult(
        AppAnnouncementFetchOutcome Outcome,
        string? Content = null,
        string? ETag = null,
        string? LastModified = null,
        Exception? Error = null)
    {
        internal static AppAnnouncementFetchResult FromSuccess(string content, string? etag, string? lastModified)
        {
            return new AppAnnouncementFetchResult(AppAnnouncementFetchOutcome.Success, content, etag, lastModified);
        }

        internal static AppAnnouncementFetchResult NotModified(string? etag, string? lastModified)
        {
            return new AppAnnouncementFetchResult(AppAnnouncementFetchOutcome.NotModified, null, etag, lastModified);
        }

        internal static AppAnnouncementFetchResult Failure(Exception? error = null)
        {
            return new AppAnnouncementFetchResult(AppAnnouncementFetchOutcome.Failure, Error: error);
        }
    }

    /// <summary>
    /// 远程公告服务：拉取仓库根 announcements.json、校验、缓存，并向界面提供快照。
    /// 只有本服务访问网络；所有失败都在内部降级为“沿用现有内容”。
    /// </summary>
    internal sealed class AppAnnouncementService
    {
        private const string LogCategory = "Announcements";

        private static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(6);

        private static readonly string AnnouncementsUrl =
            $"https://raw.githubusercontent.com/{UpdateConstants.RepoOwner}/{UpdateConstants.RepoName}/main/announcements.json";

        private static readonly HttpClient AnnouncementHttpClient = CreateHttpClient();

        private readonly object _gate = new();
        private readonly SemaphoreSlim _refreshGate = new(1, 1);
        private readonly Func<AppAnnouncementFetchRequest, CancellationToken, Task<AppAnnouncementFetchResult>> _fetch;
        private readonly AppAnnouncementCacheStore _cacheStore;
        private readonly Func<DateTimeOffset> _clock;

        private IReadOnlyList<AppAnnouncement> _current = Array.Empty<AppAnnouncement>();
        private string? _content;
        private string? _etag;
        private string? _lastModified;
        private DateTimeOffset? _fetchedAtUtc;

        internal static AppAnnouncementService Instance { get; } = new();

        private AppAnnouncementService()
            : this(FetchFromUrlAsync, AppAnnouncementCacheStore.CreateDefault(), static () => DateTimeOffset.UtcNow)
        {
        }

        internal AppAnnouncementService(
            Func<AppAnnouncementFetchRequest, CancellationToken, Task<AppAnnouncementFetchResult>> fetch,
            AppAnnouncementCacheStore cacheStore,
            Func<DateTimeOffset> clock)
        {
            _fetch = fetch ?? throw new ArgumentNullException(nameof(fetch));
            _cacheStore = cacheStore ?? throw new ArgumentNullException(nameof(cacheStore));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));

            LoadCachedSnapshot();
        }

        /// <summary>
        /// 快照变化通知；可能在后台线程触发，订阅方需自行切回 UI 线程。
        /// </summary>
        internal event EventHandler? Changed;

        /// <summary>
        /// 当前公告快照（来自缓存或最近一次成功拉取）。
        /// </summary>
        internal IReadOnlyList<AppAnnouncement> Current
        {
            get
            {
                lock (_gate)
                {
                    return _current;
                }
            }
        }

        /// <summary>
        /// 超过 TTL 时才拉取；并发调用只有一个会真正发请求。
        /// 方法内部捕获全部异常，可直接 fire-and-forget。
        /// </summary>
        internal async Task RefreshIfStaleAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    DateTimeOffset now = _clock();
                    // 未来时间戳（系统时钟回拨或手工改缓存）视为过期，与更新检查的 “lastCheck 在未来即到期” 语义一致。
                    if (_fetchedAtUtc is { } fetchedAt
                        && fetchedAt <= now
                        && now - fetchedAt < RefreshInterval)
                    {
                        return;
                    }

                    await RefreshCoreAsync(now, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    _refreshGate.Release();
                }
            }
            catch (OperationCanceledException)
            {
                // 调用方取消属于正常控制流：保留现有快照。
            }
            catch (Exception ex)
            {
                AppLog.Warn(LogCategory, "刷新公告失败，沿用现有内容", ex);
            }
        }

        private async Task RefreshCoreAsync(DateTimeOffset now, CancellationToken cancellationToken)
        {
            IReadOnlyList<DownloadSourceId> order = DownloadSourceUrlRewriter.BuildFailoverOrder(DownloadSourceId.Github);
            Exception? lastError = null;

            foreach (DownloadSourceId source in order)
            {
                string url = DownloadSourceUrlRewriter.Rewrite(AnnouncementsUrl, source);
                AppAnnouncementFetchResult result;
                try
                {
                    result = await _fetch(
                            new AppAnnouncementFetchRequest(url, _etag, _lastModified),
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // 请求超时（HttpClient.Timeout）按传输失败处理，继续尝试下一个源。
                    lastError = new HttpRequestException("公告请求超时");
                    AppLog.Debug(LogCategory, $"拉取公告超时，将切换源：source={source}");
                    continue;
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    lastError = ex;
                    AppLog.Debug(LogCategory, $"拉取公告失败，将切换源：source={source}", ex);
                    continue;
                }

                switch (result.Outcome)
                {
                    case AppAnnouncementFetchOutcome.NotModified:
                        _fetchedAtUtc = now;
                        if (!string.IsNullOrWhiteSpace(result.ETag))
                        {
                            _etag = result.ETag;
                        }

                        if (!string.IsNullOrWhiteSpace(result.LastModified))
                        {
                            _lastModified = result.LastModified;
                        }

                        TryPersistCache(now);
                        return;

                    case AppAnnouncementFetchOutcome.Success:
                        if (!ApplyContent(result, now, source))
                        {
                            // 解析/校验失败不再尝试镜像：镜像内容同样不可信，保留旧快照。
                            return;
                        }

                        return;

                    default:
                        lastError = result.Error;
                        AppLog.Debug(LogCategory, $"拉取公告失败，将切换源：source={source}", result.Error);
                        continue;
                }
            }

            AppLog.Warn(
                LogCategory,
                $"所有公告源均不可用，沿用现有内容：sourceCount={order.Count}, lastError='{lastError?.Message ?? "(none)"}'",
                lastError);
        }

        private bool ApplyContent(AppAnnouncementFetchResult result, DateTimeOffset now, DownloadSourceId source)
        {
            if (!AppAnnouncementPayloadParser.TryParse(result.Content, out IReadOnlyList<AppAnnouncement> parsed, out string? error))
            {
                AppLog.Warn(LogCategory, $"公告内容校验失败，保留旧内容：source={source}, error='{error}'");
                return false;
            }

            _content = result.Content;
            _etag = result.ETag;
            _lastModified = result.LastModified;
            _fetchedAtUtc = now;

            SetCurrent(parsed);
            TryPersistCache(now);
            return true;
        }

        private void SetCurrent(IReadOnlyList<AppAnnouncement> current)
        {
            lock (_gate)
            {
                _current = current;
            }

            Changed?.Invoke(this, EventArgs.Empty);
        }

        private void TryPersistCache(DateTimeOffset fetchedAtUtc)
        {
            string? content = _content;
            if (string.IsNullOrWhiteSpace(content))
            {
                return;
            }

            _cacheStore.TrySave(new AppAnnouncementCache
            {
                SchemaVersion = AppAnnouncementPayloadParser.SupportedSchemaVersion,
                Content = content,
                ETag = _etag,
                LastModified = _lastModified,
                FetchedAtUtc = fetchedAtUtc,
            });
        }

        private void LoadCachedSnapshot()
        {
            if (!_cacheStore.TryLoad(out AppAnnouncementCache? cache) || cache is null)
            {
                return;
            }

            if (cache.SchemaVersion != AppAnnouncementPayloadParser.SupportedSchemaVersion)
            {
                AppLog.Warn(LogCategory, $"忽略不兼容的公告缓存：schemaVersion={cache.SchemaVersion}");
                return;
            }

            // 缓存内容每次启动都重新走当前版本的解析器，避免旧版本写入的非法内容直接展示。
            if (!AppAnnouncementPayloadParser.TryParse(cache.Content, out IReadOnlyList<AppAnnouncement> cached, out string? error))
            {
                AppLog.Warn(LogCategory, $"忽略非法公告缓存：error='{error}'");
                return;
            }

            _current = cached;
            _content = cache.Content;
            _etag = cache.ETag;
            _lastModified = cache.LastModified;
            _fetchedAtUtc = cache.FetchedAtUtc;
        }

        private static async Task<AppAnnouncementFetchResult> FetchFromUrlAsync(
            AppAnnouncementFetchRequest request,
            CancellationToken cancellationToken)
        {
            using HttpRequestMessage message = new(HttpMethod.Get, request.Url);
            if (!string.IsNullOrWhiteSpace(request.ETag))
            {
                message.Headers.TryAddWithoutValidation("If-None-Match", request.ETag);
            }

            if (!string.IsNullOrWhiteSpace(request.LastModified))
            {
                message.Headers.TryAddWithoutValidation("If-Modified-Since", request.LastModified);
            }

            // 读完整个响应体才算完成：公告响应很小，这样 12 秒超时覆盖正文读取，
            // 不会出现镜像发完响应头后挂起正文、刷新一直占着并发闸的情况。
            using HttpResponseMessage response = await AnnouncementHttpClient
                .SendAsync(message, cancellationToken)
                .ConfigureAwait(false);

            string? etag = response.Headers.ETag?.ToString() ?? request.ETag;
            string? lastModified = response.Content.Headers.LastModified?.ToString("R", CultureInfo.InvariantCulture)
                ?? request.LastModified;

            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                return AppAnnouncementFetchResult.NotModified(etag, lastModified);
            }

            if (!response.IsSuccessStatusCode)
            {
                return AppAnnouncementFetchResult.Failure(
                    new HttpRequestException($"HTTP 请求失败：{(int)response.StatusCode} {response.ReasonPhrase}"));
            }

            string content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return AppAnnouncementFetchResult.FromSuccess(content, etag, lastModified);
        }

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(12),
            };

            // GitHub 对部分请求会要求带 User-Agent；这里统一添加。
            try
            {
                client.DefaultRequestHeaders.UserAgent.Clear();
                client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("WindBoard", AppInfo.Version));
            }
            catch (Exception ex)
            {
                // 版本号来自程序集元数据，格式异常时仅失去 User-Agent，不应让静态初始化失败。
                AppLog.Warn(LogCategory, "设置公告请求的 User-Agent 失败，将继续不带版本号发送", ex);
            }

            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return client;
        }
    }
}
