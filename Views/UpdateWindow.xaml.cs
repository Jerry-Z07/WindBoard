using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using WindBoard.Models;
using WindBoard.Models.Update;
using WindBoard.Services;

namespace WindBoard
{
    public partial class UpdateWindow : Window, INotifyPropertyChanged
    {
        private UpdateInfo? _updateInfo;
        private string _headerTitle = string.Empty;
        private string _statusText = string.Empty;
        private string _latestVersion = "-";
        private string _releaseDate = "-";
        private string _minSystemVersion = "-";
        private string _changelogText = string.Empty;
        private UpdateAsset? _selectedAsset;
        private bool _isChecking;
        private bool _isDownloading;
        private double _downloadProgressPercent;
        private string _downloadProgressText = string.Empty;
        private bool _downloadProgressVisible;
        private string? _downloadedPath;
        private CancellationTokenSource? _downloadCts;
        private InstallEnvironment? _installEnvironment;

        public ObservableCollection<UpdateAsset> Assets { get; } = new();

        public UpdateAsset? SelectedAsset
        {
            get => _selectedAsset;
            set
            {
                if (!ReferenceEquals(_selectedAsset, value))
                {
                    _selectedAsset = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(SelectedAssetDisplay));
                    OnPropertyChanged(nameof(InstallMethodHint));
                    OnPropertyChanged(nameof(CanInstallDownloaded));
                    OnPropertyChanged(nameof(CanDownload));
                }
            }
        }

        public string SelectedAssetDisplay => SelectedAsset?.FileName ?? "-";

        public string InstallMethodHint
        {
            get
            {
                if (_installEnvironment == null)
                {
                    return string.Empty;
                }

                var l = LocalizationService.Instance;
                return _installEnvironment.InstallMode switch
                {
                    InstallMode.InstallerPerMachine => l.GetString("UpdateWindow_Download_Hint_Installer"),
                    InstallMode.Portable when _installEnvironment.DeploymentRuntime == DeploymentRuntime.SelfContained => l.GetString("UpdateWindow_Download_Hint_Portable_SelfContained"),
                    InstallMode.Portable when _installEnvironment.DeploymentRuntime == DeploymentRuntime.FrameworkDependent => l.GetString("UpdateWindow_Download_Hint_Portable_FrameworkDependent"),
                    InstallMode.Portable => l.GetString("UpdateWindow_Download_Hint_Portable"),
                    _ => l.GetString("UpdateWindow_Download_Hint_Unknown")
                };
            }
        }

        public string HeaderTitle
        {
            get => _headerTitle;
            private set
            {
                if (_headerTitle != value)
                {
                    _headerTitle = value;
                    OnPropertyChanged();
                }
            }
        }

        public string CurrentVersion => AppVersionInfo.Version;

        public string LatestVersion
        {
            get => _latestVersion;
            private set
            {
                if (_latestVersion != value)
                {
                    _latestVersion = value;
                    OnPropertyChanged();
                }
            }
        }

        public string ReleaseDate
        {
            get => _releaseDate;
            private set
            {
                if (_releaseDate != value)
                {
                    _releaseDate = value;
                    OnPropertyChanged();
                }
            }
        }

        public string MinSystemVersion
        {
            get => _minSystemVersion;
            private set
            {
                if (_minSystemVersion != value)
                {
                    _minSystemVersion = value;
                    OnPropertyChanged();
                }
            }
        }

        public string StatusText
        {
            get => _statusText;
            private set
            {
                if (_statusText != value)
                {
                    _statusText = value;
                    OnPropertyChanged();
                }
            }
        }

