using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using MaterialDesignThemes.Wpf;
using Microsoft.Win32;
using System.IO;
using WindBoard.Services;

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

        // --- 伪装设置 ---
        private bool _camouflageEnabled;
        private string _camouflageTitle = string.Empty;
        private string _camouflageSourcePath = string.Empty;
        private ImageSource? _camouflageIconPreview;
        private string _camouflageSourceDisplayName = "未选择文件";

        // --- 书写设置 ---
        private bool _strokeThicknessConsistencyEnabled;

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

        // --- 伪装属性（绑定到 XAML，通过 ElementName=SettingsWindowRoot） ---
        public bool CamouflageEnabled
        {
            get => _camouflageEnabled;
            set
            {
                if (_camouflageEnabled != value)
                {
                    _camouflageEnabled = value;
                    OnPropertyChanged();
                    try { SettingsService.Instance.SetCamouflageEnabled(value); }
                    catch (Exception ex) { Debug.WriteLine($"[Settings] Failed to persist camouflage enabled flag: {ex}"); }
                }
            }
        }

        public string CamouflageTitle
        {
            get => _camouflageTitle;
            set
            {
                if (_camouflageTitle != value)
                {
                    _camouflageTitle = value ?? string.Empty;
                    OnPropertyChanged();
                    try { SettingsService.Instance.SetCamouflageTitle(_camouflageTitle); }
                    catch (Exception ex) { Debug.WriteLine($"[Settings] Failed to persist camouflage title: {ex}"); }
                }
            }
        }

        public string CamouflageSourcePath
        {
            get => _camouflageSourcePath;
            set
            {
                if (_camouflageSourcePath != value)
                {
                    _camouflageSourcePath = value ?? string.Empty;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CamouflageSourceDisplayName));
                    try { SettingsService.Instance.SetCamouflageSourcePath(_camouflageSourcePath); }
                    catch (Exception ex) { Debug.WriteLine($"[Settings] Failed to persist camouflage source path: {ex}"); }
                    RefreshCamouflagePreview(buildCache: true);
                }
            }
        }

        public ImageSource? CamouflageIconPreview
        {
            get => _camouflageIconPreview;
            private set
            {
                if (_camouflageIconPreview != value)
                {
                    _camouflageIconPreview = value;
                    OnPropertyChanged();
                }
            }
        }

        public string CamouflageSourceDisplayName
        {
            get => _camouflageSourceDisplayName;
            private set
            {
                if (_camouflageSourceDisplayName != value)
                {
                    _camouflageSourceDisplayName = value;
                    OnPropertyChanged();
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
                    try
                    {
                        SettingsService.Instance.SetStrokeThicknessConsistencyEnabled(value);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"Failed to persist StrokeThicknessConsistencyEnabled setting: {ex}");
                    }
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

            // 初始化“书写设置”
            try
            {
                _strokeThicknessConsistencyEnabled = SettingsService.Instance.GetStrokeThicknessConsistencyEnabled();
            }
            catch
            {
                _strokeThicknessConsistencyEnabled = false;
            }
            OnPropertyChanged(nameof(StrokeThicknessConsistencyEnabled));

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
            try { SettingsService.Instance.SetCamouflageEnabled(CamouflageEnabled); } catch { }
            try { SettingsService.Instance.SetCamouflageTitle(CamouflageTitle); } catch { }
            try { SettingsService.Instance.SetCamouflageSourcePath(CamouflageSourcePath); } catch { }
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

        // --- 基本设置·伪装 ---
        private void ToggleCamouflage_Checked(object sender, RoutedEventArgs e)
        {
            CamouflageEnabled = true;
        }

        private void ToggleCamouflage_Unchecked(object sender, RoutedEventArgs e)
        {
            CamouflageEnabled = false;
        }

        private void BtnBrowseCamouflageIcon_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new OpenFileDialog
                {
                    Title = "选择程序或图标",
                    Filter = "可执行/图标/图片|*.exe;*.ico;*.png;*.jpg;*.jpeg;*.bmp|所有文件|*.*",
                    CheckFileExists = true
                };
                if (!string.IsNullOrWhiteSpace(CamouflageSourcePath))
                {
                    try
                    {
                        dlg.InitialDirectory = Path.GetDirectoryName(CamouflageSourcePath) ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                    }
                    catch { }
                }
                if (dlg.ShowDialog(this) == true)
                {
                    CamouflageSourcePath = dlg.FileName;
                }
            }
            catch
            {
                // ignore dialog errors
            }
        }

        private void BtnClearCamouflageIcon_Click(object sender, RoutedEventArgs e)
        {
            CamouflageSourcePath = string.Empty;
            CamouflageIconPreview = null;
            CamouflageSourceDisplayName = "未选择文件";
            try { SettingsService.Instance.SetCamouflageIconCachePath(string.Empty); } catch { }
        }

        private void RefreshCamouflagePreview(bool buildCache)
        {
            var sourcePath = CamouflageSourcePath;
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                CamouflageIconPreview = null;
                CamouflageSourceDisplayName = "未选择文件";
                if (buildCache)
                {
                    try { SettingsService.Instance.SetCamouflageIconCachePath(string.Empty); } catch { }
                }
                return;
            }

            CamouflageSourceDisplayName = Path.GetFileName(sourcePath);

            if (buildCache)
            {
                if (CamouflageService.Instance.TryBuildIconCache(sourcePath, out var cachePath, out var preview))
                {
                    CamouflageIconPreview = preview;
                    try { SettingsService.Instance.SetCamouflageIconCachePath(cachePath); } catch { }
                }
                else
                {
                    CamouflageIconPreview = null;
                    CamouflageSourceDisplayName = "无法读取图标";
                    try { SettingsService.Instance.SetCamouflageIconCachePath(string.Empty); } catch { }
                }
                return;
            }

            // 不重建缓存时，尝试从已有缓存加载预览
            var cachedPath = SettingsService.Instance.GetCamouflageIconCachePath();
            if (!string.IsNullOrWhiteSpace(cachedPath) && File.Exists(cachedPath))
            {
                try
                {
                    CamouflageIconPreview = CamouflageService.Instance.LoadIconFromFile(cachedPath);
                }
                catch
                {
                    CamouflageIconPreview = null;
                }
            }
            else
            {
                // 尝试直接用源文件作为预览（不写缓存）
                try
                {
                    CamouflageIconPreview = CamouflageService.Instance.LoadIconFromFile(sourcePath);
                }
                catch
                {
                    CamouflageIconPreview = null;
                }
            }
        }
    }
}
