using System;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WindBoard.Features.Camouflage.UI;
using WindBoard.Localization;
using WindBoard.Logging;

namespace WindBoard.Settings.Pages
{
    public sealed partial class GeneralSettingsPage : Page
    {
        private bool _isSyncingUiFromSettings;
        private bool _isLanguageComboBoxInitialized;
        private bool _isStartupWindowModeComboBoxInitialized;

        public GeneralSettingsPage()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            EnsureLanguageComboBoxItems();
            EnsureStartupWindowModeComboBoxItems();
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
                EnsureLanguageComboBoxItems();
                EnsureStartupWindowModeComboBoxItems();

                string settingValue = AppSettingsService.Instance.GetLanguagePreference();
                StartupWindowMode startupMode = AppSettingsService.Instance.GetStartupWindowMode();
                bool enterScreenAnnotationWhenMinimized = AppSettingsService.Instance.GetEnterScreenAnnotationWhenMinimized();

                _isSyncingUiFromSettings = true;
                LanguageComboBox.SelectedValue = settingValue;
                StartupWindowModeComboBox.SelectedValue = StartupWindowModeParser.ToSettingValue(startupMode);
                EnterScreenAnnotationWhenMinimizedToggleSwitch.IsOn = enterScreenAnnotationWhenMinimized;
            }
            catch (Exception ex)
            {
                // 同步失败不应影响设置页：记录日志便于排查。
                AppLog.Warn("Settings", "同步常规设置 UI 失败", ex);
            }
            finally
            {
                _isSyncingUiFromSettings = false;
            }
        }

        private void EnsureStartupWindowModeComboBoxItems()
        {
            if (_isStartupWindowModeComboBoxInitialized)
            {
                return;
            }

            _isStartupWindowModeComboBoxInitialized = true;

            try
            {
                // 初始化过程中会触发 SelectionChanged，这里统一屏蔽。
                _isSyncingUiFromSettings = true;
                StartupWindowModeComboBox.Items.Clear();

                StartupWindowModeComboBox.Items.Add(new ComboBoxItem
                {
                    Content = L10n.Get("Settings_General_StartupWindowMode_Windowed"),
                    Tag = StartupWindowModeParser.WindowedValue,
                });

                StartupWindowModeComboBox.Items.Add(new ComboBoxItem
                {
                    Content = L10n.Get("Settings_General_StartupWindowMode_FullScreen"),
                    Tag = StartupWindowModeParser.FullScreenValue,
                });
            }
            catch (Exception ex)
            {
                // 构建下拉框失败不应影响设置页：记录日志并降级为“固定列表”。
                AppLog.Warn("Settings", "初始化启动窗口形态下拉框失败，将回退到固定列表", ex);
                BuildStartupWindowModeComboBoxItemsFallback();
            }
            finally
            {
                _isSyncingUiFromSettings = false;
            }
        }

        private void BuildStartupWindowModeComboBoxItemsFallback()
        {
            StartupWindowModeComboBox.Items.Clear();

            StartupWindowModeComboBox.Items.Add(new ComboBoxItem
            {
                Content = L10n.Get("Settings_General_StartupWindowMode_Windowed"),
                Tag = StartupWindowModeParser.WindowedValue,
            });

            StartupWindowModeComboBox.Items.Add(new ComboBoxItem
            {
                Content = L10n.Get("Settings_General_StartupWindowMode_FullScreen"),
                Tag = StartupWindowModeParser.FullScreenValue,
            });
        }

        private void OnStartupWindowModeSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSyncingUiFromSettings)
            {
                return;
            }

            try
            {
                string? value = StartupWindowModeComboBox.SelectedValue as string;
                if (string.IsNullOrWhiteSpace(value) && StartupWindowModeComboBox.SelectedItem is ComboBoxItem item)
                {
                    value = item.Tag as string;
                }

                StartupWindowModeParser.TryParse(value, out StartupWindowMode mode);
                string normalized = StartupWindowModeParser.ToSettingValue(mode);
                AppLog.Info("Settings", $"用户设置启动窗口形态：value='{normalized}'");

                AppSettingsService.Instance.SetStartupWindowMode(mode);
            }
            catch (Exception ex)
            {
                // 切换失败不应导致设置页崩溃：记录日志并回退 UI。
                AppLog.Warn("Settings", "设置启动窗口形态失败", ex);
                SyncUiFromSettings();
            }
        }

        private void OnEnterScreenAnnotationWhenMinimizedToggled(object sender, RoutedEventArgs e)
        {
            if (_isSyncingUiFromSettings)
            {
                return;
            }

            try
            {
                bool enabled = EnterScreenAnnotationWhenMinimizedToggleSwitch.IsOn;
                AppLog.Info("Settings", $"用户设置“最小化进入屏幕批注”：enabled={enabled}");
                AppSettingsService.Instance.SetEnterScreenAnnotationWhenMinimized(enabled);
            }
            catch (Exception ex)
            {
                // 切换失败不应导致设置页崩溃：记录日志并回退 UI。
                AppLog.Warn("Settings", "设置“最小化进入屏幕批注”失败", ex);
                SyncUiFromSettings();
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

                string settingValue = AppLanguagePreferenceParser.NormalizeOrDefault(value);
                AppLog.Info("L10n", $"用户切换语言偏好：value='{settingValue}'");

                AppSettingsService.Instance.SetLanguagePreference(settingValue);
                AppLanguageService.Apply(settingValue);

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

        private void EnsureLanguageComboBoxItems()
        {
            if (_isLanguageComboBoxInitialized)
            {
                return;
            }

            _isLanguageComboBoxInitialized = true;

            try
            {
                // 初始化过程中会触发 SelectionChanged，这里统一屏蔽。
                _isSyncingUiFromSettings = true;
                LanguageComboBox.Items.Clear();

                LanguageComboBox.Items.Add(new ComboBoxItem
                {
                    Content = L10n.Get("Settings_General_Language_FollowSystem"),
                    Tag = AppLanguagePreferenceParser.SystemValue,
                });

                foreach (string cultureName in L10n.GetSupportedCultureNames())
                {
                    LanguageComboBox.Items.Add(new ComboBoxItem
                    {
                        Content = FormatCultureDisplayName(cultureName),
                        Tag = cultureName,
                    });
                }
            }
            catch (Exception ex)
            {
                // 构建下拉框失败不应影响设置页：记录日志并降级为“固定列表”。
                AppLog.Warn("L10n", "初始化语言下拉框失败，将回退到固定列表", ex);
                BuildLanguageComboBoxItemsFallback();
            }
            finally
            {
                _isSyncingUiFromSettings = false;
            }
        }

        private void BuildLanguageComboBoxItemsFallback()
        {
            LanguageComboBox.Items.Clear();

            LanguageComboBox.Items.Add(new ComboBoxItem
            {
                Content = L10n.Get("Settings_General_Language_FollowSystem"),
                Tag = AppLanguagePreferenceParser.SystemValue,
            });

            LanguageComboBox.Items.Add(new ComboBoxItem
            {
                Content = L10n.Get("Settings_General_Language_Chinese"),
                Tag = AppLanguagePreferenceParser.ChineseValue,
            });

            LanguageComboBox.Items.Add(new ComboBoxItem
            {
                Content = L10n.Get("Settings_General_Language_English"),
                Tag = AppLanguagePreferenceParser.EnglishValue,
            });
        }

        private static string FormatCultureDisplayName(string cultureName)
        {
            try
            {
                CultureInfo culture = CultureInfo.GetCultureInfo(cultureName);
                string nativeName = string.IsNullOrWhiteSpace(culture.NativeName) ? culture.Name : culture.NativeName;
                return $"{nativeName} ({culture.Name})";
            }
            catch
            {
                return cultureName;
            }
        }

        private void OnCamouflageSettingsClicked(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(CamouflageSettingsPage));
        }
    }
}

