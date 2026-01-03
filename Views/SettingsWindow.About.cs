using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using WindBoard.Services;

namespace WindBoard
{
    public partial class SettingsWindow
    {
        public string AppVersion => AppVersionInfo.Version;

        public bool AutoCheckUpdatesEnabled
        {
            get => _autoCheckUpdatesEnabled;
            set
            {
                if (_autoCheckUpdatesEnabled != value)
                {
                    _autoCheckUpdatesEnabled = value;
                    OnPropertyChanged();
                    try { SettingsService.Instance.SetAutoCheckUpdatesEnabled(value); } catch { }
                }
            }
        }

        private void BtnCheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var win = new UpdateWindow
                {
                    Owner = this
                };
                win.ShowDialog();
            }
            catch (Exception ex)
            {
                try
                {
                    string message = string.Format(
                        System.Globalization.CultureInfo.CurrentUICulture,
                        LocalizationService.Instance.GetString("Update_OpenWindowFailed_Format"),
                        ex.Message);

                    MessageBox.Show(this, message, LocalizationService.Instance.GetString("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                }
                catch
                {
                }

                Debug.WriteLine($"[Settings] Failed to open UpdateWindow: {ex}");
            }
        }

        private void BtnOpenGitHub_Click(object sender, RoutedEventArgs e)
        {
            TryOpenExternal("https://github.com/Jerry-Z07/WindBoard");
        }

        private static void TryOpenExternal(string pathOrUrl)
        {
            if (string.IsNullOrWhiteSpace(pathOrUrl))
                return;

            try
            {
                Process.Start(new ProcessStartInfo(pathOrUrl) { UseShellExecute = true });
            }
            catch (Win32Exception ex)
            {
                Debug.WriteLine($"[Settings] Failed to open external resource '{pathOrUrl}': {ex}");
            }
            catch (ObjectDisposedException ex)
            {
                Debug.WriteLine($"[Settings] Failed to open external resource '{pathOrUrl}': {ex}");
            }
            catch (InvalidOperationException ex)
            {
                Debug.WriteLine($"[Settings] Failed to open external resource '{pathOrUrl}': {ex}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Settings] Unexpected failure when opening external resource '{pathOrUrl}': {ex}");
            }
        }
    }
}
