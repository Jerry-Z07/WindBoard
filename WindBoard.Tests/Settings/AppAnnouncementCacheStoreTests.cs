using System;
using System.IO;
using WindBoard.Settings;

namespace WindBoard.Tests.Settings;

public sealed class AppAnnouncementCacheStoreTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly AppAnnouncementCacheStore _store;

    public AppAnnouncementCacheStoreTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "WindBoard.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _store = new AppAnnouncementCacheStore(Path.Combine(_tempDirectory, "announcements.cache.json"));
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
    public void TrySave_ThenTryLoad_RoundTrips()
    {
        DateTimeOffset fetchedAt = new(2026, 1, 1, 8, 30, 0, TimeSpan.Zero);
        AppAnnouncementCache cache = new()
        {
            SchemaVersion = 1,
            Content = "{\"schemaVersion\":1,\"announcements\":[]}",
            ETag = "\"etag-1\"",
            LastModified = "Wed, 21 Oct 2015 07:28:00 GMT",
            FetchedAtUtc = fetchedAt,
        };

        Assert.True(_store.TrySave(cache));
        Assert.True(_store.TryLoad(out AppAnnouncementCache? loaded));
        Assert.NotNull(loaded);

        AppAnnouncementCache loadedCache = loaded!;
        Assert.Equal(cache.SchemaVersion, loadedCache.SchemaVersion);
        Assert.Equal(cache.Content, loadedCache.Content);
        Assert.Equal(cache.ETag, loadedCache.ETag);
        Assert.Equal(cache.LastModified, loadedCache.LastModified);
        Assert.Equal(fetchedAt, loadedCache.FetchedAtUtc);

        // 原子写不应残留临时文件。
        Assert.False(File.Exists(_store.FilePath + ".tmp"));
    }

    [Fact]
    public void TryLoad_MissingFile_ReturnsFalse()
    {
        Assert.False(_store.TryLoad(out AppAnnouncementCache? cache));
        Assert.Null(cache);
    }

    [Fact]
    public void TryLoad_CorruptedFile_ReturnsFalse()
    {
        File.WriteAllText(_store.FilePath, "not-json");

        Assert.False(_store.TryLoad(out AppAnnouncementCache? cache));
        Assert.Null(cache);
    }

    [Fact]
    public void TryLoad_EmptyContent_ReturnsFalse()
    {
        Assert.True(_store.TrySave(new AppAnnouncementCache
        {
            SchemaVersion = 1,
            Content = string.Empty,
            FetchedAtUtc = DateTimeOffset.UtcNow,
        }));

        Assert.False(_store.TryLoad(out AppAnnouncementCache? cache));
        Assert.Null(cache);
    }
}
