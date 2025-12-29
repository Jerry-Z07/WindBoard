using System.Windows;
using WindBoard.Services;

namespace WindBoard
{
    public partial class SettingsWindow
    {
        public bool ZoomPanTwoFingerOnly
        {
            get => _zoomPanTwoFingerOnly;
            set
            {
                if (_zoomPanTwoFingerOnly != value)
                {
                    _zoomPanTwoFingerOnly = value;
                    OnPropertyChanged();
                    try { SettingsService.Instance.SetZoomPanTwoFingerOnly(value); } catch { }
                }
            }
        }

        private void ToggleZoomPanTwoFingerOnly_Checked(object sender, RoutedEventArgs e)
        {
            ZoomPanTwoFingerOnly = true;
        }

        private void ToggleZoomPanTwoFingerOnly_Unchecked(object sender, RoutedEventArgs e)
        {
            ZoomPanTwoFingerOnly = false;
        }
    }
}

