using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;
using WindBoard.Models.Update;

namespace WindBoard.Services
{
    public sealed class UpdateService
    {
        private static readonly Lazy<UpdateService> _lazy = new(() => new UpdateService(), isThreadSafe: true);
        public static UpdateService Instance => _lazy.Value;

        public const string DefaultLatestJsonUrl = "https://github.com/Jerry-Z07/WindBoard/releases/latest/download/latest.json";

        private readonly HttpClient _httpClient;

        public string LatestJsonUrl { get; set; }

        internal UpdateService(HttpClient httpClient)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            LatestJsonUrl = GetLatestJsonUrlFromEnvironment() ?? DefaultLatestJsonUrl;
        }

        private UpdateService()
            : this(BuildDefaultHttpClient())
        {
        }

        public async Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken ct = default)
        {
            string? skippedVersion = null;
            try { skippedVersion = SettingsService.Instance.GetSkippedUpdateVersion(); } catch { }
            return await CheckForUpdatesAsync(skippedVersion, ct).ConfigureAwait(false);
        }

        public Task<UpdateCheckResult> CheckForUpdatesAsync(bool ignoreSkippedVersion, CancellationToken ct = default)
        {
            if (ignoreSkippedVersion)
            {
                return CheckForUpdatesAsync(skippedVersion: null, ct);
            }

            return CheckForUpdatesAsync(ct);
        }

        internal async Task<UpdateCheckResult> CheckForUpdatesAsync(string? skippedVersion, CancellationToken ct = default)
        {
            var result = new UpdateCheckResult();

            System.Version? currentVersion = AppVersionInfo.ParsedVersion;
            if (currentVersion == null)
            {
                result.UpdateAvailable = false;
                result.ErrorMessage = "Current version is unavailable.";
                return result;
            }

            string url = LatestJsonUrl;
            if (string.IsNullOrWhiteSpace(url))
            {
                result.UpdateAvailable = false;
                result.ErrorMessage = "Latest.json URL is not configured.";
                return result;
            }

            string json;
            try
            {
                using HttpResponseMessage response = await _httpClient.GetAsync(url, ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                result.UpdateAvailable = false;
                result.ErrorMessage = ex.Message;
                return result;
            }

            UpdateInfo? updateInfo;
            try
            {
                updateInfo = Newtonsoft.Json.JsonConvert.DeserializeObject<UpdateInfo>(json);
            }
            catch (Exception ex)
            {
                result.UpdateAvailable = false;
                result.ErrorMessage = $"Invalid latest.json: {ex.Message}";
                return result;
            }

            if (updateInfo == null)
            {
                result.UpdateAvailable = false;
                result.ErrorMessage = "Invalid latest.json: empty payload.";
                return result;
            }

            System.Version? latestVersion = TryParseStableVersion(updateInfo.Version) ?? TryParseStableVersion(updateInfo.VersionName);
            if (latestVersion == null)
            {
                result.UpdateAvailable = false;
                result.ErrorMessage = "Invalid latest.json: version is not parseable.";
                return result;
            }

            if (!string.IsNullOrWhiteSpace(updateInfo.MinSystemVersion))
            {
                System.Version? minSystemVersion = TryParseStableVersion(updateInfo.MinSystemVersion);
                if (minSystemVersion != null)
                {
                    System.Version currentSystemVersion = Environment.OSVersion.Version;
                    if (currentSystemVersion < minSystemVersion)
                    {
                        result.UpdateAvailable = false;
                        result.ErrorMessage = $"Requires Windows {minSystemVersion} or later.";
                        return result;
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(skippedVersion))
            {
                System.Version? skippedParsed = TryParseStableVersion(skippedVersion);
                if (skippedParsed != null && skippedParsed == latestVersion)
                {
                    result.UpdateAvailable = false;
                    result.LatestVersion = updateInfo;
                    return result;
                }
            }

            result.LatestVersion = updateInfo;
            result.UpdateAvailable = latestVersion > currentVersion;
            return result;
        }

        public UpdateAsset? GetMatchingAsset(UpdateInfo updateInfo, bool preferSelfContained = true)
        {
            if (updateInfo == null) throw new ArgumentNullException(nameof(updateInfo));
            string arch = GetCurrentArchitecture();
            return GetMatchingAsset(updateInfo, arch, preferSelfContained);
        }

        internal static UpdateAsset? GetMatchingAsset(UpdateInfo updateInfo, string arch, bool preferSelfContained = true)
        {
            if (updateInfo == null) throw new ArgumentNullException(nameof(updateInfo));
            if (string.IsNullOrWhiteSpace(arch)) return null;

            List<UpdateAsset> assets = updateInfo.Assets ?? new List<UpdateAsset>();
            string normalizedArch = arch.Trim();

            IEnumerable<UpdateAsset> candidates = assets.Where(a =>
                a != null
                && string.Equals(a.Arch?.Trim(), normalizedArch, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(a.DownloadUrl));

            string preferredRuntime = preferSelfContained ? "self-contained" : "framework-dependent";
            string fallbackRuntime = preferSelfContained ? "framework-dependent" : "self-contained";

            UpdateAsset? preferred = candidates.FirstOrDefault(a =>
                string.Equals(a.Runtime?.Trim(), preferredRuntime, StringComparison.OrdinalIgnoreCase));

            if (preferred != null)
                return preferred;

            return candidates.FirstOrDefault(a =>
                string.Equals(a.Runtime?.Trim(), fallbackRuntime, StringComparison.OrdinalIgnoreCase))
                ?? candidates.FirstOrDefault();
        }

        public async Task<string> DownloadUpdateAsync(UpdateAsset asset, IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
        {
            if (asset == null) throw new ArgumentNullException(nameof(asset));
            if (string.IsNullOrWhiteSpace(asset.DownloadUrl)) throw new ArgumentException("Asset download URL is empty.", nameof(asset));

            string fileName = string.IsNullOrWhiteSpace(asset.FileName)
                ? "WindBoard-Update.zip"
                : asset.FileName;

            string destinationDirectory = Path.Combine(Path.GetTempPath(), "WindBoard", "Updates");
            Directory.CreateDirectory(destinationDirectory);

            string destinationPath = Path.Combine(destinationDirectory, fileName);

            try
            {
                using HttpResponseMessage response = await _httpClient.GetAsync(asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                long total = response.Content.Headers.ContentLength ?? asset.Size;
                long received = 0;

                using Stream contentStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 81920, useAsync: true);
                using IncrementalHash hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

                var buffer = new byte[81920];
                while (true)
                {
                    int read = await contentStream.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false);
                    if (read <= 0)
                        break;

                    await fileStream.WriteAsync(buffer, 0, read, ct).ConfigureAwait(false);
                    hasher.AppendData(buffer, 0, read);
                    received += read;

                    progress?.Report(new DownloadProgress
                    {
                        BytesReceived = received,
                        TotalBytes = total
                    });
                }

                await fileStream.FlushAsync(ct).ConfigureAwait(false);

                if (!string.IsNullOrWhiteSpace(asset.Sha256))
                {
                    string expected = NormalizeSha256Hex(asset.Sha256);
                    string actual = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
                    if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                    {
                        try { File.Delete(destinationPath); } catch { }
                        throw new InvalidDataException($"SHA256 mismatch. Expected: {expected}, Actual: {actual}");
                    }
                }

                return destinationPath;
            }
            catch
            {
                try
                {
                    if (File.Exists(destinationPath))
                        File.Delete(destinationPath);
                }
                catch
                {
                }

                throw;
            }
        }

        public void ShowUpdateNotification(UpdateInfo updateInfo)
        {
            if (updateInfo == null) throw new ArgumentNullException(nameof(updateInfo));

            try
            {
                string title = LocalizationService.Instance.GetString("Update_Toast_Title");
                string bodyTemplate = LocalizationService.Instance.GetString("Update_Toast_Body_Format");
                string body = string.Format(CultureInfo.CurrentUICulture, bodyTemplate, updateInfo.VersionName ?? updateInfo.Version);

                XmlDocument toastXml = ToastNotificationManager.GetTemplateContent(ToastTemplateType.ToastText02);
                XmlNodeList texts = toastXml.GetElementsByTagName("text");
                if (texts.Count > 0) texts[0].AppendChild(toastXml.CreateTextNode(title));
                if (texts.Count > 1) texts[1].AppendChild(toastXml.CreateTextNode(body));

                var toast = new ToastNotification(toastXml)
                {
                    Tag = "WindBoard.Update"
                };

                ToastNotificationManager.CreateToastNotifier("WindBoard").Show(toast);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Update] Failed to show toast notification: {ex}");
            }
        }

        public static string GetCurrentArchitecture()
        {
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "x64",
                Architecture.X86 => "x86",
                Architecture.Arm64 => "arm64",
                _ => "unknown"
            };
        }

        internal static System.Version? TryParseStableVersion(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            string trimmed = text.Trim();
            if (trimmed.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                trimmed = trimmed[1..];

            int separatorIndex = trimmed.IndexOfAny(new[] { '-', '+' });
            if (separatorIndex > 0)
                trimmed = trimmed[..separatorIndex];

            return System.Version.TryParse(trimmed, out System.Version? version) ? version : null;
        }

        private static HttpClient BuildDefaultHttpClient()
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("WindBoard-Updater/1.0 (+https://github.com/Jerry-Z07/WindBoard)");
            client.Timeout = TimeSpan.FromSeconds(30);
            return client;
        }

        private static string NormalizeSha256Hex(string sha256)
        {
            string trimmed = sha256.Trim();
            if (trimmed.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed["sha256:".Length..];
            }

            return trimmed.Trim().ToLowerInvariant();
        }

        private static string? GetLatestJsonUrlFromEnvironment()
        {
            try
            {
                string? value = Environment.GetEnvironmentVariable("WINDBOARD_UPDATE_LATEST_JSON_URL");
                return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            }
            catch
            {
                return null;
            }
        }
    }
}
