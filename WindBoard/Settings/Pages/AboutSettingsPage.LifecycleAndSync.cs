using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WindBoard.Localization;
using WindBoard.Logging;
using WindBoard.Updates;

namespace WindBoard.Settings.Pages
{
    public sealed partial class AboutSettingsPage
    {
        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            SyncUiFromSettings();
            AppSettingsService.Instance.Changed += OnAppSettingsChanged;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            AppSettingsService.Instance.Changed -= OnAppSettingsChanged;

            // 页面卸载时尽力取消后台任务，避免回调在页面销毁后仍试图更新 UI。
            CancelAndDispose(ref _checkUpdatesCts);
            CancelAndDispose(ref _downloadSourceTestCts);
        }

        private static void CancelAndDispose(ref CancellationTokenSource? cts)
        {
            if (cts is null)
            {
                return;
            }

            try
            {
                cts.Cancel();
            }
            catch
            {
                // 忽略取消异常：不影响页面卸载
            }
            finally
            {
                cts.Dispose();
                cts = null;
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
                if (AppNameTextBlock is not null)
                {
                    AppNameTextBlock.Text = global::WindBoard.AppDisplayName.Get();
                }

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
    }
}

