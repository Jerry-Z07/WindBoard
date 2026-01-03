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
