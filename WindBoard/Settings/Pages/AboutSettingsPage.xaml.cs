using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using WindBoard.Localization;
using WindBoard.Logging;
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

                _isSyncingUiFromSettings = true;
                AutoCheckUpdatesComboBox.SelectedValue = settingValue;
            }
            finally
            {
                _isSyncingUiFromSettings = false;
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

                    string url = pick.Asset.DownloadUrl ?? string.Empty;
                    link.Click += (_, _) => _ = TryLaunchUrlAsync(url);
                    panel.Children.Add(link);
                }
            }

            string releasePageUrl = result.GetReleasePageUrl();
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
