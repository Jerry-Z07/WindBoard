using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WindBoard.Logging;

namespace WindBoard.Settings.Pages
{
    public sealed partial class GeneralSettingsPage : Page
    {
        private bool _isSyncingUiFromSettings;

        public GeneralSettingsPage()
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
                AppLanguagePreference preference = AppSettingsService.Instance.GetLanguagePreference();
                string settingValue = AppLanguagePreferenceParser.ToSettingValue(preference);

                _isSyncingUiFromSettings = true;
                LanguageComboBox.SelectedValue = settingValue;
            }
            catch (Exception ex)
            {
                // 同步失败不应影响设置页：记录日志便于排查。
                AppLog.Warn("L10n", "同步语言设置 UI 失败", ex);
            }
            finally
            {
                _isSyncingUiFromSettings = false;
            }
        }

        private void OnLanguageSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSyncingUiFromSettings)
            {
                return;
            }

            try
            {
                string? value = LanguageComboBox.SelectedValue as string;
                if (string.IsNullOrWhiteSpace(value) && LanguageComboBox.SelectedItem is ComboBoxItem item)
                {
                    value = item.Tag as string;
                }

                if (!AppLanguagePreferenceParser.TryParse(value, out AppLanguagePreference preference))
                {
                    preference = AppLanguagePreference.System;
                }

                string settingValue = AppLanguagePreferenceParser.ToSettingValue(preference);
                AppLog.Info("L10n", $"用户切换语言偏好：value='{settingValue}'");

                AppSettingsService.Instance.SetLanguagePreference(preference);
                AppLanguageService.Apply(preference);

                // 运行中切换语言通常需要重启才能完全生效（特别是已加载的 XAML 文本）。
                LanguageRestartInfoBar.IsOpen = true;
            }
            catch (Exception ex)
            {
                // 切换失败不应导致设置页崩溃：记录日志并回退 UI。
                AppLog.Warn("L10n", "切换语言偏好失败", ex);
                SyncUiFromSettings();
            }
        }

        private void OnCamouflageSettingsClicked(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(CamouflageSettingsPage));
        }
    }
}

