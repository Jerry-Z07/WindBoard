using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using WindBoard.Localization;

namespace WindBoard.Settings.Pages
{
    public sealed partial class CamouflageSettingsPage : Page
    {
        private bool _isSyncingFromSettings;
        private DispatcherQueueTimer? _iconBuildTimer;
        private int _iconPreviewRequestId;

        public CamouflageSettingsPage()
        {
            InitializeComponent();
            InitializeIconBuildTimer();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void InitializeIconBuildTimer()
        {
            // 图标生成涉及文件 IO + 图像解码，避免每次击键都触发：
            // - 文本框变更只做“调度”
            // - 用户停顿一小段时间后再生成缓存与预览
            _iconBuildTimer = DispatcherQueue.CreateTimer();
            _iconBuildTimer.Interval = TimeSpan.FromMilliseconds(450);
            _iconBuildTimer.IsRepeating = false;
            _iconBuildTimer.Tick += async (_, _) => await RebuildIconCacheAndPreviewAsync();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            SyncUiFromSettings(rebuildIconCacheIfNeeded: false);
            AppSettingsService.Instance.Changed += OnSettingsChanged;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            AppSettingsService.Instance.Changed -= OnSettingsChanged;
            _iconBuildTimer?.Stop();

            // 页面离开时尽量落盘，避免防抖未触发导致设置丢失。
            try
            {
                AppSettingsService.Instance.SaveAsync().GetAwaiter().GetResult();
            }
            catch
            {
                // 忽略保存失败：不阻断设置窗口关闭流程
            }
        }

        private void OnSettingsChanged(object? sender, EventArgs e)
        {
            if (!DispatcherQueue.TryEnqueue(() => SyncUiFromSettings(rebuildIconCacheIfNeeded: false)))
            {
                SyncUiFromSettings(rebuildIconCacheIfNeeded: false);
            }
        }

        private void SyncUiFromSettings(bool rebuildIconCacheIfNeeded)
        {
            CamouflageSettingsSnapshot snapshot = AppSettingsService.Instance.GetCamouflageSettingsSnapshot();

            _isSyncingFromSettings = true;
            try
            {
                EnabledToggleSwitch.IsOn = snapshot.Enabled;
                UpdateOptionsVisibility(snapshot.Enabled);

                if (!string.Equals(TitleTextBox.Text, snapshot.Title, StringComparison.Ordinal))
                {
                    TitleTextBox.Text = snapshot.Title;
                }

                if (!string.Equals(IconSourcePathTextBox.Text, snapshot.SourcePath, StringComparison.Ordinal))
                {
                    IconSourcePathTextBox.Text = snapshot.SourcePath;
                }
            }
            finally
            {
                _isSyncingFromSettings = false;
            }

            _ = SyncIconPreviewFromSettingsAsync(snapshot, rebuildIconCacheIfNeeded);
        }

        private async Task SyncIconPreviewFromSettingsAsync(CamouflageSettingsSnapshot snapshot, bool rebuildIconCacheIfNeeded)
        {
            int requestId = ++_iconPreviewRequestId;

            try
            {
                string sourcePath = (snapshot.SourcePath ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                {
                    SetIconPreview(null, L10n.Get("Settings_Camouflage_NoFileSelected"), showError: false, message: null);
                    return;
                }

                SetIconDisplayName(Path.GetFileName(sourcePath));

                string cachePath = (snapshot.IconCachePath ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(cachePath) && File.Exists(cachePath))
                {
                    byte[]? bytes = TryReadSmallFileBytes(cachePath, maxBytes: 2 * 1024 * 1024);
                    ImageSource? source = bytes is null ? null : await TryDecodeImageSourceAsync(bytes);
                    if (requestId != _iconPreviewRequestId)
                    {
                        return;
                    }

                    SetIconPreview(source, displayName: null, showError: false, message: null);
                    return;
                }

                if (!rebuildIconCacheIfNeeded)
                {
                    // 不重建缓存时，尝试直接从源文件加载预览（exe 除外）。
                    string ext = (Path.GetExtension(sourcePath) ?? string.Empty).ToLowerInvariant();
                    if (!string.Equals(ext, ".exe", StringComparison.Ordinal))
                    {
                        byte[]? bytes = TryReadSmallFileBytes(sourcePath, maxBytes: 2 * 1024 * 1024);
                        ImageSource? source = bytes is null ? null : await TryDecodeImageSourceAsync(bytes);
                        if (requestId != _iconPreviewRequestId)
                        {
                            return;
                        }

                        SetIconPreview(source, displayName: null, showError: false, message: null);
                        return;
                    }

                    SetIconPreview(null, displayName: null, showError: false, message: null);
                    return;
                }

                await RebuildIconCacheAndPreviewAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Settings/Camouflage] 同步图标预览失败：{ex}");
            }
        }

        private void OnEnabledToggled(object sender, RoutedEventArgs e)
        {
            if (_isSyncingFromSettings)
            {
                return;
            }

            bool enabled = EnabledToggleSwitch.IsOn;
            UpdateOptionsVisibility(enabled);
            AppSettingsService.Instance.Update(s => s.General.Camouflage.Enabled = enabled);
        }

        private void UpdateOptionsVisibility(bool enabled)
        {
            OptionsPanel.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnTitleTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isSyncingFromSettings)
            {
                return;
            }

            string title = TitleTextBox.Text ?? string.Empty;
            AppSettingsService.Instance.Update(s => s.General.Camouflage.Title = title);
        }

