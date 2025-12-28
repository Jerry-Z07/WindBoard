using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using WindBoard.Services;

namespace WindBoard
{
    public partial class SettingsWindow
    {
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
                        dlg.InitialDirectory = Path.GetDirectoryName(VideoPresenterPath) ??
                                               Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
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
    }
}

