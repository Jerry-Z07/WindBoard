using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WindBoard.Localization;
using WindBoard.Logging;

namespace WindBoard.Settings.Pages
{
    public sealed partial class AboutSettingsPage : Page
    {
        private bool _isSyncingUiFromSettings;

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
    }
}
