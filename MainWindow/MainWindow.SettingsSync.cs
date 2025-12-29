using System;
using System.Windows.Media;
using WindBoard.Models;
using WindBoard.Services;

namespace WindBoard
{
    public partial class MainWindow
    {
        private void InitializeSettings()
        {
            _defaultTitle = WindowTitle;
            _defaultIcon = Icon;

            SettingsService.Instance.Load();
            ApplySettingsSnapshot();
            SettingsService.Instance.SettingsChanged += SettingsService_SettingsChanged;
        }

        private void SettingsService_SettingsChanged(object? sender, AppSettings e)
        {
            ApplySettingsSnapshot();
        }

        private void ApplySettingsSnapshot()
        {
            SetBackgroundColor(SettingsService.Instance.GetBackgroundColor());
            IsVideoPresenterEnabled = SettingsService.Instance.GetVideoPresenterEnabled();
            ApplyCamouflageFromSettings();

            if (_zoomPanService != null)
            {
                try { _zoomPanService.TwoFingerOnly = SettingsService.Instance.GetZoomPanTwoFingerOnly(); } catch { }
            }

            if (_strokeService != null && _zoomPanService != null)
            {
                _strokeService.SetStrokeThicknessConsistencyEnabled(
                    SettingsService.Instance.GetStrokeThicknessConsistencyEnabled(),
                    _zoomPanService.Zoom);
                _strokeService.UpdatePenThickness(_zoomPanService.Zoom);
            }

            ApplyInkModeSettingsSnapshot();
        }

        private void ApplyInkModeSettingsSnapshot()
        {
            if (_inkMode == null)
            {
                return;
            }

            _inkMode.SetSimulatedPressureEnabled(SettingsService.Instance.GetSimulatedPressureEnabled());
        }

        private void ApplyCamouflageFromSettings()
        {
            var result = CamouflageService.Instance.BuildResult(_defaultIcon, _defaultTitle);
            WindowTitle = result.Title;
            if (result.Icon != null)
            {
                Icon = result.Icon;
            }
            CamouflageService.Instance.UpdateDesktopShortcut(result.Title, result.IconPath, result.Enabled);
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
