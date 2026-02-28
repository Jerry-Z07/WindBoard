using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using WindBoard.Settings;
using WindBoard.Updates;
using Xunit;

namespace WindBoard.Tests.Updates;

public sealed class BackgroundDownloadServiceTests
{
    [Fact]
    public async Task DownloadWithFailoverAsync_Should_Failover_To_Next_Source()
    {
        string tempDir = CreateTempDirectory();
        try
        {
            string destinationPath = Path.Combine(tempDir, "asset.bin");
            byte[] payload = [ 1, 2, 3, 4, 5 ];

            int proxyCalls = 0;
            int githubCalls = 0;

            using var httpClient = new HttpClient(new DelegateHttpMessageHandler((req, _) =>
            {
                string url = req.RequestUri?.ToString() ?? string.Empty;
                if (url.StartsWith(DownloadSourceUrlRewriter.GhProxyPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    Interlocked.Increment(ref proxyCalls);
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
                }

                Interlocked.Increment(ref githubCalls);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(payload),
                });
            }))
            {
                Timeout = Timeout.InfiniteTimeSpan,
            };

            var request = new DownloadRequest
            {
                OriginalUrl = "https://github.com/windboard/test/releases/download/v1/asset.bin",
                DestinationPath = destinationPath,
                FailoverOrder = new List<DownloadSourceId> { DownloadSourceId.GhProxy, DownloadSourceId.Github },
                MaxCycles = 1,
                IdleTimeout = Timeout.InfiniteTimeSpan,
            };

            DownloadResult result = await BackgroundDownloadService.DownloadWithFailoverAsync(
                    request,
                    progress: null,
                    cancellationToken: CancellationToken.None,
                    httpClient);

            Assert.True(result.Success);
            Assert.Equal(destinationPath, result.FilePath);
            Assert.True(File.Exists(destinationPath));
            Assert.Equal(payload, File.ReadAllBytes(destinationPath));

            Assert.Equal(1, proxyCalls);
            Assert.Equal(1, githubCalls);
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public async Task DownloadWithFailoverAsync_Should_Retry_Without_Range_When_416()
    {
        string tempDir = CreateTempDirectory();
        try
        {
            string destinationPath = Path.Combine(tempDir, "asset.bin");
            string partPath = destinationPath + ".part";
            File.WriteAllBytes(partPath, new byte[] { 9, 9, 9 });

            byte[] payload = [ 7, 7, 7, 7 ];

            int calls = 0;
            bool sawRangeRequest = false;
            bool sawNonRangeRequest = false;

            using var httpClient = new HttpClient(new DelegateHttpMessageHandler((req, _) =>
            {
                int current = Interlocked.Increment(ref calls);
                bool hasRange = req.Headers.Range is not null;

                if (current == 1)
                {
                    sawRangeRequest = hasRange;
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable));
                }

                sawNonRangeRequest = !hasRange;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(payload),
                });
            }))
            {
                Timeout = Timeout.InfiniteTimeSpan,
            };

            var request = new DownloadRequest
            {
                OriginalUrl = "https://github.com/windboard/test/releases/download/v1/asset.bin",
                DestinationPath = destinationPath,
                FailoverOrder = new List<DownloadSourceId> { DownloadSourceId.Github },
                MaxCycles = 1,
                IdleTimeout = Timeout.InfiniteTimeSpan,
            };

            DownloadResult result = await BackgroundDownloadService.DownloadWithFailoverAsync(
                    request,
                    progress: null,
                    cancellationToken: CancellationToken.None,
                    httpClient);

            Assert.True(result.Success);
            Assert.True(sawRangeRequest);
            Assert.True(sawNonRangeRequest);
            Assert.Equal(2, calls);

            Assert.Equal(payload, File.ReadAllBytes(destinationPath));
            Assert.False(File.Exists(partPath));
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public async Task DownloadWithFailoverAsync_Should_Fail_When_IdleTimeout_NoProgress()
    {
        string tempDir = CreateTempDirectory();
        try
        {
            string destinationPath = Path.Combine(tempDir, "asset.bin");

            using var httpClient = new HttpClient(new DelegateHttpMessageHandler((_, _) =>
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StreamContent(new HangingStream()),
                });
            }))
            {
                Timeout = Timeout.InfiniteTimeSpan,
            };

            var request = new DownloadRequest
            {
                OriginalUrl = "https://github.com/windboard/test/releases/download/v1/asset.bin",
                DestinationPath = destinationPath,
                FailoverOrder = new List<DownloadSourceId> { DownloadSourceId.Github },
                MaxCycles = 1,
                IdleTimeout = TimeSpan.FromMilliseconds(200),
            };

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            DownloadResult result = await BackgroundDownloadService.DownloadWithFailoverAsync(
                    request,
                    progress: null,
                    cancellationToken: cts.Token,
                    httpClient);

            Assert.False(result.Success);
            Assert.NotEmpty(result.AttemptErrors);
            Assert.Contains(result.AttemptErrors, e => e.Message.Contains("超时", StringComparison.Ordinal));
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public async Task DownloadWithFailoverAsync_Should_Validate_OriginalUrl_And_DestinationPath()
    {
        using var httpClient = new HttpClient(new DelegateHttpMessageHandler((_, _) =>
        {
            throw new InvalidOperationException("不应触发网络请求");
        }))
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };

        DownloadResult missingUrl = await BackgroundDownloadService.DownloadWithFailoverAsync(
                new DownloadRequest { OriginalUrl = "", DestinationPath = "C:\\temp\\a.bin" },
                progress: null,
                cancellationToken: CancellationToken.None,
                httpClient);

        DownloadResult missingPath = await BackgroundDownloadService.DownloadWithFailoverAsync(
                new DownloadRequest { OriginalUrl = "https://github.com/windboard/test/asset.bin", DestinationPath = "" },
                progress: null,
                cancellationToken: CancellationToken.None,
                httpClient);

        Assert.False(missingUrl.Success);
        Assert.False(missingPath.Success);
        Assert.Contains("OriginalUrl", missingUrl.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains("DestinationPath", missingPath.ErrorMessage, StringComparison.Ordinal);
    }

    private static string CreateTempDirectory()
    {
        string dir = Path.Combine(Path.GetTempPath(), "WindBoard.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void TryDeleteDirectory(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch
        {
            // 忽略：测试清理失败不应影响结果
        }
    }

    private sealed class DelegateHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public DelegateHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return _handler(request, cancellationToken);
        }
    }

    private sealed class HangingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
            return new ValueTask<int>(tcs.Task);
        }
    }
}