        private void OnIconSourcePathTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isSyncingFromSettings)
            {
                return;
            }

            string sourcePath = (IconSourcePathTextBox.Text ?? string.Empty).Trim();

            // 先写入来源路径，并清空旧缓存路径，避免“缓存仍指向旧图标”导致主窗口继续使用旧图标。
            AppSettingsService.Instance.Update(s =>
            {
                s.General.Camouflage.SourcePath = sourcePath;
                s.General.Camouflage.IconCachePath = string.Empty;
            });

            _iconBuildTimer?.Stop();

            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                SetIconPreview(null, L10n.Get("Settings_Camouflage_NoFileSelected"), showError: false, message: null);
                return;
            }

            // 立即更新显示名与预览占位（真正的缓存与预览在防抖后处理）。
            if (File.Exists(sourcePath))
            {
                SetIconDisplayName(Path.GetFileName(sourcePath));
            }
            else
            {
                SetIconDisplayName(L10n.Get("Settings_Camouflage_NoFileSelected"));
            }

            SetIconPreview(null, displayName: null, showError: false, message: null);

            _iconBuildTimer?.Start();
        }

        private async void OnBrowseIconClicked(object sender, RoutedEventArgs e)
        {
            try
            {
                Debug.WriteLine("[Settings/Camouflage] 打开图标/程序选择对话框…");
                IntPtr hwnd = TryGetHostWindowHandle();

                if (hwnd == IntPtr.Zero)
                {
                    Debug.WriteLine("[Settings/Camouflage] 无法获取宿主窗口句柄，已取消打开文件选择器。");
                    SetIconPreview(
                        null,
                        displayName: null,
                        showError: true,
                        message: L10n.Get("Settings_Camouflage_FilePicker_NoWindowHandle"));
                    return;
                }

                var picker = new FileOpenPicker();
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

                picker.FileTypeFilter.Clear();
                picker.FileTypeFilter.Add(".exe");
                picker.FileTypeFilter.Add(".ico");
                picker.FileTypeFilter.Add(".png");
                picker.FileTypeFilter.Add(".jpg");
                picker.FileTypeFilter.Add(".jpeg");
                picker.FileTypeFilter.Add(".bmp");
                picker.FileTypeFilter.Add(".gif");

                StorageFile? file = await picker.PickSingleFileAsync();
                if (file is null)
                {
                    return;
                }

                string? path = file.Path;
                path = (path ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(path))
                {
                    Debug.WriteLine("[Settings/Camouflage] 文件对话框返回的路径为空。");
                    SetIconPreview(
                        null,
                        displayName: null,
                        showError: true,
                        message: L10n.Get("Settings_Camouflage_FilePicker_PathEmpty"));
                    return;
                }

                Debug.WriteLine($"[Settings/Camouflage] 已选择图标来源：'{path}', exists={File.Exists(path)}");

                // 关键：不要只依赖 TextChanged 事件来落盘。
                // 在某些 WinUI 运行时组合下，代码里设置 Text 可能不会触发 TextChanged，
                // 导致 SourcePath 未更新，随后 RebuildIconCacheAndPreviewAsync 更新 IconCachePath 时会把 UI 同步回旧值。
                AppSettingsService.Instance.Update(s =>
                {
                    s.General.Camouflage.SourcePath = path;
                    s.General.Camouflage.IconCachePath = string.Empty;
                });

                _isSyncingFromSettings = true;
                try
                {
                    IconSourcePathTextBox.Text = path;
                }
                finally
                {
                    _isSyncingFromSettings = false;
                }

                // 浏览选择属于“明确动作”，这里立即生成一次图标缓存与预览。
                _iconBuildTimer?.Stop();
                await RebuildIconCacheAndPreviewAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Settings/Camouflage] 打开文件选择器失败：{ex}");
                SetIconPreview(
                    null,
                    displayName: null,
                    showError: true,
                    message: L10n.Format("Settings_Camouflage_FilePicker_Failed_Fmt", ex.Message));

                await ShowMessageDialogAsync(
                    L10n.Get("Settings_Camouflage_Dialog_Title"),
                    L10n.Format("Settings_Camouflage_FilePicker_Failed_Fmt", ex.Message),
                    isError: true);
            }
        }

        private void OnClearIconClicked(object sender, RoutedEventArgs e)
        {
            _iconBuildTimer?.Stop();
            IconSourcePathTextBox.Text = string.Empty;
        }

        private async void OnCreateShortcutClicked(object sender, RoutedEventArgs e)
        {
            try
            {
                string defaultTitle = L10n.Get("MainWindow_Title");

                // BuildResult 可能会触发“缓存重建并写回设置”，因此这里在调用后再取一次签名快照。
                CamouflageSettingsSnapshot before = AppSettingsService.Instance.GetCamouflageSettingsSnapshot();
                CamouflageResult result = CamouflageService.Instance.BuildResult(before, defaultTitle);

                CamouflageSettingsSnapshot after = AppSettingsService.Instance.GetCamouflageSettingsSnapshot();
                string signature = CamouflageService.Instance.GetCamouflageShortcutSettingsSignature(after);

                bool ok = CamouflageService.Instance.TryUpdateDesktopShortcut(
                    result.Title,
                    result.IconPath,
                    result.Enabled,
                    after.ShortcutLastGeneratedPath,
                    out string shortcutPath,
                    out string? errorMessage);

                if (!ok)
                {
                    await ShowMessageDialogAsync(
                        L10n.Get("Settings_Camouflage_Dialog_Title"),
                        L10n.Format("Settings_Camouflage_CreateShortcut_Failed_Fmt", errorMessage ?? L10n.Get("Common_UnknownError")),
                        isError: true);
                    return;
                }

                AppSettingsService.Instance.Update(s =>
                {
                    s.General.Camouflage.ShortcutLastGeneratedSignature = signature;
                    s.General.Camouflage.ShortcutLastGeneratedPath = shortcutPath;
                });

                await ShowMessageDialogAsync(
                    L10n.Get("Settings_Camouflage_Dialog_Title"),
                    L10n.Format("Settings_Camouflage_CreateShortcut_Success_Fmt", shortcutPath),
                    isError: false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Settings/Camouflage] 生成桌面快捷方式异常：{ex}");
                await ShowMessageDialogAsync(
                    L10n.Get("Settings_Camouflage_Dialog_Title"),
                    L10n.Format("Settings_Camouflage_CreateShortcut_Failed_Fmt", ex.Message),
                    isError: true);
            }
        }

        private async Task RebuildIconCacheAndPreviewAsync()
        {
            _iconBuildTimer?.Stop();

            int requestId = ++_iconPreviewRequestId;

            string sourcePath = (IconSourcePathTextBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                SetIconPreview(null, L10n.Get("Settings_Camouflage_NoFileSelected"), showError: false, message: null);
                AppSettingsService.Instance.Update(s => s.General.Camouflage.IconCachePath = string.Empty);
                return;
            }

            SetIconDisplayName(Path.GetFileName(sourcePath));

            bool ok = CamouflageService.Instance.TryBuildCamouflageIconCache(
                sourcePath,
                out string cachePath,
                out byte[]? previewBytes,
                out string? errorMessage);

            if (!ok)
            {
                AppSettingsService.Instance.Update(s => s.General.Camouflage.IconCachePath = string.Empty);
                SetIconPreview(
                    null,
                    L10n.Get("Settings_Camouflage_IconReadFailed"),
                    showError: true,
                    message: L10n.Format("Settings_Camouflage_IconReadFailed_Fmt", errorMessage ?? L10n.Get("Common_UnknownError")));
                return;
            }

            // 落盘缓存路径：供主窗口/快捷方式复用。
            AppSettingsService.Instance.Update(s => s.General.Camouflage.IconCachePath = cachePath);

            byte[]? bytes = previewBytes;
            if (bytes is null && File.Exists(cachePath))
            {
                bytes = TryReadSmallFileBytes(cachePath, maxBytes: 2 * 1024 * 1024);
            }

            ImageSource? preview = bytes is null ? null : await TryDecodeImageSourceAsync(bytes);

            if (requestId != _iconPreviewRequestId)
            {
                return;
            }

            SetIconPreview(preview, displayName: null, showError: false, message: null);
        }

        private async Task<ImageSource?> TryDecodeImageSourceAsync(byte[] bytes)
        {
            try
            {
                using var stream = new InMemoryRandomAccessStream();
                await stream.WriteAsync(bytes.AsBuffer());
                stream.Seek(0);

                BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);
                SoftwareBitmap bitmap = await decoder.GetSoftwareBitmapAsync();
                if (bitmap.BitmapPixelFormat != BitmapPixelFormat.Bgra8
                    || bitmap.BitmapAlphaMode != BitmapAlphaMode.Premultiplied)
                {
                    SoftwareBitmap converted = SoftwareBitmap.Convert(
                        bitmap,
                        BitmapPixelFormat.Bgra8,
                        BitmapAlphaMode.Premultiplied);
                    bitmap.Dispose();
                    bitmap = converted;
                }

                var source = new SoftwareBitmapSource();
                await source.SetBitmapAsync(bitmap);
                bitmap.Dispose();
                return source;
            }
            catch
            {
                return null;
            }
        }

        private static byte[]? TryReadSmallFileBytes(string path, int maxBytes)
        {
            try
            {
                var info = new FileInfo(path);
                if (info.Length <= 0 || info.Length > maxBytes)
                {
                    return null;
                }

                return File.ReadAllBytes(path);
            }
            catch
            {
                return null;
            }
        }

        private void SetIconDisplayName(string? displayName)
        {
            if (!string.IsNullOrWhiteSpace(displayName))
            {
                IconSourceDisplayNameTextBlock.Text = displayName;
            }
        }

        private void SetIconPreview(ImageSource? preview, string? displayName, bool showError, string? message)
        {
            if (!string.IsNullOrWhiteSpace(displayName))
            {
                IconSourceDisplayNameTextBlock.Text = displayName;
            }

            IconPreviewImage.Source = preview;

            IconStatusInfoBar.IsOpen = showError;
            if (showError)
            {
                IconStatusInfoBar.Severity = InfoBarSeverity.Error;
                IconStatusInfoBar.Message = message ?? L10n.Get("Settings_Camouflage_IconReadFailed");
            }
        }

        private async Task ShowMessageDialogAsync(string title, string message, bool isError)
        {
            if (XamlRoot is null)
            {
                Debug.WriteLine($"[Settings/Camouflage] 无法显示对话框（XamlRoot 为空）：{title} - {message}");
                return;
            }

            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = L10n.Get("Common_OK"),
                XamlRoot = XamlRoot,
            };

            if (isError)
            {
                dialog.DefaultButton = ContentDialogButton.Close;
            }

            await dialog.ShowAsync();
        }

        private static IntPtr TryGetHostWindowHandle()
        {
            try
            {
                // WinUI 3 桌面端 Page 无法直接拿到宿主 Window，这里用 SettingsWindow 的静态引用。
                if (SettingsWindow.Active is not null)
                {
                    return SettingsWindow.Active.Hwnd;
                }
            }
            catch
            {
                return IntPtr.Zero;
            }

            return IntPtr.Zero;
        }
    }
}
