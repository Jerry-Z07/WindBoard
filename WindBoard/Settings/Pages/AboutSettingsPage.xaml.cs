using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WindBoard.Localization;
using WindBoard.Logging;

namespace WindBoard.Settings.Pages
{
    public sealed partial class AboutSettingsPage : Page
    {
        private bool _isSyncingUiFromSettings;
        private readonly MultiTapGestureDetector _debugUnlockTapDetector = new(requiredTaps: 5, maxInterval: TimeSpan.FromMilliseconds(800));
        private int _debugUnlockInfoNonce;

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

            AppLog.Info("Updates", $"自动检查更新频率变更（仅占位）：interval='{UpdateCheckIntervalParser.ToSettingValue(interval)}'");
            AppSettingsService.Instance.SetUpdateCheckInterval(interval);
        }

        private async void OnCheckUpdatesClicked(object sender, RoutedEventArgs e)
        {
            AppLog.Info("Updates", "用户点击检查更新（占位）");

            try
            {
                if (XamlRoot is null)
                {
                    return;
                }

                var dialog = new ContentDialog
                {
                    Title = L10n.Get("MainWindow_FeatureWip_Title"),
                    Content = L10n.Format("MainWindow_FeatureWip_Content_Fmt", L10n.Get("Settings_About_CheckUpdates")),
                    CloseButtonText = L10n.Get("Common_Close"),
                    XamlRoot = XamlRoot,
                };

                await dialog.ShowAsync();
            }
            catch (Exception ex)
            {
                AppLog.Warn("Updates", "显示“检查更新”占位弹窗失败", ex);
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
