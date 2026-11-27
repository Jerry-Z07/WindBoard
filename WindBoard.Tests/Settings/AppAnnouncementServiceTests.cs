using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using WindBoard.Settings;

namespace WindBoard.Tests.Settings;

public sealed class AppAnnouncementServiceTests : IDisposable
{
    private const string ValidPayload = """
        {
          "schemaVersion": 1,
          "announcements": [
            {
              "id": "announcement-one",
              "severity": "warning",
              "text": { "zh-CN": { "title": "title", "message": "message" } }
            }
          ]
        }
        """;

    private const string InvalidPayload = """
        {
          "schemaVersion": 2,
          "announcements": []
        }
        """;

    private static readonly DateTimeOffset StartTime = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly string _tempDirectory;
    private readonly AppAnnouncementCacheStore _cacheStore;

    public AppAnnouncementServiceTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "WindBoard.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _cacheStore = new AppAnnouncementCacheStore(Path.Combine(_tempDirectory, "announcements.cache.json"));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }
        catch
        {
            // 清理失败不影响测试结论。
        }
    }

    [Fact]
    public async Task RefreshIfStaleAsync_WithinTtl_DoesNotRequestAgain()
    {
        DateTimeOffset now = StartTime;
        List<string> urls = new();
        AppAnnouncementService service = CreateService(
            (request, _) =>
            {
                urls.Add(request.Url);
                return Task.FromResult(AppAnnouncementFetchResult.FromSuccess(ValidPayload, "\"etag-1\"", null));
            },
            () => now);

        await service.RefreshIfStaleAsync();
        await service.RefreshIfStaleAsync();

        Assert.Single(urls);
        Assert.Single(service.Current);
    }

    [Fact]
    public async Task RefreshIfStaleAsync_AfterTtl_RequestsAgain()
    {
        DateTimeOffset now = StartTime;
        int calls = 0;
        AppAnnouncementService service = CreateService(
            (_, _) =>
            {
                calls++;
                return Task.FromResult(AppAnnouncementFetchResult.FromSuccess(ValidPayload, null, null));
            },
            () => now);

        await service.RefreshIfStaleAsync();
        now = now.AddHours(7);
        await service.RefreshIfStaleAsync();

        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task RefreshIfStaleAsync_CacheFetchedAtInFuture_TreatsAsStale()
    {
        Assert.True(_cacheStore.TrySave(new AppAnnouncementCache
        {
            SchemaVersion = 1,
            Content = ValidPayload,
            FetchedAtUtc = StartTime.AddHours(1),
        }));

        int calls = 0;
        AppAnnouncementService service = CreateService(
            (_, _) =>
            {
                calls++;
                return Task.FromResult(AppAnnouncementFetchResult.FromSuccess(ValidPayload, null, null));
            },
            () => StartTime);

        await service.RefreshIfStaleAsync();

        // 系统时钟回拨会让缓存时间戳落在未来；此时必须按过期处理，否则会长期不刷新。
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Constructor_CacheWithUnsupportedSchemaVersion_IsIgnored()
    {
        Assert.True(_cacheStore.TrySave(new AppAnnouncementCache
        {
            SchemaVersion = AppAnnouncementPayloadParser.SupportedSchemaVersion + 1,
            Content = ValidPayload,
            FetchedAtUtc = StartTime,
        }));

        AppAnnouncementService service = CreateService(
            (_, _) => Task.FromResult(AppAnnouncementFetchResult.Failure()),
            () => StartTime);

        Assert.Empty(service.Current);
    }

    [Fact]
    public async Task RefreshIfStaleAsync_NotModified_UpdatesFetchedAtWithoutChanged()
    {
        DateTimeOffset now = StartTime;
        List<AppAnnouncementFetchRequest> requests = new();
        int changedCount = 0;
        AppAnnouncementService service = CreateService(
            (request, _) =>
            {
                requests.Add(request);
                return Task.FromResult(requests.Count == 1
                    ? AppAnnouncementFetchResult.FromSuccess(ValidPayload, "\"etag-1\"", null)
                    : AppAnnouncementFetchResult.NotModified("\"etag-1\"", null));
            },
            () => now);
        service.Changed += (_, _) => changedCount++;

        await service.RefreshIfStaleAsync();
        now = now.AddHours(7);
        await service.RefreshIfStaleAsync();

        Assert.Equal(2, requests.Count);
        Assert.Equal("\"etag-1\"", requests[1].ETag);
        Assert.Equal(1, changedCount);
        Assert.Single(service.Current);
        Assert.True(_cacheStore.TryLoad(out AppAnnouncementCache? cache));
        Assert.Equal(now, cache!.FetchedAtUtc);
    }

    [Fact]
    public async Task RefreshIfStaleAsync_TransportFailures_FollowFailoverOrder()
    {
        List<string> urls = new();
        AppAnnouncementService service = CreateService(
            (request, _) =>
            {
                urls.Add(request.Url);

                // 第一个源直接抛传输异常，第二个源返回失败结果，验证两种失败形态都会切到下一个源。
                if (urls.Count == 1)
                {
                    throw new HttpRequestException("boom");
                }

                return Task.FromResult(urls.Count < 3
                    ? AppAnnouncementFetchResult.Failure(new HttpRequestException("boom"))
                    : AppAnnouncementFetchResult.FromSuccess(ValidPayload, null, null));
            },
            () => StartTime);

        await service.RefreshIfStaleAsync();

        Assert.Equal(3, urls.Count);
        Assert.Equal("https://raw.githubusercontent.com/Jerry-Z07/WindBoard/main/announcements.json", urls[0]);
        Assert.Equal(
            "https://gh-proxy.top/https://raw.githubusercontent.com/Jerry-Z07/WindBoard/main/announcements.json",
            urls[1]);
        Assert.Equal(
            "https://ghm.078465.xyz/https://raw.githubusercontent.com/Jerry-Z07/WindBoard/main/announcements.json",
            urls[2]);
        Assert.Single(service.Current);
    }

    [Fact]
    public async Task RefreshIfStaleAsync_InvalidContent_StopsFailoverAndKeepsCachedSnapshot()
    {
        string cachedPayload = ValidPayload.Replace("announcement-one", "cached-announcement", StringComparison.Ordinal);
        Assert.True(_cacheStore.TrySave(new AppAnnouncementCache
        {
            SchemaVersion = 1,
            Content = cachedPayload,
            ETag = "\"cached\"",
            FetchedAtUtc = StartTime.AddHours(-7),
        }));

        int calls = 0;
        AppAnnouncementService service = CreateService(
            (_, _) =>
            {
                calls++;
                return Task.FromResult(AppAnnouncementFetchResult.FromSuccess(InvalidPayload, "\"new\"", null));
            },
            () => StartTime);

        Assert.Single(service.Current);
        Assert.Equal("cached-announcement", service.Current[0].Id);

        await service.RefreshIfStaleAsync();

        // 解析失败不再尝试镜像，且旧快照保持不变。
        Assert.Equal(1, calls);
        Assert.Single(service.Current);
        Assert.Equal("cached-announcement", service.Current[0].Id);
    }

    [Fact]
    public async Task RefreshIfStaleAsync_AllSourcesFail_KeepsSnapshotAndDoesNotThrow()
    {
        int calls = 0;
        AppAnnouncementService service = CreateService(
            (_, _) =>
            {
                calls++;
                return Task.FromResult(AppAnnouncementFetchResult.Failure(new HttpRequestException("offline")));
            },
            () => StartTime);

        await service.RefreshIfStaleAsync();

        Assert.Equal(3, calls);
        Assert.Empty(service.Current);
    }

    [Fact]
    public async Task RefreshIfStaleAsync_Success_RaisesChangedAndWritesCache()
    {
        int changedCount = 0;
        AppAnnouncementService service = CreateService(
            (_, _) => Task.FromResult(AppAnnouncementFetchResult.FromSuccess(ValidPayload, "\"etag-1\"", "Wed, 21 Oct 2015 07:28:00 GMT")),
            () => StartTime);
        service.Changed += (_, _) => changedCount++;

        await service.RefreshIfStaleAsync();

        Assert.Equal(1, changedCount);
        Assert.Single(service.Current);
        Assert.True(_cacheStore.TryLoad(out AppAnnouncementCache? cache));
        Assert.Equal(ValidPayload, cache!.Content);
        Assert.Equal("\"etag-1\"", cache.ETag);
        Assert.Equal("Wed, 21 Oct 2015 07:28:00 GMT", cache.LastModified);
        Assert.Equal(StartTime, cache.FetchedAtUtc);
    }

    private AppAnnouncementService CreateService(
        Func<AppAnnouncementFetchRequest, CancellationToken, Task<AppAnnouncementFetchResult>> fetch,
        Func<DateTimeOffset> clock)
    {
        return new AppAnnouncementService(fetch, _cacheStore, clock);
    }
}
