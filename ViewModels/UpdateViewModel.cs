using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using WindBoard.Models;
using WindBoard.Models.Update;
using WindBoard.Services;

namespace WindBoard.ViewModels
{
    public sealed class UpdateViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly UpdateService _updateService;
        private readonly SettingsService _settingsService;
        private readonly LocalizationService _localizationService;

        private InstallEnvironment? _installEnvironment;
        private UpdateInfo? _updateInfo;
        private UpdateAsset? _selectedAsset;
        private string? _downloadedPath;

        private string _headerTitle = string.Empty;
        private string _statusText = string.Empty;
        private string _latestVersion = "-";
        private string _releaseDate = "-";
        private string _minSystemVersion = "-";
        private string _changelogText = string.Empty;

        private bool _isChecking;
        private bool _isDownloading;
        private bool _downloadProgressVisible;
        private double _downloadProgressPercent;
        private string _downloadProgressText = string.Empty;

        private CancellationTokenSource? _downloadCts;

        public event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler? RequestClose;

        public UpdateViewModel()
            : this(UpdateService.Instance, SettingsService.Instance, LocalizationService.Instance)
        {
        }

        internal UpdateViewModel(UpdateService updateService, SettingsService settingsService, LocalizationService localizationService)
        {
            _updateService = updateService ?? throw new ArgumentNullException(nameof(updateService));
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));

            HeaderTitle = _localizationService.GetString("UpdateWindow_Header_Checking");
            StatusText = _localizationService.GetString("UpdateWindow_Status_Checking");
        }

        public string HeaderTitle
        {
            get => _headerTitle;
            private set => SetField(ref _headerTitle, value);
        }

        public string StatusText
        {
            get => _statusText;
            private set => SetField(ref _statusText, value);
        }

        public string CurrentVersion => AppVersionInfo.Version;

        public string LatestVersion
        {
            get => _latestVersion;
            private set => SetField(ref _latestVersion, value);
        }

        public string ReleaseDate
        {
            get => _releaseDate;
            private set => SetField(ref _releaseDate, value);
        }

        public string MinSystemVersion
        {
            get => _minSystemVersion;
            private set => SetField(ref _minSystemVersion, value);
        }

        public string ChangelogText
        {
            get => _changelogText;
            private set => SetField(ref _changelogText, value);
        }

        public string SelectedAssetDisplay => _selectedAsset?.FileName ?? "-";

        public string InstallMethodHint
        {
            get
            {
                if (_installEnvironment == null)
                {
                    return string.Empty;
                }

                return _installEnvironment.InstallMode switch
                {
                    InstallMode.InstallerPerMachine => _localizationService.GetString("UpdateWindow_Download_Hint_Installer"),
                    InstallMode.Portable when _installEnvironment.DeploymentRuntime == DeploymentRuntime.SelfContained => _localizationService.GetString("UpdateWindow_Download_Hint_Portable_SelfContained"),
                    InstallMode.Portable when _installEnvironment.DeploymentRuntime == DeploymentRuntime.FrameworkDependent => _localizationService.GetString("UpdateWindow_Download_Hint_Portable_FrameworkDependent"),
                    InstallMode.Portable => _localizationService.GetString("UpdateWindow_Download_Hint_Portable"),
                    _ => _localizationService.GetString("UpdateWindow_Download_Hint_Unknown")
                };
            }
        }

        public bool DownloadProgressVisible
        {
            get => _downloadProgressVisible;
            private set => SetField(ref _downloadProgressVisible, value);
        }

        public double DownloadProgressPercent
        {
            get => _downloadProgressPercent;
            private set => SetField(ref _downloadProgressPercent, value);
        }

        public string DownloadProgressText
        {
            get => _downloadProgressText;
            private set => SetField(ref _downloadProgressText, value);
        }

        public bool CanDownload => !_isChecking && !_isDownloading && _selectedAsset != null;

        public bool CanSkipVersion => !_isChecking && !_isDownloading && _updateInfo != null;

        public bool CanOpenDownloadedFolder => !_isChecking && !_isDownloading && !string.IsNullOrWhiteSpace(_downloadedPath);

        public bool CanInstallDownloaded => !_isChecking && !_isDownloading && _selectedAsset != null && UpdateService.IsInstallerAsset(_selectedAsset) && !string.IsNullOrWhiteSpace(_downloadedPath);

        public async Task InitializeAsync()
        {
            _isChecking = true;
            NotifyComputed();

            ClearView();
            HeaderTitle = _localizationService.GetString("UpdateWindow_Header_Checking");
            StatusText = _localizationService.GetString("UpdateWindow_Status_Checking");

            UpdateCheckResult result;
            try
            {
                result = await _updateService.CheckForUpdatesAsync(ignoreSkippedVersion: true).ConfigureAwait(true);
                try { _settingsService.SetLastUpdateCheckTime(DateTime.UtcNow); } catch { }
            }
            catch (Exception ex)
            {
                result = new UpdateCheckResult { UpdateAvailable = false, ErrorMessage = ex.Message };
            }
            finally
            {
                _isChecking = false;
                NotifyComputed();
            }

            if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
            {
                HeaderTitle = _localizationService.GetString("UpdateWindow_Header_Error");
                StatusText = string.Format(CultureInfo.CurrentUICulture, _localizationService.GetString("UpdateWindow_Status_Error_Format"), result.ErrorMessage);
                return;
            }

            if (result.LatestVersion == null)
            {
                HeaderTitle = _localizationService.GetString("UpdateWindow_Header_Error");
                StatusText = _localizationService.GetString("UpdateWindow_Status_NoInfo");
                return;
            }

            _updateInfo = result.LatestVersion;
            _installEnvironment = InstallModeDetector.Detect();
            OnPropertyChanged(nameof(InstallMethodHint));

            LatestVersion = !string.IsNullOrWhiteSpace(_updateInfo.VersionName) ? _updateInfo.VersionName : _updateInfo.Version;
            ReleaseDate = _updateInfo.ReleaseDate != default
                ? _updateInfo.ReleaseDate.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentUICulture)
                : "-";
            MinSystemVersion = string.IsNullOrWhiteSpace(_updateInfo.MinSystemVersion) ? "-" : _updateInfo.MinSystemVersion;
            ChangelogText = ResolveLocalizedChangelog(_updateInfo);

            _selectedAsset = _updateService.SelectAssetForCurrentInstallation(_updateInfo, _installEnvironment);
            OnPropertyChanged(nameof(SelectedAssetDisplay));
            NotifyComputed();

            if (result.UpdateAvailable)
            {
                HeaderTitle = _localizationService.GetString("UpdateWindow_Header_NewVersion");
                StatusText = _localizationService.GetString("UpdateWindow_Status_UpdateAvailable");
            }
            else
            {
                HeaderTitle = _localizationService.GetString("UpdateWindow_Header_UpToDate");
                StatusText = _localizationService.GetString("UpdateWindow_Status_UpToDate");
            }
        }

        public async Task DownloadAsync()
        {
            if (_selectedAsset == null)
            {
                return;
            }

            _downloadCts?.Cancel();
            _downloadCts?.Dispose();
            _downloadCts = new CancellationTokenSource();

            _isDownloading = true;
            DownloadProgressVisible = true;
            DownloadProgressPercent = 0;
            DownloadProgressText = _localizationService.GetString("UpdateWindow_Download_Status_Starting");
            NotifyComputed();

            try
            {
                var progress = new Progress<DownloadProgress>(p =>
                {
                    double percent = p.ProgressPercentage;
                    if (double.IsNaN(percent) || double.IsInfinity(percent)) percent = 0;
                    DownloadProgressPercent = percent;
                    DownloadProgressText = string.Format(
                        CultureInfo.CurrentUICulture,
                        _localizationService.GetString("UpdateWindow_Download_Status_Progress_Format"),
                        FormatBytes(p.BytesReceived),
                        p.TotalBytes > 0 ? FormatBytes(p.TotalBytes) : "-");
                });

                string path = await _updateService.DownloadUpdateAsync(_selectedAsset, progress, _downloadCts.Token).ConfigureAwait(true);
                _downloadedPath = path;
                OnPropertyChanged(nameof(CanOpenDownloadedFolder));
                DownloadProgressPercent = 100;
                DownloadProgressText = UpdateService.IsInstallerAsset(_selectedAsset)
                    ? _localizationService.GetString("UpdateWindow_Download_Status_Done_Installer")
                    : _localizationService.GetString("UpdateWindow_Download_Status_Done_Zip");
            }
            catch (OperationCanceledException)
            {
                DownloadProgressText = _localizationService.GetString("UpdateWindow_Download_Status_Canceled");
            }
            catch (Exception ex)
            {
                DownloadProgressText = string.Format(CultureInfo.CurrentUICulture, _localizationService.GetString("UpdateWindow_Download_Status_Failed_Format"), ex.Message);
            }
            finally
            {
                _isDownloading = false;
                NotifyComputed();
            }
        }

        public void InstallDownloaded()
        {
            if (_selectedAsset == null || !UpdateService.IsInstallerAsset(_selectedAsset))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(_downloadedPath) || !File.Exists(_downloadedPath))
            {
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(_downloadedPath)
                {
                    UseShellExecute = true,
                    Verb = "runas"
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Update] Failed to launch installer: {ex}");
            }
        }

        public void OpenDownloadedFolder()
        {
            if (string.IsNullOrWhiteSpace(_downloadedPath))
            {
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_downloadedPath}\"")
                {
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Update] Failed to open downloaded file location: {ex}");
            }
        }

        public void OpenReleasePage()
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://github.com/Jerry-Z07/WindBoard/releases/latest")
                {
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Update] Failed to open release page: {ex}");
            }
        }

        public void SkipThisVersion()
        {
            if (_updateInfo == null)
            {
                return;
            }

            try { _settingsService.SetSkippedUpdateVersion(_updateInfo.Version); } catch { }
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            try { _downloadCts?.Cancel(); } catch { }
            _downloadCts?.Dispose();
            _downloadCts = null;
        }

        private void ClearView()
        {
            _updateInfo = null;
            _installEnvironment = null;
            _selectedAsset = null;
            _downloadedPath = null;

            DownloadProgressVisible = false;
            DownloadProgressPercent = 0;
            DownloadProgressText = string.Empty;
            LatestVersion = "-";
            ReleaseDate = "-";
            MinSystemVersion = "-";
            ChangelogText = string.Empty;

            OnPropertyChanged(nameof(SelectedAssetDisplay));
            OnPropertyChanged(nameof(InstallMethodHint));
        }

        private string ResolveLocalizedChangelog(UpdateInfo updateInfo)
        {
            if (updateInfo.Changelog == null || updateInfo.Changelog.Count == 0)
            {
                return string.Empty;
            }

            string preferredKey = _localizationService.CurrentLanguage == AppLanguage.English
                ? "en-US"
                : "zh-CN";

            if (updateInfo.Changelog.TryGetValue(preferredKey, out string? value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            foreach ((string _, string text) in updateInfo.Changelog)
            {
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }

            return string.Empty;
        }

        private void NotifyComputed()
        {
            OnPropertyChanged(nameof(CanDownload));
            OnPropertyChanged(nameof(CanSkipVersion));
            OnPropertyChanged(nameof(CanOpenDownloadedFolder));
            OnPropertyChanged(nameof(CanInstallDownloaded));
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value))
            {
                return;
            }

            field = value;
            OnPropertyChanged(propertyName);
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "0 B";
            string[] suffix = new[] { "B", "KB", "MB", "GB", "TB" };
            double value = bytes;
            int i = 0;
            while (value >= 1024 && i < suffix.Length - 1)
            {
                value /= 1024;
                i++;
            }
            return string.Format(CultureInfo.InvariantCulture, "{0:0.##} {1}", value, suffix[i]);
        }
    }
}

