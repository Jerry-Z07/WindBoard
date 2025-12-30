using System;
using System.Windows;
using System.Windows.Controls;
using MaterialDesignThemes.Wpf;
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

        // --- 平滑参数属性 ---
        public bool CustomSmoothingEnabled
        {
            get => _customSmoothingEnabled;
            set
            {
                if (_customSmoothingEnabled != value)
                {
                    _customSmoothingEnabled = value;
                    OnPropertyChanged();
                    try { SettingsService.Instance.SetCustomSmoothingEnabled(value); } catch { }
                }
            }
        }

        // 笔参数
        public double SmoothingPenStepMm
        {
            get => _smoothingPenStepMm;
            set
            {
                if (Math.Abs(_smoothingPenStepMm - value) > 0.001)
                {
                    _smoothingPenStepMm = value;
                    OnPropertyChanged();
                    try { SettingsService.Instance.SetSmoothingPenStepMm(value); } catch { }
                }
            }
        }

        public double SmoothingPenEpsilonMm
        {
            get => _smoothingPenEpsilonMm;
            set
            {
                if (Math.Abs(_smoothingPenEpsilonMm - value) > 0.001)
                {
                    _smoothingPenEpsilonMm = value;
                    OnPropertyChanged();
                    try { SettingsService.Instance.SetSmoothingPenEpsilonMm(value); } catch { }
                }
            }
        }

        public double SmoothingPenFcMin
        {
            get => _smoothingPenFcMin;
            set
            {
                if (Math.Abs(_smoothingPenFcMin - value) > 0.001)
                {
                    _smoothingPenFcMin = value;
                    OnPropertyChanged();
                    try { SettingsService.Instance.SetSmoothingPenFcMin(value); } catch { }
                }
            }
        }

        public double SmoothingPenBeta
        {
            get => _smoothingPenBeta;
            set
            {
                if (Math.Abs(_smoothingPenBeta - value) > 0.0001)
                {
                    _smoothingPenBeta = value;
                    OnPropertyChanged();
                    try { SettingsService.Instance.SetSmoothingPenBeta(value); } catch { }
                }
            }
        }

        public double SmoothingPenDCutoff
        {
            get => _smoothingPenDCutoff;
            set
            {
                if (Math.Abs(_smoothingPenDCutoff - value) > 0.001)
                {
                    _smoothingPenDCutoff = value;
                    OnPropertyChanged();
                    try { SettingsService.Instance.SetSmoothingPenDCutoff(value); } catch { }
                }
            }
        }

        // 手指参数
        public double SmoothingFingerStepMm
        {
            get => _smoothingFingerStepMm;
            set
            {
                if (Math.Abs(_smoothingFingerStepMm - value) > 0.001)
                {
                    _smoothingFingerStepMm = value;
                    OnPropertyChanged();
                    try { SettingsService.Instance.SetSmoothingFingerStepMm(value); } catch { }
                }
            }
        }

        public double SmoothingFingerEpsilonMm
        {
            get => _smoothingFingerEpsilonMm;
            set
            {
                if (Math.Abs(_smoothingFingerEpsilonMm - value) > 0.001)
                {
                    _smoothingFingerEpsilonMm = value;
                    OnPropertyChanged();
                    try { SettingsService.Instance.SetSmoothingFingerEpsilonMm(value); } catch { }
                }
            }
        }

        public double SmoothingFingerFcMin
        {
            get => _smoothingFingerFcMin;
            set
            {
                if (Math.Abs(_smoothingFingerFcMin - value) > 0.001)
                {
                    _smoothingFingerFcMin = value;
                    OnPropertyChanged();
                    try { SettingsService.Instance.SetSmoothingFingerFcMin(value); } catch { }
                }
            }
        }

        public double SmoothingFingerBeta
        {
            get => _smoothingFingerBeta;
            set
            {
                if (Math.Abs(_smoothingFingerBeta - value) > 0.0001)
                {
                    _smoothingFingerBeta = value;
                    OnPropertyChanged();
                    try { SettingsService.Instance.SetSmoothingFingerBeta(value); } catch { }
                }
            }
        }

        public double SmoothingFingerDCutoff
        {
            get => _smoothingFingerDCutoff;
            set
            {
                if (Math.Abs(_smoothingFingerDCutoff - value) > 0.001)
                {
                    _smoothingFingerDCutoff = value;
                    OnPropertyChanged();
                    try { SettingsService.Instance.SetSmoothingFingerDCutoff(value); } catch { }
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

        // --- 平滑参数展开器事件处理 ---
        private async void SmoothingExpander_Expanded(object sender, RoutedEventArgs e)
        {
            // 检查是否需要显示风险提示
            if (!SettingsService.Instance.GetSmoothingWarningDismissed())
            {
                var expander = sender as Expander;

                // 创建弹窗内容
                var stackPanel = new StackPanel { Margin = new Thickness(24) };

                var title = new TextBlock
                {
                    Text = "修改风险提示",
                    Style = (Style)FindResource("MaterialDesignHeadline6TextBlock"),
                    Margin = new Thickness(0, 0, 0, 16)
                };

                var content = new TextBlock
                {
                    Text = "平滑参数会直接影响笔迹的书写体验。不当的参数设置可能导致：\n\n" +
                           "• 笔迹延迟过高或过低\n" +
                           "• 书写不流畅或过度平滑\n" +
                           "• 细节丢失或抖动明显\n\n" +
                           "建议仅在了解参数含义的情况下进行调整。",
                    Style = (Style)FindResource("MaterialDesignBodyMediumTextBlock"),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 16),
                    MaxWidth = 400
                };

                var checkBox = new CheckBox
                {
                    Content = "不再提示",
                    Margin = new Thickness(0, 0, 0, 16)
                };

                var buttonPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right
                };

                var cancelButton = new Button
                {
                    Content = "取消",
                    Style = (Style)FindResource("MaterialDesignFlatButton"),
                    IsCancel = true,
                    Command = DialogHost.CloseDialogCommand,
                    CommandParameter = false,
                    Margin = new Thickness(0, 0, 8, 0)
                };

                var okButton = new Button
                {
                    Content = "我已了解",
                    Style = (Style)FindResource("MaterialDesignFlatButton"),
                    IsDefault = true,
                    Command = DialogHost.CloseDialogCommand,
                    CommandParameter = true
                };

                buttonPanel.Children.Add(cancelButton);
                buttonPanel.Children.Add(okButton);

                stackPanel.Children.Add(title);
                stackPanel.Children.Add(content);
                stackPanel.Children.Add(checkBox);
                stackPanel.Children.Add(buttonPanel);

                var result = await DialogHost.Show(stackPanel, "SettingsDialogHost");

                if (result is bool confirmed && confirmed)
                {
                    // 用户确认，保存"不再提示"选项
                    if (checkBox.IsChecked == true)
                    {
                        SettingsService.Instance.SetSmoothingWarningDismissed(true);
                    }
                }
                else
                {
                    // 用户取消，折叠展开器
                    if (expander != null)
                    {
                        expander.IsExpanded = false;
                    }
                }
            }
        }

        private void ToggleCustomSmoothing_Checked(object sender, RoutedEventArgs e)
        {
            CustomSmoothingEnabled = true;
        }

        private void ToggleCustomSmoothing_Unchecked(object sender, RoutedEventArgs e)
        {
            CustomSmoothingEnabled = false;
        }

        private void BtnResetSmoothingParameters_Click(object sender, RoutedEventArgs e)
        {
            SettingsService.Instance.ResetSmoothingParameters();

            // 刷新本地字段
            _smoothingPenStepMm = SettingsService.Instance.GetSmoothingPenStepMm();
            _smoothingPenEpsilonMm = SettingsService.Instance.GetSmoothingPenEpsilonMm();
            _smoothingPenFcMin = SettingsService.Instance.GetSmoothingPenFcMin();
            _smoothingPenBeta = SettingsService.Instance.GetSmoothingPenBeta();
            _smoothingPenDCutoff = SettingsService.Instance.GetSmoothingPenDCutoff();

            _smoothingFingerStepMm = SettingsService.Instance.GetSmoothingFingerStepMm();
            _smoothingFingerEpsilonMm = SettingsService.Instance.GetSmoothingFingerEpsilonMm();
            _smoothingFingerFcMin = SettingsService.Instance.GetSmoothingFingerFcMin();
            _smoothingFingerBeta = SettingsService.Instance.GetSmoothingFingerBeta();
            _smoothingFingerDCutoff = SettingsService.Instance.GetSmoothingFingerDCutoff();

            // 通知 UI 更新
            OnPropertyChanged(nameof(SmoothingPenStepMm));
            OnPropertyChanged(nameof(SmoothingPenEpsilonMm));
            OnPropertyChanged(nameof(SmoothingPenFcMin));
            OnPropertyChanged(nameof(SmoothingPenBeta));
            OnPropertyChanged(nameof(SmoothingPenDCutoff));
            OnPropertyChanged(nameof(SmoothingFingerStepMm));
            OnPropertyChanged(nameof(SmoothingFingerEpsilonMm));
            OnPropertyChanged(nameof(SmoothingFingerFcMin));
            OnPropertyChanged(nameof(SmoothingFingerBeta));
            OnPropertyChanged(nameof(SmoothingFingerDCutoff));
        }
    }
}
