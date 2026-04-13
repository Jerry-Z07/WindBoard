using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WindBoard.Settings;
using Xunit;

namespace WindBoard.Tests.Settings;

public sealed class AppSettingsServiceTests
{
    [Fact]
    public async Task ExportToFileAsync_WritesCurrentSettingsSnapshotToSelectedJson()
    {
        string root = CreateTempDirectory();
        try
        {
            string defaultSettingsPath = Path.Combine(root, "data", "settings.json");
            string exportPath = Path.Combine(root, "backup", "windboard-settings.json");
            AppSettingsService service = CreateService(defaultSettingsPath);

            service.Update(settings =>
            {
                settings.General.LanguagePreference = AppLanguagePreferenceParser.EnglishValue;
                settings.General.Updates.AutoCheckInterval = UpdateCheckIntervalParser.MonthlyValue;
                settings.General.Updates.LastNotifiedVersion = "  v2.5.0  ";
            });

            await service.ExportToFileAsync(exportPath, CancellationToken.None);

            var exportedStore = new AppSettingsStore(exportPath);
            AppSettings exported = exportedStore.LoadOrDefault();
            Assert.Equal(AppLanguagePreferenceParser.EnglishValue, exported.General.LanguagePreference);
            Assert.Equal(UpdateCheckIntervalParser.MonthlyValue, exported.General.Updates.AutoCheckInterval);
            Assert.Equal("v2.5.0", exported.General.Updates.LastNotifiedVersion);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ImportFromFileAsync_ReplacesCurrentSettingsAndPersistsNormalizedValues()
    {
        string root = CreateTempDirectory();
        CultureSnapshot cultureSnapshot = CaptureCultureSnapshot();
        try
        {
            string defaultSettingsPath = Path.Combine(root, "data", "settings.json");
            string importPath = Path.Combine(root, "incoming", "settings.json");
            AppSettingsService service = CreateService(defaultSettingsPath);
            AppLanguageService.Apply(AppLanguagePreferenceParser.EnglishValue);

            service.SetLanguagePreference(AppLanguagePreferenceParser.EnglishValue);
            service.SetUpdateCheckInterval(UpdateCheckInterval.Monthly);
            await service.SaveAsync();

            Directory.CreateDirectory(Path.GetDirectoryName(importPath)!);
            await File.WriteAllTextAsync(
                importPath,
                """
                {
                  "general": {
                    "languagePreference": "  zh_CN  ",
                    "updates": {
                      "autoCheckInterval": "invalid"
                    }
                  },
                  "diagnostics": {
                    "logging": {
                      "retentionDays": 999
                    }
                  }
                }
                """);

            await service.ImportFromFileAsync(importPath, CancellationToken.None);

            Assert.Equal(AppLanguagePreferenceParser.ChineseValue, service.GetLanguagePreference());
            Assert.Equal(AppLanguagePreferenceParser.ChineseValue, CultureInfo.DefaultThreadCurrentCulture?.Name);
            Assert.Equal(AppLanguagePreferenceParser.ChineseValue, CultureInfo.DefaultThreadCurrentUICulture?.Name);
            Assert.Equal(UpdateCheckInterval.Weekly, service.GetUpdateCheckInterval());
            Assert.Equal(365, service.GetLoggingSettingsSnapshot().RetentionDays);

            AppSettings persisted = new AppSettingsStore(defaultSettingsPath).LoadOrDefault();
            Assert.Equal(AppLanguagePreferenceParser.ChineseValue, persisted.General.LanguagePreference);
            Assert.Equal(UpdateCheckIntervalParser.WeeklyValue, persisted.General.Updates.AutoCheckInterval);
            Assert.Equal(365, persisted.Diagnostics.Logging.RetentionDays);
        }
        finally
        {
            RestoreCultureSnapshot(cultureSnapshot);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ResetToDefaultsAsync_RestoresDefaultSettingsAndPersists()
    {
        string root = CreateTempDirectory();
        CultureSnapshot cultureSnapshot = CaptureCultureSnapshot();
        try
        {
            string defaultSettingsPath = Path.Combine(root, "data", "settings.json");
            AppSettingsService service = CreateService(defaultSettingsPath);
            string expectedSystemUiCulture = CultureInfo.CurrentUICulture.Name;
            string overriddenLanguage = string.Equals(expectedSystemUiCulture, AppLanguagePreferenceParser.ChineseValue, StringComparison.OrdinalIgnoreCase)
                ? AppLanguagePreferenceParser.EnglishValue
                : AppLanguagePreferenceParser.ChineseValue;

            service.SetLanguagePreference(overriddenLanguage);
            AppLanguageService.Apply(overriddenLanguage);
            service.SetStartupWindowMode(StartupWindowMode.FullScreen);
            service.SetEnterScreenAnnotationWhenMinimized(false);
            await service.SaveAsync();

            await service.ResetToDefaultsAsync(CancellationToken.None);

            Assert.Equal(AppLanguagePreferenceParser.SystemValue, service.GetLanguagePreference());
            Assert.Null(CultureInfo.DefaultThreadCurrentCulture);
            Assert.Null(CultureInfo.DefaultThreadCurrentUICulture);
            Assert.Equal(StartupWindowMode.Windowed, service.GetStartupWindowMode());
            Assert.True(service.GetEnterScreenAnnotationWhenMinimized());
            Assert.Equal(UpdateCheckInterval.Weekly, service.GetUpdateCheckInterval());

            AppSettings persisted = new AppSettingsStore(defaultSettingsPath).LoadOrDefault();
            Assert.Equal(AppLanguagePreferenceParser.SystemValue, persisted.General.LanguagePreference);
            Assert.Equal(StartupWindowModeParser.WindowedValue, persisted.General.StartupWindowMode);
            Assert.True(persisted.General.EnterScreenAnnotationWhenMinimized);
            Assert.Equal(UpdateCheckIntervalParser.WeeklyValue, persisted.General.Updates.AutoCheckInterval);
        }
        finally
        {
            RestoreCultureSnapshot(cultureSnapshot);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ImportFromFileAsync_DoesNotPostBackToCapturedSynchronizationContext()
    {
        string root = CreateTempDirectory();
        CultureSnapshot cultureSnapshot = CaptureCultureSnapshot();
        SynchronizationContext? originalContext = SynchronizationContext.Current;
        var trackingContext = new TrackingSynchronizationContext();
        try
        {
            string defaultSettingsPath = Path.Combine(root, "data", "settings.json");
            string importPath = Path.Combine(root, "incoming", "settings.json");
            AppSettingsService service = CreateService(defaultSettingsPath);

            Directory.CreateDirectory(Path.GetDirectoryName(importPath)!);
            await File.WriteAllTextAsync(importPath, "{ \"general\": { \"languagePreference\": \"en-US\" } }");

            int postCount = await RunOnTrackingSynchronizationContextAsync(
                trackingContext,
                () => service.ImportFromFileAsync(importPath, CancellationToken.None));

            Assert.Equal(0, postCount);
        }
        finally
        {
            RestoreCultureSnapshot(cultureSnapshot);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ResetToDefaultsAsync_DoesNotPostBackToCapturedSynchronizationContext()
    {
        string root = CreateTempDirectory();
        CultureSnapshot cultureSnapshot = CaptureCultureSnapshot();
        SynchronizationContext? originalContext = SynchronizationContext.Current;
        var trackingContext = new TrackingSynchronizationContext();
        try
        {
            string defaultSettingsPath = Path.Combine(root, "data", "settings.json");
            AppSettingsService service = CreateService(defaultSettingsPath);
            SemaphoreSlim ioGate = GetIoGate(service);
            Assert.True(ioGate.Wait(0));

            int postCount = await RunResetOnTrackingSynchronizationContextAsync(service, ioGate, trackingContext);

            Assert.Equal(0, postCount);
        }
        finally
        {
            RestoreCultureSnapshot(cultureSnapshot);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ImportFromFileAsync_ThrowsOnInvalidJson_AndKeepsCurrentSettings()
    {
        string root = CreateTempDirectory();
        try
        {
            string defaultSettingsPath = Path.Combine(root, "data", "settings.json");
            string importPath = Path.Combine(root, "incoming", "invalid-settings.json");
            AppSettingsService service = CreateService(defaultSettingsPath);

            service.SetLanguagePreference(AppLanguagePreferenceParser.EnglishValue);

            Directory.CreateDirectory(Path.GetDirectoryName(importPath)!);
            await File.WriteAllTextAsync(importPath, "{ invalid json ");

            JsonException ex = await Assert.ThrowsAsync<JsonException>(
                () => service.ImportFromFileAsync(importPath, CancellationToken.None));

            Assert.NotNull(ex);
            Assert.Equal(AppLanguagePreferenceParser.EnglishValue, service.GetLanguagePreference());
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static AppSettingsService CreateService(string settingsPath)
    {
        var store = new AppSettingsStore(settingsPath);
        return new AppSettingsService(store);
    }

    private static CultureSnapshot CaptureCultureSnapshot()
    {
        return new CultureSnapshot(
            CultureInfo.CurrentCulture,
            CultureInfo.CurrentUICulture,
            CultureInfo.DefaultThreadCurrentCulture,
            CultureInfo.DefaultThreadCurrentUICulture);
    }

    private static void RestoreCultureSnapshot(CultureSnapshot snapshot)
    {
        CultureInfo.CurrentCulture = snapshot.CurrentCulture;
        CultureInfo.CurrentUICulture = snapshot.CurrentUICulture;
        CultureInfo.DefaultThreadCurrentCulture = snapshot.DefaultThreadCurrentCulture;
        CultureInfo.DefaultThreadCurrentUICulture = snapshot.DefaultThreadCurrentUICulture;
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "WindBoard.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private sealed record CultureSnapshot(
        CultureInfo CurrentCulture,
        CultureInfo CurrentUICulture,
        CultureInfo? DefaultThreadCurrentCulture,
        CultureInfo? DefaultThreadCurrentUICulture);

    private static SemaphoreSlim GetIoGate(AppSettingsService service)
    {
        FieldInfo? ioGateField = typeof(AppSettingsService).GetField("_ioGate", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(ioGateField);
        return Assert.IsType<SemaphoreSlim>(ioGateField.GetValue(service));
    }

    private static Task<int> RunResetOnTrackingSynchronizationContextAsync(
        AppSettingsService service,
        SemaphoreSlim ioGate,
        TrackingSynchronizationContext trackingContext)
    {
        return RunOnTrackingSynchronizationContextAsync(
            trackingContext,
            async () =>
            {
                Task resetTask = service.ResetToDefaultsAsync(CancellationToken.None);
                await Task.Delay(20).ConfigureAwait(false);
                ioGate.Release();
                await resetTask.ConfigureAwait(false);
            });
    }

    private static async Task<int> RunOnTrackingSynchronizationContextAsync(
        TrackingSynchronizationContext trackingContext,
        Func<Task> action)
    {
        SynchronizationContext? originalContext = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(trackingContext);
            await action().ConfigureAwait(false);
            return trackingContext.PostCount;
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    private sealed class TrackingSynchronizationContext : SynchronizationContext
    {
        private int _postCount;

        internal int PostCount => Volatile.Read(ref _postCount);

        public override void Post(SendOrPostCallback d, object? state)
        {
            Interlocked.Increment(ref _postCount);
            ThreadPool.QueueUserWorkItem(static callbackState =>
            {
                var (callback, callbackArg) = ((SendOrPostCallback Callback, object? State))callbackState!;
                callback(callbackArg);
            }, (d, state));
        }
    }
}
