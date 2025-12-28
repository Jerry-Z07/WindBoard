using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using WindBoard.Services;

namespace WindBoard
{
    public partial class SettingsWindow
    {
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
                        dlg.InitialDirectory = Path.GetDirectoryName(CamouflageSourcePath) ??
                                               Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
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

