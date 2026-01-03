using System;
using System.Threading.Tasks;
using System.Windows.Threading;
using WindBoard.Models.Update;
using WindBoard.Services;

namespace WindBoard
{
    public partial class MainWindow
    {
        private static readonly TimeSpan UpdateCheckMinInterval = TimeSpan.FromHours(12);

        private void TryCheckUpdatesOnStartup()
        {
            bool enabled;
            try { enabled = SettingsService.Instance.GetAutoCheckUpdatesEnabled(); }
            catch { enabled = true; }

            if (!enabled)
            {
                return;
            }

            DateTime? lastCheckUtc = null;
            try { lastCheckUtc = SettingsService.Instance.GetLastUpdateCheckTime()?.ToUniversalTime(); }
            catch { }

            if (lastCheckUtc.HasValue && (DateTime.UtcNow - lastCheckUtc.Value) < UpdateCheckMinInterval)
            {
                return;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    UpdateCheckResult result = await UpdateService.Instance.CheckForUpdatesAsync().ConfigureAwait(false);
                    try { SettingsService.Instance.SetLastUpdateCheckTime(DateTime.UtcNow); } catch { }

                    if (result.UpdateAvailable && result.LatestVersion != null)
                    {
                        Dispatcher.Invoke(() => UpdateService.Instance.ShowUpdateNotification(result.LatestVersion));
                    }
                }
                catch
                {
                    // Silent failure: startup auto-check should not disrupt the user.
                }
            });
        }
    }
}

