using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using WindBoard.Localization;
using WindBoard.Logging;
using WindBoard.Persistence;
using WindBoard.Updates;

namespace WindBoard.Settings.Pages
{
    public sealed partial class AboutSettingsPage : Page
    {
        private bool _isSyncingUiFromSettings;
        private readonly MultiTapGestureDetector _debugUnlockTapDetector = new(requiredTaps: 5, maxInterval: TimeSpan.FromMilliseconds(800));
        private int _debugUnlockInfoNonce;
        private bool _isCheckingUpdates;
        private CancellationTokenSource? _checkUpdatesCts;
        private bool _isTestingDownloadSource;
        private CancellationTokenSource? _downloadSourceTestCts;

        public AboutSettingsPage()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            SyncUiFromSettings();
            AppSettingsService.Instance.Changed += OnAppSettingsChanged;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            AppSettingsService.Instance.Changed -= OnAppSettingsChanged;

            try
            {
                _checkUpdatesCts?.Cancel();
            }
            catch
            {
                // 忽略取消异常：不影响页面卸载
            }
            finally
            {
                _checkUpdatesCts?.Dispose();
                _checkUpdatesCts = null;
            }

            try
            {
                _downloadSourceTestCts?.Cancel();
            }
            catch
            {
                // 忽略取消异常：不影响页面卸载
            }
            finally
            {
                _downloadSourceTestCts?.Dispose();
                _downloadSourceTestCts = null;
            }
        }

        private void OnAppSettingsChanged(object? sender, EventArgs e)
        {
            // 设置变更可能来自非 UI 线程，这里统一切回 UI 线程刷新。
            if (!DispatcherQueue.TryEnqueue(SyncUiFromSettings))
            {
                SyncUiFromSettings();
            }
        }

        private void SyncUiFromSettings()
        {
            try
            {
                VersionTextBlock.Text = AppInfo.DisplayVersion;

                UpdateCheckInterval interval = AppSettingsService.Instance.GetUpdateCheckInterval();
                string settingValue = UpdateCheckIntervalParser.ToSettingValue(interval);

                DownloadSourcePreferencesSnapshot downloadSource = AppSettingsService.Instance.GetUpdateDownloadSourcePreferencesSnapshot();

                _isSyncingUiFromSettings = true;
                AutoCheckUpdatesComboBox.SelectedValue = settingValue;

                SyncDownloadSourceUi(downloadSource);
            }
            finally
            {
                _isSyncingUiFromSettings = false;
            }
        }

