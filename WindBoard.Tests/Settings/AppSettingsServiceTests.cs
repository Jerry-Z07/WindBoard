using System;
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

            await InvokeTaskMethodAsync(service, "ExportToFileAsync", exportPath, CancellationToken.None);

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
        try
        {
            string defaultSettingsPath = Path.Combine(root, "data", "settings.json");
            string importPath = Path.Combine(root, "incoming", "settings.json");
            AppSettingsService service = CreateService(defaultSettingsPath);

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

            await InvokeTaskMethodAsync(service, "ImportFromFileAsync", importPath, CancellationToken.None);

            Assert.Equal(AppLanguagePreferenceParser.ChineseValue, service.GetLanguagePreference());
            Assert.Equal(UpdateCheckInterval.Weekly, service.GetUpdateCheckInterval());
            Assert.Equal(365, service.GetLoggingSettingsSnapshot().RetentionDays);

            AppSettings persisted = new AppSettingsStore(defaultSettingsPath).LoadOrDefault();
            Assert.Equal(AppLanguagePreferenceParser.ChineseValue, persisted.General.LanguagePreference);
            Assert.Equal(UpdateCheckIntervalParser.WeeklyValue, persisted.General.Updates.AutoCheckInterval);
            Assert.Equal(365, persisted.Diagnostics.Logging.RetentionDays);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ResetToDefaultsAsync_RestoresDefaultSettingsAndPersists()
    {
        string root = CreateTempDirectory();
        try
        {
            string defaultSettingsPath = Path.Combine(root, "data", "settings.json");
            AppSettingsService service = CreateService(defaultSettingsPath);

            service.SetLanguagePreference(AppLanguagePreferenceParser.EnglishValue);
            service.SetStartupWindowMode(StartupWindowMode.FullScreen);
            service.SetEnterScreenAnnotationWhenMinimized(false);
            await service.SaveAsync();

            await InvokeTaskMethodAsync(service, "ResetToDefaultsAsync", CancellationToken.None);

            Assert.Equal(AppLanguagePreferenceParser.SystemValue, service.GetLanguagePreference());
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
                () => InvokeTaskMethodAsync(service, "ImportFromFileAsync", importPath, CancellationToken.None));

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
        ConstructorInfo? ctor = typeof(AppSettingsService).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(AppSettingsStore)],
            modifiers: null);

        Assert.NotNull(ctor);
        return Assert.IsType<AppSettingsService>(ctor.Invoke([store]));
    }

    private static async Task InvokeTaskMethodAsync(object target, string methodName, params object[] args)
    {
        MethodInfo? method = target.GetType().GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.NotNull(method);

        try
        {
            var task = Assert.IsAssignableFrom<Task>(method.Invoke(target, args));
            await task.ConfigureAwait(false);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
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
}
