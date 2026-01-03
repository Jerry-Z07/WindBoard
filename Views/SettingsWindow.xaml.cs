using System;
using System.ComponentModel;
using System.Windows;
using MaterialDesignThemes.Wpf;
using WindBoard.Models;
using WindBoard.Services;

namespace WindBoard
{
    public partial class SettingsWindow : Window, INotifyPropertyChanged
    {
        public SettingsWindow()
        {
            InitializeComponent();
            _colorPopupBox = FindName("ColorPopupBox") as PopupBox;
            LocalizationService.Instance.LanguageChanged += LocalizationService_LanguageChanged;
            RefreshAppLanguageItems();
            RefreshStrokeSmoothingModeItems();

            // 初始化颜色为当前设置服务中的背景色
            _currentColor = SettingsService.Instance.GetBackgroundColor();
            OnPropertyChanged(nameof(CurrentColor));
            OnPropertyChanged(nameof(CurrentColorHex));

            // 初始化语言
            try
            {
                _appLanguage = SettingsService.Instance.GetLanguage();
            }
            catch
            {
                _appLanguage = AppLanguage.Chinese;
            }
            OnPropertyChanged(nameof(AppLanguage));

            // 初始化 Hex 文本框内容改为依赖绑定刷新（避免破坏 XAML 绑定）
            // 不再直接赋值 HexTextBox.Text，CurrentColor/CurrentColorHex 的 OnPropertyChanged 将驱动界面更新。

            // 初始化“视频展台”相关设置
            try
            {
                _videoPresenterEnabled = SettingsService.Instance.GetVideoPresenterEnabled();
                _videoPresenterPath = SettingsService.Instance.GetVideoPresenterPath();
                _videoPresenterArgs = SettingsService.Instance.GetVideoPresenterArgs();
            }
            catch
            {
                _videoPresenterEnabled = true;
                _videoPresenterPath = DefaultVideoPresenterPath;
                _videoPresenterArgs = DefaultVideoPresenterArgs;
            }
            OnPropertyChanged(nameof(VideoPresenterEnabled));
            OnPropertyChanged(nameof(VideoPresenterPath));
            OnPropertyChanged(nameof(VideoPresenterArgs));

            // 初始化"书写设置"
            try
            {
                _strokeSmoothingMode = SettingsService.Instance.GetStrokeSmoothingMode();
                _strokeThicknessConsistencyEnabled = SettingsService.Instance.GetStrokeThicknessConsistencyEnabled();
                _simulatedPressureEnabled = SettingsService.Instance.GetSimulatedPressureEnabled();
            }
            catch
            {
                _strokeSmoothingMode = StrokeSmoothingMode.RawInput;
                _strokeThicknessConsistencyEnabled = false;
                _simulatedPressureEnabled = false;
            }
            OnPropertyChanged(nameof(StrokeSmoothingMode));
            OnPropertyChanged(nameof(StrokeThicknessConsistencyEnabled));
            OnPropertyChanged(nameof(SimulatedPressureEnabled));

            // 初始化"触摸手势"
            try
            {
                _zoomPanTwoFingerOnly = SettingsService.Instance.GetZoomPanTwoFingerOnly();
            }
            catch
            {
                _zoomPanTwoFingerOnly = false;
            }
            OnPropertyChanged(nameof(ZoomPanTwoFingerOnly));

            // 初始化"更新设置"
            try
            {
                _autoCheckUpdatesEnabled = SettingsService.Instance.GetAutoCheckUpdatesEnabled();
            }
            catch
            {
                _autoCheckUpdatesEnabled = true;
            }
            OnPropertyChanged(nameof(AutoCheckUpdatesEnabled));

            // 初始化伪装相关设置
            try
            {
                _camouflageEnabled = SettingsService.Instance.GetCamouflageEnabled();
                _camouflageTitle = SettingsService.Instance.GetCamouflageTitle();
                _camouflageSourcePath = SettingsService.Instance.GetCamouflageSourcePath();
            }
            catch
            {
                _camouflageEnabled = false;
                _camouflageTitle = string.Empty;
                _camouflageSourcePath = string.Empty;
            }
            OnPropertyChanged(nameof(CamouflageEnabled));
            OnPropertyChanged(nameof(CamouflageTitle));
            OnPropertyChanged(nameof(CamouflageSourcePath));
            OnPropertyChanged(nameof(CamouflageSourceDisplayName));

            RefreshCamouflagePreview(buildCache: false);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        // 统一底部按钮：应用/确定/取消
        private void ApplyAllSettings()
        {
            try { SettingsService.Instance.SetLanguage(AppLanguage); } catch { }
            try { SettingsService.Instance.SetVideoPresenterPath(VideoPresenterPath); } catch { }
            try { SettingsService.Instance.SetVideoPresenterArgs(VideoPresenterArgs); } catch { }
            try { SettingsService.Instance.SetBackgroundColor(CurrentColor); } catch { }
            try { SettingsService.Instance.SetCamouflageEnabled(CamouflageEnabled); } catch { }
            try { SettingsService.Instance.SetCamouflageTitle(CamouflageTitle); } catch { }
            try { SettingsService.Instance.SetCamouflageSourcePath(CamouflageSourcePath); } catch { }
            try { SettingsService.Instance.SetStrokeSmoothingMode(StrokeSmoothingMode); } catch { }
            try { SettingsService.Instance.SetStrokeThicknessConsistencyEnabled(StrokeThicknessConsistencyEnabled); } catch { }
            try { SettingsService.Instance.SetSimulatedPressureEnabled(SimulatedPressureEnabled); } catch { }
        }

        private void BtnApply_Click(object sender, RoutedEventArgs e) => ApplyAllSettings();

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            ApplyAllSettings();
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e) => Close();

        protected override void OnClosed(EventArgs e)
        {
            LocalizationService.Instance.LanguageChanged -= LocalizationService_LanguageChanged;
            base.OnClosed(e);
        }

        private void LocalizationService_LanguageChanged(object? sender, AppLanguage e)
        {
            RefreshAppLanguageItems();
            RefreshStrokeSmoothingModeItems();

            if (string.IsNullOrWhiteSpace(CamouflageSourcePath))
            {
                CamouflageSourceDisplayName = LocalizationService.Instance.GetString("SettingsWindow_General_Camouflage_NoFileSelected");
            }
        }
    }
}
