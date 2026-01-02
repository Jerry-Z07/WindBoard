using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using WindBoard.Models;
using WindBoard.Services;

namespace WindBoard
{
    public partial class SettingsWindow
    {
        public sealed class StrokeSmoothingModeItem : INotifyPropertyChanged
        {
            public StrokeSmoothingMode Mode { get; }

            private string _displayName;
            public string DisplayName
            {
                get => _displayName;
                set
                {
                    if (_displayName != value)
                    {
                        _displayName = value;
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));
                    }
                }
            }

            public event PropertyChangedEventHandler? PropertyChanged;

            public StrokeSmoothingModeItem(StrokeSmoothingMode mode, string displayName)
            {
                Mode = mode;
                _displayName = displayName;
            }
        }

        private readonly ObservableCollection<StrokeSmoothingModeItem> _strokeSmoothingModeItems = new();
        public ObservableCollection<StrokeSmoothingModeItem> StrokeSmoothingModeItems => _strokeSmoothingModeItems;

        private void RefreshStrokeSmoothingModeItems()
        {
            var l = LocalizationService.Instance;
            string rawInput = l.GetString("SettingsWindow_Writing_Smoothing_RawInput");

            if (_strokeSmoothingModeItems.Count == 0)
            {
                _strokeSmoothingModeItems.Add(new StrokeSmoothingModeItem(StrokeSmoothingMode.RawInput, rawInput));
                _strokeSmoothingModeItems.Add(new StrokeSmoothingModeItem(StrokeSmoothingMode.Existing, "DPS"));
                return;
            }

            foreach (StrokeSmoothingModeItem item in _strokeSmoothingModeItems)
            {
                if (item.Mode == StrokeSmoothingMode.RawInput)
                {
                    item.DisplayName = rawInput;
                }
            }
        }

        // --- 书写设置属性（绑定到 XAML，通过 ElementName=SettingsWindowRoot） ---
        public StrokeSmoothingMode StrokeSmoothingMode
        {
            get => _strokeSmoothingMode;
            set
            {
                if (_strokeSmoothingMode != value)
                {
                    _strokeSmoothingMode = value;
                    OnPropertyChanged();
                    try { SettingsService.Instance.SetStrokeSmoothingMode(value); } catch { }
                }
            }
        }

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
