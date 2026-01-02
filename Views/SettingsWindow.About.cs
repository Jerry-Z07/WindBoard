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
            try
            {
                Process.Start(new ProcessStartInfo(pathOrUrl) { UseShellExecute = true });
            }
            catch
            {
                // ignore
            }
        }
    }
}

