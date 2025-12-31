using System;
using System.Windows.Media;
using System.Windows.Threading;
using WindBoard.Models;
using WindBoard.Services;

namespace WindBoard
{
    public partial class MainWindow
    {
        private DispatcherTimer? _camouflageShortcutUpdateTimer;
        private string _lastCamouflageSettingsSignature = string.Empty;
        private string _pendingCamouflageShortcutSignature = string.Empty;
        private string _pendingCamouflageShortcutTitle = string.Empty;
        private string? _pendingCamouflageShortcutIconPath;
        private bool _pendingCamouflageShortcutEnabled;

        private void InitializeSettings()
        {
            _defaultTitle = WindowTitle;
            _defaultIcon = Icon;

            SettingsService.Instance.Load();
            ApplySettingsSnapshot(isStartup: true);
            _lastCamouflageSettingsSignature = CamouflageService.Instance.GetCamouflageShortcutSettingsSignature();
            SettingsService.Instance.SettingsChanged += SettingsService_SettingsChanged;
        }

        private void SettingsService_SettingsChanged(object? sender, AppSettings e)
        {
            ApplySettingsSnapshot(isStartup: false);
        }

        private void ApplySettingsSnapshot(bool isStartup)
        {
            SetBackgroundColor(SettingsService.Instance.GetBackgroundColor());
            IsVideoPresenterEnabled = SettingsService.Instance.GetVideoPresenterEnabled();
            var camouflageResult = ApplyCamouflageFromSettings();
            ApplyZoomPanGestureSettingsSnapshot();

            if (_strokeService != null && _zoomPanService != null)
            {
                _strokeService.SetStrokeThicknessConsistencyEnabled(
                    SettingsService.Instance.GetStrokeThicknessConsistencyEnabled(),
                    _zoomPanService.Zoom);
                _strokeService.UpdatePenThickness(_zoomPanService.Zoom);
            }

            ApplyInkModeSettingsSnapshot();

            // 伪装快捷方式：仅在设置“发生修改”时自动更新一次；每次启动不再自动生成。
            if (!isStartup)
            {
                TryScheduleCamouflageShortcutUpdate(camouflageResult);
            }
        }

        private void ApplyZoomPanGestureSettingsSnapshot()
        {
            if (_zoomPanService == null) return;
            try { _zoomPanService.TwoFingerOnly = SettingsService.Instance.GetZoomPanTwoFingerOnly(); } catch { }
        }

        private void ApplyInkModeSettingsSnapshot()
        {
            if (_inkMode == null)
            {
                return;
            }

            _inkMode.SetSimulatedPressureEnabled(SettingsService.Instance.GetSimulatedPressureEnabled());
        }

        private CamouflageResult ApplyCamouflageFromSettings()
        {
            var result = CamouflageService.Instance.BuildResult(_defaultIcon, _defaultTitle);
            WindowTitle = result.Title;
            if (result.Icon != null)
            {
                Icon = result.Icon;
            }
            return result;
        }

        private void TryScheduleCamouflageShortcutUpdate(CamouflageResult currentResult)
        {
            string currentCamouflageSettingsSignature = CamouflageService.Instance.GetCamouflageShortcutSettingsSignature();
            if (string.Equals(currentCamouflageSettingsSignature, _lastCamouflageSettingsSignature, StringComparison.Ordinal))
            {
                return;
            }

            _lastCamouflageSettingsSignature = currentCamouflageSettingsSignature;

            string lastGeneratedSignature = SettingsService.Instance.GetCamouflageShortcutLastGeneratedSignature();
            if (string.Equals(lastGeneratedSignature, currentCamouflageSettingsSignature, StringComparison.Ordinal))
            {
                // 用户把设置改回“已生成过”的状态：取消任何待执行的更新，避免生成旧配置的快捷方式。
                _pendingCamouflageShortcutSignature = string.Empty;
                _pendingCamouflageShortcutTitle = string.Empty;
                _pendingCamouflageShortcutIconPath = null;
                _pendingCamouflageShortcutEnabled = false;
                _camouflageShortcutUpdateTimer?.Stop();
                return;
            }

            _pendingCamouflageShortcutSignature = currentCamouflageSettingsSignature;
            _pendingCamouflageShortcutTitle = currentResult.Title;
            _pendingCamouflageShortcutIconPath = currentResult.IconPath;
            _pendingCamouflageShortcutEnabled = currentResult.Enabled;

            _camouflageShortcutUpdateTimer ??= new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(600)
            };
            _camouflageShortcutUpdateTimer.Stop();
            _camouflageShortcutUpdateTimer.Tick -= CamouflageShortcutUpdateTimer_Tick;
            _camouflageShortcutUpdateTimer.Tick += CamouflageShortcutUpdateTimer_Tick;
            _camouflageShortcutUpdateTimer.Start();
        }

        private void CamouflageShortcutUpdateTimer_Tick(object? sender, EventArgs e)
        {
            _camouflageShortcutUpdateTimer?.Stop();

            if (string.IsNullOrWhiteSpace(_pendingCamouflageShortcutSignature))
            {
                return;
            }

            string currentCamouflageSettingsSignature = CamouflageService.Instance.GetCamouflageShortcutSettingsSignature();
            if (!string.Equals(currentCamouflageSettingsSignature, _pendingCamouflageShortcutSignature, StringComparison.Ordinal))
            {
                // 待更新签名已过期（期间设置又变化了并触发了新一轮调度），本次不再执行。
                return;
            }

            string lastGeneratedSignature = SettingsService.Instance.GetCamouflageShortcutLastGeneratedSignature();
            if (string.Equals(lastGeneratedSignature, _pendingCamouflageShortcutSignature, StringComparison.Ordinal))
            {
                return;
            }

            bool ok = CamouflageService.Instance.TryUpdateDesktopShortcut(
                _pendingCamouflageShortcutTitle,
                _pendingCamouflageShortcutIconPath,
                _pendingCamouflageShortcutEnabled,
                out _,
                out _);

            if (ok)
            {
                SettingsService.Instance.SetCamouflageShortcutLastGeneratedSignature(_pendingCamouflageShortcutSignature);
                _pendingCamouflageShortcutSignature = string.Empty;
            }
        }

        public void SetBackgroundColor(Color color)
        {
            var brush = new SolidColorBrush(color);
            if (CanvasHost != null) CanvasHost.Background = brush;
            if (Viewport != null) Viewport.Background = brush;

            // InkCanvas 必须保持透明，否则会遮住“底层附件”
            if (MyCanvas != null) MyCanvas.Background = Brushes.Transparent;
        }
    }
}
