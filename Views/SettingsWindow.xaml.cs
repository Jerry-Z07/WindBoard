using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using MaterialDesignThemes.Wpf;
using Microsoft.Win32;
using System.IO;

namespace WindBoard
{
    public partial class SettingsWindow : Window, INotifyPropertyChanged
    {
        // 主窗口依赖已移除（仅保留 UI）
        private Color _currentColor;
        private PopupBox? _colorPopupBox;

        // --- 视频展台设置（SettingsWindow 层） ---
        private bool _videoPresenterEnabled;
        private string _videoPresenterPath = string.Empty;
        private string _videoPresenterArgs = string.Empty;

        private const string DefaultVideoPresenterPath = @"C:\\Program Files (x86)\\Seewo\\EasiCamera\\sweclauncher\\sweclauncher.exe";
        private const string DefaultVideoPresenterArgs = "-from en5";

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
                    OnPropertyChanged(nameof(CurrentBrush));

                    SettingsService.Instance.SetBackgroundColor(_currentColor);
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

        // 供 UI 直接绑定刷子，避免 SolidColorBrush.Color 子属性绑定更新问题
        public SolidColorBrush CurrentBrush => new SolidColorBrush(_currentColor);

        // --- 视频展台属性（绑定到 XAML，通过 ElementName=SettingsWindowRoot） ---
        public bool VideoPresenterEnabled
        {
            get => _videoPresenterEnabled;
            set
            {
                if (_videoPresenterEnabled != value)
                {
                    _videoPresenterEnabled = value;
                    OnPropertyChanged();
                    // 立即持久化：与颜色设置一致（切换开关即时生效）
                    try { SettingsService.Instance.SetVideoPresenterEnabled(value); } catch { }
                }
            }
        }

        public string VideoPresenterPath
        {
            get => _videoPresenterPath;
            set
            {
                if (_videoPresenterPath != value)
                {
                    _videoPresenterPath = value ?? string.Empty;
                    OnPropertyChanged();
                }
            }
        }

        public string VideoPresenterArgs
        {
            get => _videoPresenterArgs;
            set
            {
                if (_videoPresenterArgs != value)
                {
                    _videoPresenterArgs = value ?? string.Empty;
                    OnPropertyChanged();
                }
            }
        }

        public SettingsWindow()
        {
            InitializeComponent();
            _colorPopupBox = FindName("ColorPopupBox") as PopupBox;

            // 初始化颜色为当前设置服务中的背景色
            _currentColor = SettingsService.Instance.GetBackgroundColor();
            OnPropertyChanged(nameof(CurrentColor));
            OnPropertyChanged(nameof(CurrentColorHex));

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
            var raw = HexTextBox?.Text;

            var hex = raw?.Trim();
            if (string.IsNullOrWhiteSpace(hex))
            {
                return;
            }

            if (!hex.StartsWith("#"))
                hex = "#" + hex;

            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hex);
                CurrentColor = color;
            }
            catch (Exception)
            {
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

        // --- 基本设置·视频展台：事件处理 ---
        private void ToggleVideoPresenter_Checked(object sender, RoutedEventArgs e)
        {
            VideoPresenterEnabled = true;
        }

        private void ToggleVideoPresenter_Unchecked(object sender, RoutedEventArgs e)
        {
            VideoPresenterEnabled = false;
        }

        private void BtnBrowseVideoPresenterPath_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new OpenFileDialog
                {
                    Title = "选择视频展台程序",
                    Filter = "可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*",
                    CheckFileExists = true
                };
                if (!string.IsNullOrWhiteSpace(VideoPresenterPath))
                {
                    try
                    {
                        dlg.InitialDirectory = Path.GetDirectoryName(VideoPresenterPath) ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                    }
                    catch { }
                }
                if (dlg.ShowDialog(this) == true)
                {
                    VideoPresenterPath = dlg.FileName;
                    try { SettingsService.Instance.SetVideoPresenterPath(VideoPresenterPath); } catch { }
                }
            }
            catch { }
        }

        private void BtnApplyVideoPresenterPath_Click(object sender, RoutedEventArgs e)
        {
            try { SettingsService.Instance.SetVideoPresenterPath(VideoPresenterPath); } catch { }
        }

        private void BtnApplyVideoPresenterArgs_Click(object sender, RoutedEventArgs e)
        {
            try { SettingsService.Instance.SetVideoPresenterArgs(VideoPresenterArgs); } catch { }
        }

        private void BtnResetVideoPresenterDefaults_Click(object sender, RoutedEventArgs e)
        {
            VideoPresenterPath = DefaultVideoPresenterPath;
            VideoPresenterArgs = DefaultVideoPresenterArgs;
            try
            {
                SettingsService.Instance.SetVideoPresenterPath(VideoPresenterPath);
                SettingsService.Instance.SetVideoPresenterArgs(VideoPresenterArgs);
            }
            catch { }
        }

        // 统一底部按钮：应用/确定/取消
        private void ApplyAllSettings()
        {
            try { SettingsService.Instance.SetVideoPresenterPath(VideoPresenterPath); } catch { }
            try { SettingsService.Instance.SetVideoPresenterArgs(VideoPresenterArgs); } catch { }
            try { SettingsService.Instance.SetBackgroundColor(CurrentColor); } catch { }
        }

        private void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            ApplyAllSettings();
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            ApplyAllSettings();
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
