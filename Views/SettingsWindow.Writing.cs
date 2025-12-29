using System;
using System.Windows;
using WindBoard.Services;

namespace WindBoard
{
    public partial class SettingsWindow
    {
        // --- 书写设置属性（绑定到 XAML，通过 ElementName=SettingsWindowRoot） ---
        public bool StrokeThicknessConsistencyEnabled
        {
            get => _strokeThicknessConsistencyEnabled;
            set
            {
                if (_strokeThicknessConsistencyEnabled != value)
                {
                    _strokeThicknessConsistencyEnabled = value;
                    OnPropertyChanged();
                    // 立即持久化：与其他设置一致（切换开关即时生效）
                    try { SettingsService.Instance.SetStrokeThicknessConsistencyEnabled(value); } catch { }
                }
            }
        }

        public bool SimulatedPressureEnabled
        {
            get => _simulatedPressureEnabled;
            set
            {
                if (_simulatedPressureEnabled != value)
                {
                    _simulatedPressureEnabled = value;
                    OnPropertyChanged();
                    try { SettingsService.Instance.SetSimulatedPressureEnabled(value); } catch { }
                }
            }
        }

        // --- 书写设置事件处理 ---
        private void ToggleStrokeThicknessConsistency_Checked(object sender, RoutedEventArgs e)
        {
            StrokeThicknessConsistencyEnabled = true;
        }

        private void ToggleStrokeThicknessConsistency_Unchecked(object sender, RoutedEventArgs e)
        {
            StrokeThicknessConsistencyEnabled = false;
        }

        private void ToggleSimulatedPressure_Checked(object sender, RoutedEventArgs e)
        {
            SimulatedPressureEnabled = true;
        }

        private void ToggleSimulatedPressure_Unchecked(object sender, RoutedEventArgs e)
        {
            SimulatedPressureEnabled = false;
        }
    }
}