        public string ChangelogText
        {
            get => _changelogText;
            private set
            {
                if (_changelogText != value)
                {
                    _changelogText = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool CanDownload => !_isChecking && !_isDownloading && SelectedAsset != null;

        public bool CanSelectAsset => !_isChecking && !_isDownloading && Assets.Count > 0;

        public bool CanSkipVersion => !_isChecking && !_isDownloading && _updateInfo != null;

        public bool CanOpenDownloadedFolder => !_isChecking && !_isDownloading && !string.IsNullOrWhiteSpace(_downloadedPath);

        public bool CanInstallDownloaded => !_isChecking && !_isDownloading && SelectedAsset != null && IsInstallerAsset(SelectedAsset) && !string.IsNullOrWhiteSpace(_downloadedPath);

        public double DownloadProgressPercent
        {
            get => _downloadProgressPercent;
            private set
            {
                if (Math.Abs(_downloadProgressPercent - value) > 0.001)
                {
                    _downloadProgressPercent = value;
                    OnPropertyChanged();
                }
            }
        }

        public string DownloadProgressText
        {
            get => _downloadProgressText;
            private set
            {
                if (_downloadProgressText != value)
                {
                    _downloadProgressText = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool DownloadProgressVisible
        {
            get => _downloadProgressVisible;
            private set
            {
                if (_downloadProgressVisible != value)
                {
                    _downloadProgressVisible = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public UpdateWindow()
        {
            InitializeComponent();
            DataContext = this;

            HeaderTitle = LocalizationService.Instance.GetString("UpdateWindow_Header_Checking");
            StatusText = LocalizationService.Instance.GetString("UpdateWindow_Status_Checking");
            Loaded += UpdateWindow_Loaded;
            Closed += UpdateWindow_Closed;
        }

        private async void UpdateWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await CheckAndRenderAsync().ConfigureAwait(true);
        }

        private void UpdateWindow_Closed(object? sender, EventArgs e)
        {
            try { _downloadCts?.Cancel(); } catch { }
            _downloadCts?.Dispose();
            _downloadCts = null;
        }

        private async Task CheckAndRenderAsync()
        {
            _isChecking = true;
            OnPropertyChanged(nameof(CanDownload));
            OnPropertyChanged(nameof(CanSelectAsset));
            OnPropertyChanged(nameof(CanSkipVersion));
            OnPropertyChanged(nameof(CanOpenDownloadedFolder));
            OnPropertyChanged(nameof(CanInstallDownloaded));

            Assets.Clear();
            SelectedAsset = null;
            _updateInfo = null;
            _installEnvironment = null;
            OnPropertyChanged(nameof(InstallMethodHint));
            _downloadedPath = null;
            DownloadProgressVisible = false;
            DownloadProgressPercent = 0;
            DownloadProgressText = string.Empty;
            LatestVersion = "-";
            ReleaseDate = "-";
            MinSystemVersion = "-";
            ChangelogText = string.Empty;

            HeaderTitle = LocalizationService.Instance.GetString("UpdateWindow_Header_Checking");
            StatusText = LocalizationService.Instance.GetString("UpdateWindow_Status_Checking");

            UpdateCheckResult result;
            try
            {
                result = await UpdateService.Instance.CheckForUpdatesAsync(ignoreSkippedVersion: true).ConfigureAwait(true);
                try { SettingsService.Instance.SetLastUpdateCheckTime(DateTime.UtcNow); } catch { }
            }
            catch (Exception ex)
            {
                result = new UpdateCheckResult { UpdateAvailable = false, ErrorMessage = ex.Message };
            }

            _isChecking = false;
            OnPropertyChanged(nameof(CanDownload));
            OnPropertyChanged(nameof(CanSelectAsset));
            OnPropertyChanged(nameof(CanSkipVersion));
            OnPropertyChanged(nameof(CanOpenDownloadedFolder));
            OnPropertyChanged(nameof(CanInstallDownloaded));

            if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
            {
                HeaderTitle = LocalizationService.Instance.GetString("UpdateWindow_Header_Error");
                StatusText = string.Format(CultureInfo.CurrentUICulture, LocalizationService.Instance.GetString("UpdateWindow_Status_Error_Format"), result.ErrorMessage);
                return;
            }

            if (result.LatestVersion == null)
            {
                HeaderTitle = LocalizationService.Instance.GetString("UpdateWindow_Header_Error");
                StatusText = LocalizationService.Instance.GetString("UpdateWindow_Status_NoInfo");
                return;
            }

            _updateInfo = result.LatestVersion;
            _installEnvironment = InstallModeDetector.Detect();
            OnPropertyChanged(nameof(InstallMethodHint));

            string latestText = !string.IsNullOrWhiteSpace(_updateInfo.VersionName)
                ? _updateInfo.VersionName
                : _updateInfo.Version;

            LatestVersion = latestText;
            ReleaseDate = _updateInfo.ReleaseDate != default
                ? _updateInfo.ReleaseDate.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentUICulture)
                : "-";
            MinSystemVersion = string.IsNullOrWhiteSpace(_updateInfo.MinSystemVersion) ? "-" : _updateInfo.MinSystemVersion;

            ChangelogText = ResolveLocalizedChangelog(_updateInfo);

            Assets.Clear();
            foreach (UpdateAsset asset in _updateInfo.Assets)
            {
                Assets.Add(asset);
            }

            SelectedAsset = SelectAssetForCurrentInstallation(_updateInfo, _installEnvironment);

            if (result.UpdateAvailable)
            {
                HeaderTitle = LocalizationService.Instance.GetString("UpdateWindow_Header_NewVersion");
                StatusText = LocalizationService.Instance.GetString("UpdateWindow_Status_UpdateAvailable");
            }
            else
            {
                HeaderTitle = LocalizationService.Instance.GetString("UpdateWindow_Header_UpToDate");
                StatusText = LocalizationService.Instance.GetString("UpdateWindow_Status_UpToDate");
            }
        }

        private static string ResolveLocalizedChangelog(UpdateInfo updateInfo)
        {
            if (updateInfo.Changelog == null || updateInfo.Changelog.Count == 0)
            {
                return string.Empty;
            }

            string preferredKey = LocalizationService.Instance.CurrentLanguage == AppLanguage.English
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

        private async void BtnDownload_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedAsset == null)
                return;

            _downloadCts?.Cancel();
            _downloadCts?.Dispose();
            _downloadCts = new CancellationTokenSource();

            _isDownloading = true;
            DownloadProgressVisible = true;
            DownloadProgressPercent = 0;
            DownloadProgressText = LocalizationService.Instance.GetString("UpdateWindow_Download_Status_Starting");
            OnPropertyChanged(nameof(CanDownload));
            OnPropertyChanged(nameof(CanSelectAsset));
            OnPropertyChanged(nameof(CanSkipVersion));
            OnPropertyChanged(nameof(CanOpenDownloadedFolder));
            OnPropertyChanged(nameof(CanInstallDownloaded));

            try
            {
                var progress = new Progress<DownloadProgress>(p =>
                {
                    double percent = p.ProgressPercentage;
                    if (double.IsNaN(percent) || double.IsInfinity(percent)) percent = 0;
                    DownloadProgressPercent = percent;
                    DownloadProgressText = string.Format(
                        CultureInfo.CurrentUICulture,
                        LocalizationService.Instance.GetString("UpdateWindow_Download_Status_Progress_Format"),
                        FormatBytes(p.BytesReceived),
                        p.TotalBytes > 0 ? FormatBytes(p.TotalBytes) : "-");
                });

                string path = await UpdateService.Instance.DownloadUpdateAsync(SelectedAsset, progress, _downloadCts.Token).ConfigureAwait(true);
                _downloadedPath = path;
                DownloadProgressPercent = 100;
                DownloadProgressText = IsInstallerAsset(SelectedAsset)
                    ? LocalizationService.Instance.GetString("UpdateWindow_Download_Status_Done_Installer")
                    : LocalizationService.Instance.GetString("UpdateWindow_Download_Status_Done_Zip");
            }
            catch (OperationCanceledException)
            {
                DownloadProgressText = LocalizationService.Instance.GetString("UpdateWindow_Download_Status_Canceled");
            }
            catch (Exception ex)
            {
                DownloadProgressText = string.Format(CultureInfo.CurrentUICulture, LocalizationService.Instance.GetString("UpdateWindow_Download_Status_Failed_Format"), ex.Message);
            }
            finally
            {
                _isDownloading = false;
                OnPropertyChanged(nameof(CanDownload));
                OnPropertyChanged(nameof(CanSelectAsset));
                OnPropertyChanged(nameof(CanSkipVersion));
                OnPropertyChanged(nameof(CanOpenDownloadedFolder));
                OnPropertyChanged(nameof(CanInstallDownloaded));
            }
        }

        private void BtnInstall_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedAsset == null || !IsInstallerAsset(SelectedAsset))
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
            catch
            {
            }
        }

        private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_downloadedPath))
                return;

            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_downloadedPath}\"")
                {
                    UseShellExecute = true
                });
            }
            catch
            {
            }
        }

        private void BtnOpenRelease_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://github.com/Jerry-Z07/WindBoard/releases/latest")
                {
                    UseShellExecute = true
                });
            }
            catch
            {
            }
        }

        private void BtnSkipVersion_Click(object sender, RoutedEventArgs e)
        {
            if (_updateInfo == null)
                return;

            try { SettingsService.Instance.SetSkippedUpdateVersion(_updateInfo.Version); } catch { }
            Close();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        private static bool IsInstallerAsset(UpdateAsset asset)
        {
            return string.Equals(asset.Runtime?.Trim(), "installer", StringComparison.OrdinalIgnoreCase);
        }

        private static UpdateAsset? SelectAssetForCurrentInstallation(UpdateInfo updateInfo, InstallEnvironment? env)
        {
            if (updateInfo.Assets == null || updateInfo.Assets.Count == 0)
            {
                return null;
            }

            string arch = UpdateService.GetCurrentArchitecture();
            System.Collections.Generic.IEnumerable<UpdateAsset> candidates = updateInfo.Assets
                .Where(a => a != null
                    && !string.IsNullOrWhiteSpace(a.DownloadUrl)
                    && string.Equals(a.Arch?.Trim(), arch, StringComparison.OrdinalIgnoreCase));

            if (!candidates.Any())
            {
                candidates = updateInfo.Assets.Where(a => a != null && !string.IsNullOrWhiteSpace(a.DownloadUrl));
            }

            string? runtimeWanted = null;
            if (env != null)
            {
                if (env.InstallMode == InstallMode.InstallerPerMachine)
                {
                    runtimeWanted = "installer";
                }
                else if (env.InstallMode == InstallMode.Portable)
                {
                    runtimeWanted = env.DeploymentRuntime switch
                    {
                        DeploymentRuntime.FrameworkDependent => "framework-dependent",
                        DeploymentRuntime.SelfContained => "self-contained",
                        _ => "self-contained"
                    };
                }
            }

            if (!string.IsNullOrWhiteSpace(runtimeWanted))
            {
                UpdateAsset? match = candidates.FirstOrDefault(a => string.Equals(a.Runtime?.Trim(), runtimeWanted, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    return match;
                }
            }

            UpdateAsset? installer = candidates.FirstOrDefault(IsInstallerAsset);
            if (installer != null)
            {
                return installer;
            }

            UpdateAsset? selfContained = candidates.FirstOrDefault(a => string.Equals(a.Runtime?.Trim(), "self-contained", StringComparison.OrdinalIgnoreCase));
            if (selfContained != null)
            {
                return selfContained;
            }

            UpdateAsset? frameworkDependent = candidates.FirstOrDefault(a => string.Equals(a.Runtime?.Trim(), "framework-dependent", StringComparison.OrdinalIgnoreCase));
            return frameworkDependent ?? candidates.FirstOrDefault();
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
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
