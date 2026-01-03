using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using WindBoard.ViewModels;

namespace WindBoard
{
    public partial class UpdateWindow : Window
    {
        private readonly UpdateViewModel _viewModel;

        public UpdateWindow()
        {
            InitializeComponent();

            _viewModel = new UpdateViewModel();
            _viewModel.RequestClose += ViewModel_RequestClose;
            DataContext = _viewModel;

            Loaded += UpdateWindow_Loaded;
            Closed += UpdateWindow_Closed;
        }

        private async void UpdateWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await RunSafeAsync(() => _viewModel.InitializeAsync()).ConfigureAwait(true);
        }

        private void UpdateWindow_Closed(object? sender, EventArgs e)
        {
            _viewModel.RequestClose -= ViewModel_RequestClose;
            _viewModel.Dispose();
        }

        private void ViewModel_RequestClose(object? sender, EventArgs e) => Close();

        private async void BtnDownload_Click(object sender, RoutedEventArgs e)
        {
            await RunSafeAsync(() => _viewModel.DownloadAsync()).ConfigureAwait(true);
        }

        private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.OpenDownloadedFolder();
        }

        private void BtnOpenRelease_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.OpenReleasePage();
        }

        private void BtnInstall_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.InstallDownloaded();
        }

        private void BtnSkipVersion_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.SkipThisVersion();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        private static async Task RunSafeAsync(Func<Task> action)
        {
            try
            {
                await action().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UpdateWindow] Unexpected failure: {ex}");
            }
        }
    }
}
