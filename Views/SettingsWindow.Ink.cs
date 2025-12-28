using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using WindBoard.Core.Ink;
using WindBoard.Services;

namespace WindBoard
{
    public partial class SettingsWindow
    {
        public bool StrokeThicknessConsistencyEnabled
        {
            get => _strokeThicknessConsistencyEnabled;
            set
            {
                if (_strokeThicknessConsistencyEnabled != value)
                {
                    _strokeThicknessConsistencyEnabled = value;
                    OnPropertyChanged();
                    try
                    {
                        SettingsService.Instance.SetStrokeThicknessConsistencyEnabled(value);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Failed to persist StrokeThicknessConsistencyEnabled setting: {ex}");
                    }
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
                    SchedulePersistSimulatedPressure();
                }
            }
        }

        public double SimulatedPressureStartTaperMm
        {
            get => _simulatedPressureStartTaperMm;
            set
            {
                if (Math.Abs(_simulatedPressureStartTaperMm - value) > 0.0001)
                {
                    _simulatedPressureStartTaperMm = value;
                    OnPropertyChanged();
                    SchedulePersistSimulatedPressure();
                }
            }
        }

        public double SimulatedPressureEndTaperMm
        {
            get => _simulatedPressureEndTaperMm;
            set
            {
                if (Math.Abs(_simulatedPressureEndTaperMm - value) > 0.0001)
                {
                    _simulatedPressureEndTaperMm = value;
                    OnPropertyChanged();
                    SchedulePersistSimulatedPressure();
                }
            }
        }

        public double SimulatedPressureSpeedMinMmPerSec
        {
            get => _simulatedPressureSpeedMinMmPerSec;
            set
            {
                if (Math.Abs(_simulatedPressureSpeedMinMmPerSec - value) > 0.0001)
                {
                    _simulatedPressureSpeedMinMmPerSec = value;
                    OnPropertyChanged();
                    SchedulePersistSimulatedPressure();
                }
            }
        }

        public double SimulatedPressureSpeedMaxMmPerSec
        {
            get => _simulatedPressureSpeedMaxMmPerSec;
            set
            {
                if (Math.Abs(_simulatedPressureSpeedMaxMmPerSec - value) > 0.0001)
                {
                    _simulatedPressureSpeedMaxMmPerSec = value;
                    OnPropertyChanged();
                    SchedulePersistSimulatedPressure();
                }
            }
        }

        public double SimulatedPressureFastSpeedMinFactor
        {
            get => _simulatedPressureFastSpeedMinFactor;
            set
            {
                if (Math.Abs(_simulatedPressureFastSpeedMinFactor - value) > 0.0001)
                {
                    _simulatedPressureFastSpeedMinFactor = value;
                    OnPropertyChanged();
                    SchedulePersistSimulatedPressure();
                }
            }
        }

        public double SimulatedPressureFloor
        {
            get => _simulatedPressureFloor;
            set
            {
                if (Math.Abs(_simulatedPressureFloor - value) > 0.0001)
                {
                    _simulatedPressureFloor = value;
                    OnPropertyChanged();
                    SchedulePersistSimulatedPressure();
                }
            }
        }

        public double SimulatedPressureEndFloor
        {
            get => _simulatedPressureEndFloor;
            set
            {
                if (Math.Abs(_simulatedPressureEndFloor - value) > 0.0001)
                {
                    _simulatedPressureEndFloor = value;
                    OnPropertyChanged();
                    SchedulePersistSimulatedPressure();
                }
            }
        }

        public double SimulatedPressureSmoothingTauMs
        {
            get => _simulatedPressureSmoothingTauMs;
            set
            {
                if (Math.Abs(_simulatedPressureSmoothingTauMs - value) > 0.0001)
                {
                    _simulatedPressureSmoothingTauMs = value;
                    OnPropertyChanged();
                    SchedulePersistSimulatedPressure();
                }
            }
        }

