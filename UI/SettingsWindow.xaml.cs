using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using MaterialDesignThemes.Wpf;

namespace WindBoard.UI
{
    public partial class SettingsWindow : Window, INotifyPropertyChanged
    {
        // 主窗口依赖已移除（仅保留 UI）
        private Color _currentColor;
        private PopupBox? _colorPopupBox;

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public Color CurrentColor
        {
            get => _currentColor;
            set
            {
                if (_currentColor != value)
                {
                    _currentColor = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CurrentColorHex));
                    
                    WindBoard.SettingsService.Instance.SetBackgroundColor(_currentColor);
                }
            }
        }

        public string CurrentColorHex
        {
            get => _currentColor.ToString();
            set
            {
                if (string.IsNullOrWhiteSpace(value)) return;
                var hex = value.Trim();
                if (!hex.StartsWith("#")) hex = "#" + hex;
                try
                {
                    var color = (Color)ColorConverter.ConvertFromString(hex);
                    CurrentColor = color;
                }
                catch (Exception)
                {
                    // 无效颜色时默认使用白色
                    CurrentColor = Colors.White;
                }
            }
        }

        public SettingsWindow()
        {
            InitializeComponent();
            _colorPopupBox = FindName("ColorPopupBox") as PopupBox;

            // 初始化颜色为当前设置服务中的背景色
            _currentColor = WindBoard.SettingsService.Instance.GetBackgroundColor();
            OnPropertyChanged(nameof(CurrentColor));
            OnPropertyChanged(nameof(CurrentColorHex));

            // 初始化 Hex 文本框内容
            if (HexTextBox != null)
            {
                HexTextBox.Text = CurrentColorHex;
            }
        }

        private void PresetColor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string colorCode)
            {
                try
                {
                    Color color = (Color)ColorConverter.ConvertFromString(colorCode);
                    CurrentColor = color;
                    if (_colorPopupBox != null)
                    {
                        _colorPopupBox.IsPopupOpen = false;
                    }
                }
                catch (Exception)
                {
                    // 无效颜色时默认使用白色
                    CurrentColor = Colors.White;
                    if (_colorPopupBox != null)
                    {
                        _colorPopupBox.IsPopupOpen = false;
                    }
                }
            }
        }

        private async void OpenColorPicker_Click(object sender, RoutedEventArgs e)
        {
            if (_colorPopupBox != null)
            {
                _colorPopupBox.IsPopupOpen = false;
            }

            var colorPicker = new ColorPicker
            {
                Color = CurrentColor,
                Width = 500,
                Height = 300,
                Margin = new Thickness(0, 0, 0, 16)
            };

            var stackPanel = new StackPanel { Margin = new Thickness(24) };
            
            var title = new TextBlock 
            { 
                Text = "自定义颜色", 
                Style = (Style)FindResource("MaterialDesignHeadline6TextBlock"), 
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
                CommandParameter = false 
            };
            
            var okButton = new Button 
            { 
                Content = "确定", 
                Style = (Style)FindResource("MaterialDesignFlatButton"), 
                IsDefault = true, 
                Command = DialogHost.CloseDialogCommand, 
                CommandParameter = true 
            };

            buttonPanel.Children.Add(cancelButton);
            buttonPanel.Children.Add(okButton);

            stackPanel.Children.Add(title);
            stackPanel.Children.Add(colorPicker);
            stackPanel.Children.Add(buttonPanel);

            var result = await DialogHost.Show(stackPanel, "SettingsDialogHost");

            if (result is bool confirmed && confirmed)
            {
                CurrentColor = colorPicker.Color;
            }
        }

        private void ApplyHexColorFromTextBox()
        {
            var hex = HexTextBox?.Text?.Trim();
            if (string.IsNullOrWhiteSpace(hex))
                return;

            if (!hex.StartsWith("#"))
                hex = "#" + hex;

            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hex);
                CurrentColor = color;
            }
            catch (Exception)
            {
                // 无效颜色时默认使用白色
                CurrentColor = Colors.White;
            }
        }

        private void HexInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ApplyHexColorFromTextBox();
                e.Handled = true;
            }
        }

        private void HexApply_Click(object sender, RoutedEventArgs e)
        {
            ApplyHexColorFromTextBox();
        }

        private void BtnResetColor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string colorCode)
            {
                try
                {
                    var color = (Color)ColorConverter.ConvertFromString(colorCode);
                    CurrentColor = color;
                }
                catch (Exception)
                {
                    // 无效颜色时默认使用白色
                    CurrentColor = Colors.White;
                }
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
