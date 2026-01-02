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
                var l = LocalizationService.Instance;
                var dlg = new OpenFileDialog
                {
                    Title = l.GetString("SettingsWindow_General_Camouflage_BrowseDialog_Title"),
                    Filter = l.GetString("SettingsWindow_General_Camouflage_BrowseDialog_Filter"),
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
            var l = LocalizationService.Instance;
            CamouflageSourcePath = string.Empty;
            CamouflageIconPreview = null;
            CamouflageSourceDisplayName = l.GetString("SettingsWindow_General_Camouflage_NoFileSelected");
            try { SettingsService.Instance.SetCamouflageIconCachePath(string.Empty); } catch { }
        }

        private void BtnCreateCamouflageShortcut_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 默认标题仅作为“未开启伪装/未填写标题”时的兜底
                string defaultTitle = AppDisplayNames.GetAppNameFromSettings();

                var result = CamouflageService.Instance.BuildResult(defaultIcon: null, defaultTitle: defaultTitle);
                string signature = CamouflageService.Instance.GetCamouflageShortcutSettingsSignature();

                bool ok = CamouflageService.Instance.TryUpdateDesktopShortcut(
                    result.Title,
                    result.IconPath,
                    result.Enabled,
                    out var shortcutPath,
                    out var errorMessage);

                if (!ok)
                {
                    var l = LocalizationService.Instance;
                    string message = l.Format(
                        "SettingsWindow_General_Camouflage_CreateShortcut_Failed",
                        errorMessage ?? l.GetString("Common_UnknownError"));
                    MessageBox.Show(
                        message,
                        l.GetString("SettingsWindow_General_Camouflage_MessageBox_Title"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }

                try { SettingsService.Instance.SetCamouflageShortcutLastGeneratedSignature(signature); } catch { }

                var l2 = LocalizationService.Instance;
                MessageBox.Show(
                    l2.Format("SettingsWindow_General_Camouflage_CreateShortcut_Success", shortcutPath),
                    l2.GetString("SettingsWindow_General_Camouflage_MessageBox_Title"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                var l = LocalizationService.Instance;
                MessageBox.Show(
                    l.Format("SettingsWindow_General_Camouflage_CreateShortcut_Failed", ex.Message),
                    l.GetString("SettingsWindow_General_Camouflage_MessageBox_Title"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void RefreshCamouflagePreview(bool buildCache)
        {
            var sourcePath = CamouflageSourcePath;
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                var l = LocalizationService.Instance;
                CamouflageIconPreview = null;
                CamouflageSourceDisplayName = l.GetString("SettingsWindow_General_Camouflage_NoFileSelected");
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
                    var l = LocalizationService.Instance;
                    CamouflageIconPreview = null;
                    CamouflageSourceDisplayName = l.GetString("SettingsWindow_General_Camouflage_IconReadFailed");
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