        private void ApplySimulatedPressureFields(SimulatedPressureConfig cfg)
        {
            _isApplyingSimulatedPressureFields = true;
            try
            {
                _simulatedPressureEnabled = cfg.Enabled;
                _simulatedPressureStartTaperMm = cfg.StartTaperMm;
                _simulatedPressureEndTaperMm = cfg.EndTaperMm;
                _simulatedPressureSpeedMinMmPerSec = cfg.SpeedMinMmPerSec;
                _simulatedPressureSpeedMaxMmPerSec = cfg.SpeedMaxMmPerSec;
                _simulatedPressureFastSpeedMinFactor = cfg.FastSpeedMinFactor;
                _simulatedPressureFloor = cfg.PressureFloor;
                _simulatedPressureEndFloor = cfg.EndPressureFloor;
                _simulatedPressureSmoothingTauMs = cfg.SmoothingTauMs;

                OnPropertyChanged(nameof(SimulatedPressureEnabled));
                OnPropertyChanged(nameof(SimulatedPressureStartTaperMm));
                OnPropertyChanged(nameof(SimulatedPressureEndTaperMm));
                OnPropertyChanged(nameof(SimulatedPressureSpeedMinMmPerSec));
                OnPropertyChanged(nameof(SimulatedPressureSpeedMaxMmPerSec));
                OnPropertyChanged(nameof(SimulatedPressureFastSpeedMinFactor));
                OnPropertyChanged(nameof(SimulatedPressureFloor));
                OnPropertyChanged(nameof(SimulatedPressureEndFloor));
                OnPropertyChanged(nameof(SimulatedPressureSmoothingTauMs));
            }
            finally
            {
                _isApplyingSimulatedPressureFields = false;
            }
        }

        private SimulatedPressureConfig BuildSimulatedPressureConfigFromFields()
        {
            return new SimulatedPressureConfig
            {
                Enabled = _simulatedPressureEnabled,
                StartTaperMm = _simulatedPressureStartTaperMm,
                EndTaperMm = _simulatedPressureEndTaperMm,
                SpeedMinMmPerSec = _simulatedPressureSpeedMinMmPerSec,
                SpeedMaxMmPerSec = _simulatedPressureSpeedMaxMmPerSec,
                FastSpeedMinFactor = _simulatedPressureFastSpeedMinFactor,
                PressureFloor = (float)_simulatedPressureFloor,
                EndPressureFloor = (float)_simulatedPressureEndFloor,
                SmoothingTauMs = _simulatedPressureSmoothingTauMs
            };
        }

        private void SchedulePersistSimulatedPressure()
        {
            if (_isApplyingSimulatedPressureFields) return;

            _simulatedPressurePersistTimer ??= new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(160)
            };

            _simulatedPressurePersistTimer.Stop();
            _simulatedPressurePersistTimer.Tick -= SimulatedPressurePersistTimer_Tick;
            _simulatedPressurePersistTimer.Tick += SimulatedPressurePersistTimer_Tick;
            _simulatedPressurePersistTimer.Start();
        }

        private void SimulatedPressurePersistTimer_Tick(object? sender, EventArgs e)
        {
            _simulatedPressurePersistTimer?.Stop();
            PersistSimulatedPressureNow();
        }

        private void PersistSimulatedPressureNow()
        {
            try
            {
                var cfg = BuildSimulatedPressureConfigFromFields();
                cfg.ClampInPlace();
                SettingsService.Instance.SetSimulatedPressureConfig(cfg);
                ApplySimulatedPressureFields(cfg);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Settings] Failed to persist simulated pressure config: {ex}");
            }
        }

        private void CleanupSimulatedPressureTimer(bool persistPending)
        {
            if (_simulatedPressurePersistTimer != null)
            {
                _simulatedPressurePersistTimer.Stop();
                _simulatedPressurePersistTimer.Tick -= SimulatedPressurePersistTimer_Tick;
                _simulatedPressurePersistTimer = null;
            }

            if (persistPending)
            {
                PersistSimulatedPressureNow();
            }
        }

        private void BtnResetSimulatedPressure_Click(object sender, RoutedEventArgs e)
        {
            var cfg = DefaultSimulatedPressureConfig.Clone();
            cfg.ClampInPlace();
            ApplySimulatedPressureFields(cfg);
            PersistSimulatedPressureNow();
        }
    }
}
