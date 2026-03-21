using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using WindBoard.Localization;
using WindBoard.Logging;
using WindBoard.Reminders;
using WindBoard.Settings;

namespace WindBoard.Updates
{
    /// <summary>
    /// 更新检查服务：
    /// - 拉取 GitHub Release 的 latest.json
    /// - 对比当前版本与最新版本
    /// - 选择推荐下载项（含安装包 -fd 区分）
    /// - 自动检查时结合设置做节流与“提醒一次”
    /// </summary>
    internal sealed class AppUpdateService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
        };

        private static readonly HttpClient UpdateHttpClient = CreateHttpClient();

        private readonly SemaphoreSlim _checkGate = new(1, 1);

        internal static AppUpdateService Instance { get; } = new();

        private AppUpdateService()
        {
        }

        internal async Task TryEnsureInstallerDownloadSourceSelectedAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                AppInstallProbeResult install = AppInstallProbe.Probe();
                if (install.Kind != AppInstallKind.Installer)
                {
                    return;
                }

                DownloadSourcePreferencesSnapshot prefs = AppSettingsService.Instance.GetUpdateDownloadSourcePreferencesSnapshot();
                if (prefs.Policy != DownloadSourcePolicy.Auto)
                {
                    return;
                }

                if (prefs.LastTestUtc is not null)
                {
                    return;
                }

                _ = await ResolvePreferredDownloadSourceIdAsync(UpdateCheckMode.Auto, install, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // 选源失败不应影响主流程：后续仍可使用默认 Github 源或下载失败轮询兜底。
                AppLog.Warn("Updates", "安装版初始化下载源失败", ex);
            }
        }

        internal async Task<DownloadSourceId> SpeedTestAndPersistBestDownloadSourceAsync(CancellationToken cancellationToken = default)
        {
            DownloadSourceSpeedTestResult[] results = await DownloadSourceSpeedTester
                .TestAsync(UpdateConstants.LatestJsonUrl, cancellationToken)
                .ConfigureAwait(false);

            if (!DownloadSourceSpeedTester.TryPickFastest(results, out DownloadSourceId fastest))
            {
                fastest = DownloadSourceId.Github;
            }

            DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
            AppSettingsService.Instance.SetUpdateDownloadSourceId(fastest);
            AppSettingsService.Instance.SetUpdateDownloadSourceLastTestUtc(nowUtc);

            AppLog.Info("Updates", $"下载源测速完成：fastest={fastest}, testedAtUtc={nowUtc:O}");
            return fastest;
        }

        internal async Task<AppUpdateCheckResult> CheckForUpdatesAsync(
            UpdateCheckMode mode,
            CancellationToken cancellationToken = default)
        {
            Stopwatch sw = Stopwatch.StartNew();
            string currentVersion = AppInfo.Version;

            await _checkGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                AppInstallProbeResult install = AppInstallProbe.Probe();
                string arch = GetCurrentArch();

                DownloadSourceId preferredSource = await ResolvePreferredDownloadSourceIdAsync(mode, install, cancellationToken)
                    .ConfigureAwait(false);

                (LatestReleaseInfo latest, DownloadSourceId usedSource) latestResult;
                try
                {
                    latestResult = await FetchLatestWithFailoverAsync(preferredSource, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    AppLog.Warn("Updates", "获取 latest.json 失败", ex);
                    return new AppUpdateCheckResult
                    {
                        State = AppUpdateCheckState.Error,
                        CurrentVersion = currentVersion,
                        Message = BuildUserFriendlyErrorMessage(ex),
                        Duration = sw.Elapsed,
                        Error = ex,
                        EffectiveDownloadSourceId = preferredSource,
                    };
                }

                DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
                AppSettingsService.Instance.SetUpdateLastCheckUtc(nowUtc);

                LatestReleaseInfo latest = latestResult.latest;
                DownloadSourceId usedSource = latestResult.usedSource;
                UpdateAssetRecommendation assets = UpdateAssetSelector.Select(latest.Assets, arch, install);

                AppUpdateCheckState state = CompareVersions(currentVersion, latest.Version);
                if (state == AppUpdateCheckState.UpdateAvailable && mode == UpdateCheckMode.Manual)
                {
                    // 手动检查相当于“用户已知晓”：同步写入已提醒版本，避免随后自动检查重复提示。
                    AppSettingsService.Instance.SetUpdateLastNotifiedVersion(latest.Version);
                }

                AppLog.Info(
                    "Updates",
                    $"更新检查完成：mode={mode}, state={state}, current='{currentVersion}', latest='{latest.Version}', install={install.Kind}/{install.Variant}({install.Evidence}), arch={arch}, sourcePreferred={preferredSource}, sourceUsed={usedSource}, durationMs={(int)sw.Elapsed.TotalMilliseconds}");

                return new AppUpdateCheckResult
                {
                    State = state,
                    CurrentVersion = currentVersion,
                    Latest = latest,
                    Assets = assets,
                    Duration = sw.Elapsed,
                    EffectiveDownloadSourceId = usedSource,
                };
            }
            finally
            {
                _checkGate.Release();
            }
        }

        internal async Task TryAutoCheckAndRemindAsync(Window window, CancellationToken cancellationToken = default)
        {
            if (window is null)
            {
                throw new ArgumentNullException(nameof(window));
            }

            UpdatePreferencesSnapshot prefs = AppSettingsService.Instance.GetUpdatePreferencesSnapshot();
            DateTimeOffset nowUtc = DateTimeOffset.UtcNow;

            if (!UpdateCheckDueCalculator.IsDue(prefs.AutoCheckInterval, prefs.LastCheckUtc, nowUtc))
            {
                AppLog.Debug("Updates", $"自动检查未到期：interval={prefs.AutoCheckInterval}, lastCheckUtc={prefs.LastCheckUtc:O}");
                return;
            }

            AppUpdateCheckResult result = await CheckForUpdatesAsync(UpdateCheckMode.Auto, cancellationToken).ConfigureAwait(false);
            if (result.State != AppUpdateCheckState.UpdateAvailable)
            {
                return;
            }

            string latestVersion = result.Latest?.Version ?? string.Empty;
            if (string.IsNullOrWhiteSpace(latestVersion))
            {
                return;
            }

            // “提醒一次”去重：跨会话基于 settings.json；会话内基于 AppReminderService 的 signature。
            if (string.Equals(prefs.LastNotifiedVersion, latestVersion, StringComparison.OrdinalIgnoreCase))
            {
                AppLog.Debug("Updates", $"已提醒过该版本更新，将跳过：version='{latestVersion}'");
                return;
            }

            string title = L10n.Format("Reminder_Update_Available_Title_Fmt", AppInfo.DisplayVersion, "v" + latestVersion);
            string body = L10n.Get("Reminder_Update_Available_Body");

            string signature = $"UpdateAvailable:{latestVersion}";
            AppReminderService.Instance.RemindOncePerSignature(
                window,
                signature,
                new AppReminderMessage
                {
                    Title = title,
                    Body = body,
                    Severity = AppReminderSeverity.Info,
                });

            AppSettingsService.Instance.SetUpdateLastNotifiedVersion(latestVersion);
        }

        private static async Task<DownloadSourceId> ResolvePreferredDownloadSourceIdAsync(
            UpdateCheckMode mode,
            AppInstallProbeResult install,
            CancellationToken cancellationToken)
        {
            DownloadSourcePreferencesSnapshot prefs = AppSettingsService.Instance.GetUpdateDownloadSourcePreferencesSnapshot();
            DownloadSourceId id = prefs.SourceId;
            DateTimeOffset nowUtc = DateTimeOffset.UtcNow;

            bool shouldTest = DownloadSourceSpeedTestPolicy.ShouldSpeedTest(
                installKind: install.Kind,
                policy: prefs.Policy,
                lastTestUtc: prefs.LastTestUtc,
                mode: mode,
                nowUtc: nowUtc);

            if (!shouldTest)
            {
                return id;
            }

            DownloadSourceSpeedTestResult[] results = await DownloadSourceSpeedTester
                .TestAsync(UpdateConstants.LatestJsonUrl, cancellationToken)
                .ConfigureAwait(false);

            if (!DownloadSourceSpeedTester.TryPickFastest(results, out DownloadSourceId fastest))
            {
                fastest = DownloadSourceId.Github;
            }

            // 约定：auto 模式测速后写入最快源；fixed 模式不应进入测速分支（由策略保证）。
            AppSettingsService.Instance.SetUpdateDownloadSourceId(fastest);
            AppSettingsService.Instance.SetUpdateDownloadSourceLastTestUtc(nowUtc);

            AppLog.Info("Updates", $"下载源测速完成：fastest={fastest}, testedAtUtc={nowUtc:O}");
            return fastest;
        }

        private static async Task<(LatestReleaseInfo latest, DownloadSourceId usedSource)> FetchLatestWithFailoverAsync(
            DownloadSourceId preferredSource,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<DownloadSourceId> order = DownloadSourceUrlRewriter.BuildFailoverOrder(preferredSource);
            Exception? lastError = null;

            foreach (DownloadSourceId source in order)
            {
                string url = DownloadSourceUrlRewriter.Rewrite(UpdateConstants.LatestJsonUrl, source);
                try
                {
                    using HttpRequestMessage req = new(HttpMethod.Get, url);
                    using HttpResponseMessage resp = await UpdateHttpClient
                        .SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                        .ConfigureAwait(false);

                    if (!resp.IsSuccessStatusCode)
                    {
                        string status = $"{(int)resp.StatusCode} {resp.ReasonPhrase}";
                        throw new HttpRequestException($"HTTP 请求失败：{status}");
                    }

                    await using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                    LatestReleaseInfo? latest = await JsonSerializer
                        .DeserializeAsync<LatestReleaseInfo>(stream, JsonOptions, cancellationToken)
                        .ConfigureAwait(false);

                    if (latest is null)
                    {
                        throw new InvalidOperationException("latest.json 解析结果为空");
                    }

                    latest.Version = (latest.Version ?? string.Empty).Trim();
                    latest.VersionName = (latest.VersionName ?? string.Empty).Trim();
                    latest.ReleaseDate = (latest.ReleaseDate ?? string.Empty).Trim();

                    return (latest, source);
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    AppLog.Warn("Updates", $"获取 latest.json 失败，将切换下载源：source={source}", ex);
                }
            }

            throw lastError ?? new InvalidOperationException("获取 latest.json 失败（未知错误）");
        }

        private static string GetCurrentArch()
        {
            Architecture arch = RuntimeInformation.ProcessArchitecture;
            return arch switch
            {
                Architecture.X86 => "x86",
                Architecture.Arm64 => "arm64",
                _ => "x64",
            };
        }

        private static AppUpdateCheckState CompareVersions(string currentVersion, string latestVersion)
        {
            if (!SemanticVersion.TryParse(currentVersion, out SemanticVersion current))
            {
                AppLog.Warn("Updates", $"当前版本无法解析为 SemVer，将返回“不确定”：current='{currentVersion}'");
                return AppUpdateCheckState.Indeterminate;
            }

            if (!SemanticVersion.TryParse(latestVersion, out SemanticVersion latest))
            {
                AppLog.Warn("Updates", $"最新版本无法解析为 SemVer，将返回“不确定”：latest='{latestVersion}'");
                return AppUpdateCheckState.Indeterminate;
            }

            return current.CompareTo(latest) < 0
                ? AppUpdateCheckState.UpdateAvailable
                : AppUpdateCheckState.UpToDate;
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
            catch
            {
                // 忽略：User-Agent 设置失败不应阻断更新检查
            }

            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return client;
        }

        private static string BuildUserFriendlyErrorMessage(Exception ex)
        {
            // 面向用户的错误信息要简短可理解；详细堆栈已写入日志。
            return ex switch
            {
                TaskCanceledException => L10n.Get("Updates_CheckFailed_Timeout"),
                HttpRequestException => L10n.Get("Updates_CheckFailed_Network"),
                JsonException => L10n.Get("Updates_CheckFailed_Parse"),
                _ => L10n.Get("Updates_CheckFailed_Generic"),
            };
        }
    }

    internal enum UpdateCheckMode
    {
        Manual,
        Auto,
    }

    internal enum AppUpdateCheckState
    {
        UpToDate,
        UpdateAvailable,
        Indeterminate,
        Error,
    }

    internal sealed class AppUpdateCheckResult
    {
        internal AppUpdateCheckState State { get; init; } = AppUpdateCheckState.Error;

        internal string CurrentVersion { get; init; } = string.Empty;

        internal LatestReleaseInfo? Latest { get; init; }

        internal UpdateAssetRecommendation? Assets { get; init; }

        /// <summary>
        /// 本次检查/下载建议使用的下载源（用于把 GitHub 原链接改写为镜像链接）。
        /// </summary>
        internal DownloadSourceId EffectiveDownloadSourceId { get; init; } = DownloadSourceId.Github;

        internal string Message { get; init; } = string.Empty;

        internal TimeSpan Duration { get; init; }

        internal Exception? Error { get; init; }

        internal string GetReleasePageUrl()
        {
            string versionName = Latest?.VersionName ?? string.Empty;
            return UpdateConstants.GetReleaseTagPageUrl(versionName);
        }

        internal string? TryGetChangelog(string cultureName)
        {
            if (Latest?.Changelog is null || Latest.Changelog.Count == 0)
            {
                return null;
            }

            string culture = (cultureName ?? string.Empty).Trim();
            if (!string.IsNullOrEmpty(culture) && Latest.Changelog.TryGetValue(culture, out string? exact))
            {
                return exact;
            }

            // 约定：优先中文，其次英文。
            if (culture.StartsWith("zh", StringComparison.OrdinalIgnoreCase) && Latest.Changelog.TryGetValue("zh-CN", out string? zh))
            {
                return zh;
            }

            if (Latest.Changelog.TryGetValue("en-US", out string? en))
            {
                return en;
            }

            // 最后兜底：取任意一条。
            foreach (string? v in Latest.Changelog.Values)
            {
                if (!string.IsNullOrWhiteSpace(v))
                {
                    return v;
                }
            }

            return null;
        }

        internal string TryGetReleaseDateLocalText()
        {
            string releaseDate = Latest?.ReleaseDate ?? string.Empty;
            if (string.IsNullOrWhiteSpace(releaseDate))
            {
                return string.Empty;
            }

            if (DateTimeOffset.TryParse(releaseDate, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset utc))
            {
                DateTimeOffset local = utc.ToLocalTime();
                return local.ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);
            }

            return releaseDate;
        }
    }
}
