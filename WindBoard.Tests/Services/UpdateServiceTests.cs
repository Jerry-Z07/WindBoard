using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WindBoard.Models.Update;
using WindBoard.Services;
using WindBoard.Services.Notifications;
using Xunit;

namespace WindBoard.Tests.Services;

public sealed class UpdateServiceTests
{
    [Fact]
    public void TryParseStableVersion_StripsPrefixAndPrerelease()
    {
        Assert.Equal(new System.Version(1, 2, 3), UpdateService.TryParseStableVersion("v1.2.3-beta+build.7"));
        Assert.Equal(new System.Version(10, 0, 26100, 0), UpdateService.TryParseStableVersion("10.0.26100.0"));
        Assert.Null(UpdateService.TryParseStableVersion("not-a-version"));
    }

    [Fact]
    public void GetMatchingAsset_PrefersSelfContained()
    {
        var info = new UpdateInfo
        {
            Assets =
            {
                new UpdateAsset { Arch = "x64", Runtime = "framework-dependent", FileName = "fd.zip", DownloadUrl = "https://example/fd.zip" },
                new UpdateAsset { Arch = "x64", Runtime = "self-contained", FileName = "sc.zip", DownloadUrl = "https://example/sc.zip" }
            }
        };

        UpdateAsset? selected = UpdateService.GetMatchingAsset(info, arch: "x64", preferSelfContained: true);

        Assert.NotNull(selected);
        Assert.Equal("self-contained", selected!.Runtime);
    }

    [Fact]
    public void SelectAssetForInstallation_InstallerMode_PicksInstaller()
    {
        var info = new UpdateInfo
        {
            Assets =
            {
                new UpdateAsset { Arch = "x64", Runtime = "self-contained", FileName = "sc.zip", DownloadUrl = "https://example/sc.zip" },
                new UpdateAsset { Arch = "x64", Runtime = "installer", FileName = "setup.exe", DownloadUrl = "https://example/setup.exe" }
            }
        };

        var env = new InstallEnvironment(InstallMode.InstallerPerMachine, DeploymentRuntime.SelfContained, executablePath: "C:\\Program Files\\WindBoard\\WindBoard.exe", installRoot: "C:\\Program Files\\WindBoard");

        UpdateAsset? selected = UpdateService.SelectAssetForInstallation(info, env, arch: "x64");

        Assert.NotNull(selected);
        Assert.Equal("installer", selected!.Runtime);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_ParsesLatestJsonAndDetectsUpdate()
    {
        const string latestJson = """
        {
          "version": "9999.0.0",
          "versionName": "v9999.0.0",
          "releaseDate": "2024-01-15T10:30:00Z",
          "minSystemVersion": "10.0.0.0",
          "changelog": { "en-US": "Test" },
          "assets": [
            {
              "arch": "x64",
              "runtime": "self-contained",
              "fileName": "WindBoard-9999.0.0-win-x64.zip",
              "downloadUrl": "https://example/WindBoard-9999.0.0-win-x64.zip",
              "size": 1,
              "sha256": ""
            }
          ]
        }
        """;

        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(latestJson, Encoding.UTF8, "application/json")
        });

        var client = new HttpClient(handler);
        var svc = new UpdateService(client, new NullNotificationService())
        {
            LatestJsonUrl = "https://example/latest.json"
        };

        UpdateCheckResult result = await svc.CheckForUpdatesAsync(skippedVersion: null, ct: CancellationToken.None);

        Assert.NotNull(result.LatestVersion);
        Assert.True(result.UpdateAvailable);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_SkippedVersion_SuppressesUpdateAvailable()
    {
        const string latestJson = """
        {
          "version": "9999.0.0",
          "versionName": "v9999.0.0",
          "releaseDate": "2024-01-15T10:30:00Z",
          "minSystemVersion": "10.0.0.0",
          "changelog": { "en-US": "Test" },
          "assets": []
        }
        """;

        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(latestJson, Encoding.UTF8, "application/json")
        });

        var client = new HttpClient(handler);
        var svc = new UpdateService(client, new NullNotificationService())
        {
            LatestJsonUrl = "https://example/latest.json"
        };

        UpdateCheckResult result = await svc.CheckForUpdatesAsync(skippedVersion: "9999.0.0", ct: CancellationToken.None);

        Assert.NotNull(result.LatestVersion);
        Assert.False(result.UpdateAvailable);
        Assert.Null(result.ErrorMessage);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly System.Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public StubHttpMessageHandler(System.Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }

    private sealed class NullNotificationService : INotificationService
    {
        public void ShowUpdateAvailable(UpdateInfo updateInfo)
        {
        }
    }
}
