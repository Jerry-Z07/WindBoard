using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using WindBoard.Settings;
using WindBoard.Reminders;
using WindBoard.Updates;
using Xunit;

namespace WindBoard.Tests.Updates;

public sealed class AppUpdateServiceTests
{
    [Fact]
    public void TryShowAvailableUpdateReminder_ReturnsFalse_WhenReminderWasNotShown()
    {
        Window window = (Window)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Window));
        var message = new AppReminderMessage { Title = "title", Body = "body" };

        bool shown = AppUpdateService.TryShowAvailableUpdateReminder(
            window,
            latestVersion: "2.0.0",
            lastNotifiedVersion: string.Empty,
            message,
            static (_, _, _) => false);

        Assert.False(shown);
    }

    [Fact]
    public void TryShowAvailableUpdateReminder_ReturnsFalse_WhenVersionWasAlreadyNotified()
    {
        Window window = (Window)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Window));
        var message = new AppReminderMessage { Title = "title", Body = "body" };

        bool shown = AppUpdateService.TryShowAvailableUpdateReminder(
            window,
            latestVersion: "2.0.0",
            lastNotifiedVersion: "2.0.0",
            message,
            static (_, _, _) => true);

        Assert.False(shown);
    }

    [Fact]
    public async Task TryAutoCheckAndRemindAsync_DoesNotPersistVersion_WhenReminderWasNotShown()
    {
        Window window = (Window)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Window));
        string? persistedVersion = null;
        var service = new AppUpdateService(
            checkForUpdatesOverride: static (_, _) => Task.FromResult(CreateUpdateAvailableResult("2.0.0")),
            getUpdatePreferencesOverride: static () => CreatePrefs(lastNotifiedVersion: string.Empty),
            setUpdateLastNotifiedVersionOverride: version => persistedVersion = version,
            remindOncePerSignatureOverride: static (_, _, _) => false);

        await service.TryAutoCheckAndRemindAsync(window, CancellationToken.None);

        Assert.Null(persistedVersion);
    }

    [Fact]
    public async Task TryAutoCheckAndRemindAsync_UsesFreshPreferencesBeforeShowingReminder()
    {
        Window window = (Window)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Window));
        string? persistedVersion = null;
        int prefsReadCount = 0;
        int remindCallCount = 0;
        var service = new AppUpdateService(
            checkForUpdatesOverride: static (_, _) => Task.FromResult(CreateUpdateAvailableResult("2.0.0")),
            getUpdatePreferencesOverride: () =>
            {
                prefsReadCount++;
                return prefsReadCount == 1
                    ? CreatePrefs(lastNotifiedVersion: string.Empty)
                    : CreatePrefs(lastNotifiedVersion: "2.0.0");
            },
            setUpdateLastNotifiedVersionOverride: version => persistedVersion = version,
            remindOncePerSignatureOverride: (_, _, _) =>
            {
                remindCallCount++;
                return true;
            });

        await service.TryAutoCheckAndRemindAsync(window, CancellationToken.None);

        Assert.Null(persistedVersion);
        Assert.Equal(0, remindCallCount);
    }

    private static AppUpdateCheckResult CreateUpdateAvailableResult(string version)
    {
        return new AppUpdateCheckResult
        {
            State = AppUpdateCheckState.UpdateAvailable,
            Latest = new LatestReleaseInfo { Version = version },
        };
    }

    private static UpdatePreferencesSnapshot CreatePrefs(string lastNotifiedVersion)
    {
        return new UpdatePreferencesSnapshot
        {
            AutoCheckInterval = UpdateCheckInterval.Weekly,
            LastCheckUtc = null,
            LastNotifiedVersion = lastNotifiedVersion,
        };
    }
}