        private void SyncDownloadSourceUi(DownloadSourcePreferencesSnapshot snapshot)
        {
            try
            {
                if (DownloadSourceComboBox is null)
                {
                    return;
                }

                string selected = snapshot.Policy == DownloadSourcePolicy.Auto
                    ? "auto"
                    : DownloadSourceIdParser.ToSettingValue(snapshot.SourceId);

                bool restoreSyncing = _isSyncingUiFromSettings;
                _isSyncingUiFromSettings = true;
                try
                {
                    DownloadSourceComboBox.SelectedValue = selected;
                }
                finally
                {
                    _isSyncingUiFromSettings = restoreSyncing;
                }

                if (DownloadSourceStatusTextBlock is null)
                {
                    return;
                }

                if (_isTestingDownloadSource)
                {
                    DownloadSourceStatusTextBlock.Text = L10n.Get("Updates_DownloadSource_Status_Testing");
                    return;
                }

                string currentName = GetDownloadSourceDisplayName(snapshot.SourceId);
                string status = L10n.Format("Updates_DownloadSource_Status_Current_Fmt", currentName);

                if (snapshot.LastTestUtc is not null)
                {
                    string time = snapshot.LastTestUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);
                    status += " · " + L10n.Format("Updates_DownloadSource_Status_LastTest_Fmt", time);
                }

                DownloadSourceStatusTextBlock.Text = status;
            }
            catch (Exception ex)
            {
                // 更新失败不应影响设置页；记录日志便于排查。
                AppLog.Warn("Updates", "同步下载源 UI 失败", ex);
            }
        }

        private void OnAutoCheckUpdatesSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSyncingUiFromSettings)
            {
                return;
            }

            string? value = AutoCheckUpdatesComboBox.SelectedValue as string;
            if (string.IsNullOrWhiteSpace(value) && AutoCheckUpdatesComboBox.SelectedItem is ComboBoxItem item)
            {
                value = item.Tag as string;
            }

            if (!UpdateCheckIntervalParser.TryParse(value, out UpdateCheckInterval interval))
            {
                interval = UpdateCheckInterval.Weekly;
            }

            AppLog.Info("Updates", $"自动检查更新频率变更：interval='{UpdateCheckIntervalParser.ToSettingValue(interval)}'");
            AppSettingsService.Instance.SetUpdateCheckInterval(interval);
        }

        private async void OnDownloadSourceSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSyncingUiFromSettings)
            {
                return;
            }

            string? value = DownloadSourceComboBox.SelectedValue as string;
            if (string.IsNullOrWhiteSpace(value) && DownloadSourceComboBox.SelectedItem is ComboBoxItem item)
            {
                value = item.Tag as string;
            }

            string v = (value ?? string.Empty).Trim();
            if (v.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                AppLog.Info("Updates", "用户选择下载源：auto");
                AppSettingsService.Instance.SetUpdateDownloadSourcePolicy(DownloadSourcePolicy.Auto);

                // 自动模式：后台测速并写入最快源。
                await StartDownloadSourceSpeedTestAsync().ConfigureAwait(true);
                return;
            }

            if (!DownloadSourceIdParser.TryParse(v, out DownloadSourceId id))
            {
                id = DownloadSourceId.Github;
            }

            AppLog.Info("Updates", $"用户选择下载源：fixed/{id}");
            AppSettingsService.Instance.SetUpdateDownloadSourcePolicy(DownloadSourcePolicy.Fixed);
            AppSettingsService.Instance.SetUpdateDownloadSourceId(id);
        }

        private async Task StartDownloadSourceSpeedTestAsync()
        {
            if (_isTestingDownloadSource)
            {
                return;
            }

            _isTestingDownloadSource = true;
            SyncDownloadSourceUi(AppSettingsService.Instance.GetUpdateDownloadSourcePreferencesSnapshot());

            _downloadSourceTestCts?.Dispose();
            _downloadSourceTestCts = new CancellationTokenSource();

            try
            {
                _ = await AppUpdateService.Instance
                    .SpeedTestAndPersistBestDownloadSourceAsync(_downloadSourceTestCts.Token)
                    .ConfigureAwait(true);
            }
            catch (TaskCanceledException)
            {
                AppLog.Debug("Updates", "下载源测速已取消");
            }
            catch (Exception ex)
            {
                AppLog.Warn("Updates", "下载源测速失败", ex);
            }
            finally
            {
                _isTestingDownloadSource = false;
                SyncDownloadSourceUi(AppSettingsService.Instance.GetUpdateDownloadSourcePreferencesSnapshot());
            }
        }

        private async void OnCheckUpdatesClicked(object sender, RoutedEventArgs e)
        {
            if (_isCheckingUpdates)
            {
                return;
            }

            _isCheckingUpdates = true;
            SetCheckUpdatesUiState(isChecking: true);

            _checkUpdatesCts?.Dispose();
            _checkUpdatesCts = new CancellationTokenSource();

            try
            {
                if (XamlRoot is null)
                {
                    return;
                }

                AppLog.Info("Updates", "用户点击检查更新");
                AppUpdateCheckResult result = await AppUpdateService.Instance
                    .CheckForUpdatesAsync(UpdateCheckMode.Manual, _checkUpdatesCts.Token)
                    .ConfigureAwait(true);

                await ShowUpdateResultDialogAsync(result).ConfigureAwait(true);
            }
            catch (TaskCanceledException)
            {
                // 用户关闭页面/窗口导致取消：不提示、不打扰。
                AppLog.Debug("Updates", "检查更新已取消");
            }
            catch (Exception ex)
            {
                AppLog.Warn("Updates", "检查更新失败", ex);

                try
                {
                    await ShowUpdateResultDialogAsync(new AppUpdateCheckResult
                    {
                        State = AppUpdateCheckState.Error,
                        CurrentVersion = AppInfo.Version,
                        Message = L10n.Get("Updates_CheckFailed_Generic"),
                        Error = ex,
                    }).ConfigureAwait(true);
                }
                catch (Exception dialogEx)
                {
                    AppLog.Warn("Updates", "展示更新检查失败弹窗失败", dialogEx);
                }
            }
            finally
            {
                _isCheckingUpdates = false;
                SetCheckUpdatesUiState(isChecking: false);
            }
        }

        private void SetCheckUpdatesUiState(bool isChecking)
        {
            try
            {
                if (CheckUpdatesButton is not null)
                {
                    CheckUpdatesButton.IsEnabled = !isChecking;
                }

                if (CheckUpdatesProgressRing is not null)
                {
                    CheckUpdatesProgressRing.IsActive = isChecking;
                    CheckUpdatesProgressRing.Visibility = isChecking ? Visibility.Visible : Visibility.Collapsed;
                }

                if (CheckUpdatesChevronIcon is not null)
                {
                    CheckUpdatesChevronIcon.Visibility = isChecking ? Visibility.Collapsed : Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                // 更新检查 UI 状态更新失败不应影响主流程；记录日志便于排查。
                AppLog.Warn("Updates", "更新“检查更新”按钮状态失败", ex);
            }
        }

        private async Task ShowUpdateResultDialogAsync(AppUpdateCheckResult result)
        {
            if (XamlRoot is null)
            {
                return;
            }

            DownloadSourceId sourceForUrls = result.EffectiveDownloadSourceId;
            string releasePageUrl = result.GetReleasePageUrl();

            var panel = new StackPanel
            {
                Spacing = 10,
            };

            panel.Children.Add(new TextBlock
            {
                Text = $"{L10n.Get("Updates_CurrentVersion")}：{AppInfo.DisplayVersion}",
                Opacity = 0.8,
                TextWrapping = TextWrapping.WrapWholeWords,
            });

            string latestVersion = result.Latest?.Version ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(latestVersion))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = $"{L10n.Get("Updates_LatestVersion")}：v{latestVersion}",
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    TextWrapping = TextWrapping.WrapWholeWords,
                });
            }

            string dateText = result.TryGetReleaseDateLocalText();
            if (!string.IsNullOrWhiteSpace(dateText))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = $"{L10n.Get("Updates_ReleaseDate")}：{dateText}",
                    Opacity = 0.8,
                    TextWrapping = TextWrapping.WrapWholeWords,
                });
            }

            if (!string.IsNullOrWhiteSpace(result.Message))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = result.Message,
                    TextWrapping = TextWrapping.WrapWholeWords,
                });
            }

            string? changelog = result.TryGetChangelog(CultureInfo.CurrentUICulture.Name);
            if (!string.IsNullOrWhiteSpace(changelog))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = L10n.Get("Updates_Changelog_Title"),
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Margin = new Thickness(0, 6, 0, 0),
                });

                panel.Children.Add(new ScrollViewer
                {
                    MaxHeight = 260,
                    Content = new TextBlock
                    {
                        Text = changelog,
                        TextWrapping = TextWrapping.Wrap,
                        Opacity = 0.95,
                    },
                });
            }

            bool showDownloads = result.State is AppUpdateCheckState.UpdateAvailable or AppUpdateCheckState.Indeterminate;
            if (showDownloads && result.Assets is not null && result.Assets.Alternatives.Count > 0)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = L10n.Get("Updates_Download_Title"),
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Margin = new Thickness(0, 6, 0, 0),
                });

                foreach (UpdateAssetPick pick in result.Assets.Alternatives)
                {
                    string label = GetAssetLabel(pick.Kind);
                    bool isRecommended = result.Assets.Recommended is not null && ReferenceEquals(result.Assets.Recommended.Asset, pick.Asset);
                    string content = isRecommended
                        ? L10n.Format("Updates_Download_Item_Recommended_Fmt", label)
                        : label;

                    var link = new HyperlinkButton
                    {
                        Content = content,
                        HorizontalAlignment = HorizontalAlignment.Left,
                        Padding = new Thickness(0),
                    };

                    string originalUrl = pick.Asset.DownloadUrl ?? string.Empty;
                    string rewrittenUrl = DownloadSourceUrlRewriter.Rewrite(originalUrl, sourceForUrls);
                    link.Click += (_, _) => _ = TryLaunchUrlAsync(rewrittenUrl);
                    panel.Children.Add(link);
                }

                if (result.State == AppUpdateCheckState.UpdateAvailable && result.Assets.Recommended is not null)
                {
                    var downloadButton = new Button
                    {
                        Content = L10n.Get("Updates_DownloadButton"),
                        HorizontalAlignment = HorizontalAlignment.Left,
                        Margin = new Thickness(0, 6, 0, 0),
                    };

                    UpdateAssetPick recommended = result.Assets.Recommended;
                    downloadButton.Click += async (_, _) =>
                    {
                        try
                        {
                            await DownloadAssetWithProgressAsync(recommended, sourceForUrls, releasePageUrl).ConfigureAwait(true);
                        }
                        catch (Exception ex)
                        {
                            AppLog.Warn("Updates", "启动下载失败", ex);
                        }
                    };

                    panel.Children.Add(downloadButton);
                }
            }

            var releaseLink = new HyperlinkButton
            {
                Content = L10n.Get("Updates_OpenReleasePage"),
                HorizontalAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(0),
                Margin = new Thickness(0, 8, 0, 0),
            };
            releaseLink.Click += (_, _) => _ = TryLaunchUrlAsync(releasePageUrl);
            panel.Children.Add(releaseLink);

            string title = result.State switch
            {
                AppUpdateCheckState.UpToDate => L10n.Get("Updates_CheckResult_UpToDate_Title"),
                AppUpdateCheckState.UpdateAvailable => L10n.Get("Updates_CheckResult_UpdateAvailable_Title"),
                AppUpdateCheckState.Indeterminate => L10n.Get("Updates_CheckResult_Indeterminate_Title"),
                _ => L10n.Get("Updates_CheckResult_Error_Title"),
            };

            var dialog = new ContentDialog
            {
                Title = title,
                Content = panel,
                CloseButtonText = L10n.Get("Common_Close"),
                XamlRoot = XamlRoot,
            };

            await dialog.ShowAsync();
        }

        private static string GetAssetLabel(UpdateAssetKind kind)
        {
            return kind switch
            {
                UpdateAssetKind.InstallerFrameworkDependent => L10n.Get("Updates_Download_FrameworkDependentInstaller"),
                UpdateAssetKind.InstallerSelfContained => L10n.Get("Updates_Download_SelfContainedInstaller"),
                UpdateAssetKind.PortableZip => L10n.Get("Updates_Download_PortableZip"),
                _ => L10n.Get("Updates_Download_Unknown"),
            };
        }

        private static async Task TryLaunchUrlAsync(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            try
            {
                bool launched = await Launcher.LaunchUriAsync(new Uri(url));
                if (!launched)
                {
                    AppLog.Warn("Updates", $"打开链接失败（LaunchUriAsync 返回 false）：url='{url}'");
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn("Updates", $"打开链接失败：url='{url}'", ex);
            }
        }

        private async Task DownloadAssetWithProgressAsync(UpdateAssetPick pick, DownloadSourceId preferredSource, string releasePageUrl)
        {
            if (XamlRoot is null)
            {
                return;
            }

            string fileName = (pick.Asset.FileName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return;
            }

            string originalUrl = (pick.Asset.DownloadUrl ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(originalUrl))
            {
                return;
            }

            // 更新下载目录也属于应用数据：便携版需落到 {AppBase}\data 下。
            string downloadsDir = AppDataPaths.DownloadsDirectory;
            if (string.IsNullOrWhiteSpace(downloadsDir))
            {
                // 极端兜底：避免空路径导致写入到意外位置。
                downloadsDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "WindBoard",
                    "downloads");
            }
            string destinationPath = Path.Combine(downloadsDir, fileName);

            var cts = new CancellationTokenSource();

            var progressBar = new ProgressBar
            {
                Minimum = 0,
                Maximum = 100,
                IsIndeterminate = true,
            };

            var progressText = new TextBlock
            {
                Opacity = 0.8,
                TextWrapping = TextWrapping.WrapWholeWords,
            };

            var sourceText = new TextBlock
            {
                Opacity = 0.8,
                TextWrapping = TextWrapping.WrapWholeWords,
            };

            var dialogPanel = new StackPanel
            {
                Spacing = 10,
            };
            dialogPanel.Children.Add(new TextBlock { Text = fileName, TextWrapping = TextWrapping.WrapWholeWords });
            dialogPanel.Children.Add(progressBar);
            dialogPanel.Children.Add(sourceText);
            dialogPanel.Children.Add(progressText);

            var dialog = new ContentDialog
            {
                Title = L10n.Get("Updates_DownloadDialog_Title"),
                Content = dialogPanel,
                CloseButtonText = L10n.Get("Common_Cancel"),
                XamlRoot = XamlRoot,
            };

            dialog.Closed += (_, _) =>
            {
                try
                {
                    cts.Cancel();
                }
                catch
                {
                    // 忽略取消异常
                }
            };

            var progress = new Progress<DownloadProgress>(p =>
            {
                try
                {
                    string sourceName = GetDownloadSourceDisplayName(p.SourceId);
                    sourceText.Text = L10n.Format("Updates_DownloadDialog_Source_Fmt", sourceName);

                    if (p.TotalBytes is not null && p.TotalBytes.Value > 0)
                    {
                        progressBar.IsIndeterminate = false;
                        double percent = Math.Clamp(p.DownloadedBytes * 100.0 / p.TotalBytes.Value, 0, 100);
                        progressBar.Value = percent;
                        progressText.Text = L10n.Format("Updates_DownloadDialog_Progress_Fmt", FormatBytes(p.DownloadedBytes), FormatBytes(p.TotalBytes.Value));
                    }
                    else
                    {
                        progressBar.IsIndeterminate = true;
                        progressText.Text = L10n.Format("Updates_DownloadDialog_ProgressUnknown_Fmt", FormatBytes(p.DownloadedBytes));
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Warn("Downloads", "更新下载进度 UI 失败", ex);
                }
            });

            DownloadRequest request = new()
            {
                OriginalUrl = originalUrl,
                DestinationPath = destinationPath,
                PreferredSourceId = preferredSource,
                FailoverOrder = DownloadSourceUrlRewriter.BuildFailoverOrder(preferredSource),
                MaxCycles = 3,
            };

            Task<DownloadResult> downloadTask = BackgroundDownloadService.DownloadWithFailoverAsync(request, progress, cts.Token);
            Task showTask = dialog.ShowAsync().AsTask();

            Task finished = await Task.WhenAny(downloadTask, showTask).ConfigureAwait(true);
            if (finished == showTask)
            {
                // 用户主动关闭：取消下载并返回。
                try
                {
                    cts.Cancel();
                }
                catch
                {
                    // 忽略
                }

                try
                {
                    await downloadTask.ConfigureAwait(true);
                }
                catch
                {
                    // 忽略下载异常：已取消
                }

                return;
            }

            DownloadResult result = await downloadTask.ConfigureAwait(true);

            try
            {
                dialog.Hide();
                await showTask.ConfigureAwait(true);
            }
            catch
            {
                // 忽略：对话框关闭失败不影响后续提示
            }

            if (result.Success)
            {
                await ShowDownloadCompletedDialogAsync(result.FilePath, pick.Kind).ConfigureAwait(true);
                return;
            }

            string message = L10n.Get("Updates_DownloadDialog_Failed_Message");
            await ShowDownloadFailedDialogAsync(message, releasePageUrl).ConfigureAwait(true);
        }

        private async Task ShowDownloadCompletedDialogAsync(string filePath, UpdateAssetKind kind)
        {
            if (XamlRoot is null)
            {
                return;
            }

            string title = L10n.Get("Updates_DownloadDialog_Completed_Title");
            string content = L10n.Format("Updates_DownloadDialog_Completed_Path_Fmt", filePath);

            string primaryText = kind == UpdateAssetKind.PortableZip
                ? L10n.Get("Updates_DownloadDialog_OpenFolder")
                : L10n.Get("Updates_DownloadDialog_RunInstaller");

            var dialog = new ContentDialog
            {
                Title = title,
                Content = content,
                PrimaryButtonText = primaryText,
                SecondaryButtonText = L10n.Get("Updates_DownloadDialog_OpenFolder"),
                CloseButtonText = L10n.Get("Common_Close"),
                XamlRoot = XamlRoot,
            };

            ContentDialogResult result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary)
            {
                if (kind == UpdateAssetKind.PortableZip)
                {
                    TryOpenInExplorer(filePath, selectFile: true);
                }
                else
                {
                    TryRunInstaller(filePath);
                }

                return;
            }

            if (result == ContentDialogResult.Secondary)
            {
                TryOpenInExplorer(filePath, selectFile: true);
            }
        }

        private async Task ShowDownloadFailedDialogAsync(string message, string releasePageUrl)
        {
            if (XamlRoot is null)
            {
                return;
            }

            var dialog = new ContentDialog
            {
                Title = L10n.Get("Updates_DownloadDialog_Failed_Title"),
                Content = message,
                PrimaryButtonText = L10n.Get("Updates_OpenReleasePage"),
                CloseButtonText = L10n.Get("Common_Close"),
                XamlRoot = XamlRoot,
            };

            ContentDialogResult result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                await TryLaunchUrlAsync(releasePageUrl).ConfigureAwait(true);
            }
        }

        private static void TryRunInstaller(string filePath)
        {
            try
            {
                Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                AppLog.Warn("Downloads", $"运行安装程序失败：path='{filePath}'", ex);
            }
        }

        private static void TryOpenInExplorer(string path, bool selectFile)
        {
            try
            {
                string argument = selectFile
                    ? $"/select,\"{path}\""
                    : $"\"{path}\"";

                Process.Start(new ProcessStartInfo("explorer.exe", argument) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                AppLog.Warn("Downloads", $"打开资源管理器失败：path='{path}'", ex);
            }
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 0)
            {
                return "0 B";
            }

            const double KB = 1024;
            const double MB = 1024 * 1024;
            const double GB = 1024 * 1024 * 1024;

            if (bytes >= GB)
            {
                return (bytes / GB).ToString("0.00", CultureInfo.InvariantCulture) + " GB";
            }

            if (bytes >= MB)
            {
                return (bytes / MB).ToString("0.00", CultureInfo.InvariantCulture) + " MB";
            }

            if (bytes >= KB)
            {
                return (bytes / KB).ToString("0.00", CultureInfo.InvariantCulture) + " KB";
            }

            return bytes.ToString(CultureInfo.InvariantCulture) + " B";
        }

        private static string GetDownloadSourceDisplayName(DownloadSourceId id)
        {
            return id switch
            {
                DownloadSourceId.GhProxy => L10n.Get("Updates_DownloadSource_GhProxy"),
                DownloadSourceId.Felicity => L10n.Get("Updates_DownloadSource_Felicity"),
                DownloadSourceId.ZeroSeven => L10n.Get("Updates_DownloadSource_07"),
                _ => L10n.Get("Updates_DownloadSource_Github"),
            };
        }

        private void OnAppIconTapped(object sender, TappedRoutedEventArgs e)
        {
            try
            {
#if DEBUG
                // Debug 构建下调试入口默认显示，无需解锁。
                return;
#else
                if (DebugToolsGate.IsVisible)
                {
                    return;
                }

                if (_debugUnlockTapDetector.RegisterTap(DateTimeOffset.UtcNow))
                {
                    DebugToolsGate.UnlockForSession();
                    ShowDebugUnlockInfo();
                }
#endif
            }
            catch (Exception ex)
            {
                // 防御：隐藏入口的手势检测不应影响 About 页正常使用。
                AppLog.Warn("Debug", "关于页调试入口解锁点击处理失败", ex);
            }
        }

        private void ShowDebugUnlockInfo()
        {
            if (DebugUnlockInfoBar is null)
            {
                return;
            }

            DebugUnlockInfoBar.IsOpen = true;

            // 轻提示：自动关闭，避免占用页面空间。
            int nonce = ++_debugUnlockInfoNonce;
            _ = AutoDismissDebugUnlockInfoBarAsync(nonce);
        }

        private async Task AutoDismissDebugUnlockInfoBarAsync(int nonce)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(4)).ConfigureAwait(false);

                _ = DispatcherQueue.TryEnqueue(() =>
                {
                    // 如果期间又触发过提示（nonce 变化），则不关闭最新提示。
                    if (nonce != _debugUnlockInfoNonce)
                    {
                        return;
                    }

                    try
                    {
                        if (DebugUnlockInfoBar is not null)
                        {
                            DebugUnlockInfoBar.IsOpen = false;
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLog.Warn("Debug", "自动关闭调试解锁提示失败", ex);
                    }
                });
            }
            catch (Exception ex)
            {
                AppLog.Warn("Debug", "调试解锁提示延迟任务失败", ex);
            }
        }
    }
}
