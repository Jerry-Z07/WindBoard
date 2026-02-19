using System;
using System.Collections.Generic;
using System.IO;
using WindBoard.Persistence;
using WindBoard.Updates;

namespace WindBoard.Tests.Persistence;

public sealed class AppDataPathsTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    public void Dispose()
    {
        foreach (string dir in _tempDirs)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    private string CreateTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"WindBoardTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    [Fact]
    public void ComputeSnapshot_Installer_UsesLocalAppData()
    {
        int ensureWritableCalls = 0;

        var install = new AppInstallProbeResult
        {
            Kind = AppInstallKind.Installer,
            Evidence = "registry",
            InstallDir = @"C:\Program Files\WindBoard",
        };

        AppDataPathsSnapshot snapshot = AppDataPaths.ComputeSnapshot(
            install,
            appBaseDirectory: @"C:\Program Files\WindBoard\",
            localAppDataDirectory: @"C:\Users\test\AppData\Local\",
            tryEnsureWritable: _ =>
            {
                ensureWritableCalls++;
                return (ok: true, errorMessage: null);
            });

        Assert.Equal(0, ensureWritableCalls);
        Assert.Equal(AppInstallKind.Installer, snapshot.InstallKind);
        Assert.Equal(Path.Combine(@"C:\Users\test\AppData\Local", "WindBoard"), snapshot.RootDirectory);
        Assert.False(snapshot.UsingPortableDataDirectory);
        Assert.Equal(Path.Combine(snapshot.RootDirectory, "settings.json"), snapshot.SettingsFilePath);
        Assert.Equal(Path.Combine(snapshot.RootDirectory, "Logs"), snapshot.LogsDirectory);
        Assert.Equal(Path.Combine(snapshot.RootDirectory, "downloads"), snapshot.DownloadsDirectory);
    }

    [Fact]
    public void ComputeSnapshot_Portable_Writable_UsesBaseData()
    {
        int ensureWritableCalls = 0;

        var install = new AppInstallProbeResult
        {
            Kind = AppInstallKind.Portable,
            Evidence = "fallback",
            InstallDir = @"D:\WindBoard",
        };

        AppDataPathsSnapshot snapshot = AppDataPaths.ComputeSnapshot(
            install,
            appBaseDirectory: @"D:\WindBoard\",
            localAppDataDirectory: @"C:\Users\test\AppData\Local\",
            tryEnsureWritable: dir =>
            {
                ensureWritableCalls++;
                Assert.Equal(@"D:\WindBoard\data", dir);
                return (ok: true, errorMessage: null);
            });

        Assert.Equal(1, ensureWritableCalls);
        Assert.Equal(Path.Combine(@"D:\WindBoard", "data"), snapshot.RootDirectory);
        Assert.True(snapshot.UsingPortableDataDirectory);
        Assert.True(snapshot.PortableDataDirectoryWritable);
    }

    [Fact]
    public void ComputeSnapshot_Portable_NotWritable_FallsBackToLocalAppData()
    {
        int ensureWritableCalls = 0;

        var install = new AppInstallProbeResult
        {
            Kind = AppInstallKind.Portable,
            Evidence = "fallback",
            InstallDir = @"D:\WindBoard",
        };

        AppDataPathsSnapshot snapshot = AppDataPaths.ComputeSnapshot(
            install,
            appBaseDirectory: @"D:\WindBoard\",
            localAppDataDirectory: @"C:\Users\test\AppData\Local\",
            tryEnsureWritable: dir =>
            {
                ensureWritableCalls++;
                Assert.Equal(@"D:\WindBoard\data", dir);
                return (ok: false, errorMessage: "AccessDenied");
            });

        Assert.Equal(1, ensureWritableCalls);
        Assert.Equal(Path.Combine(@"C:\Users\test\AppData\Local", "WindBoard"), snapshot.RootDirectory);
        Assert.False(snapshot.UsingPortableDataDirectory);
        Assert.False(snapshot.PortableDataDirectoryWritable);
        Assert.Equal("AccessDenied", snapshot.PortableDataDirectoryWriteTestError);
    }

    [Fact]
    public void TryMigrateSettingsFileIfNeeded_WhenDestinationMissing_CopiesFile()
    {
        string root = CreateTempDir();
        string source = Path.Combine(root, "old", "settings.json");
        string dest = Path.Combine(root, "data", "settings.json");

        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        File.WriteAllText(source, "{\"a\":1}");

        SettingsMigrationResult result = AppDataPaths.TryMigrateSettingsFileIfNeeded(source, dest);

        Assert.True(result.Migrated);
        Assert.True(File.Exists(dest));
        Assert.Equal("{\"a\":1}", File.ReadAllText(dest));
        Assert.True(File.Exists(source));
    }

    [Fact]
    public void TryMigrateSettingsFileIfNeeded_WhenDestinationExists_DoesNotOverwrite()
    {
        string root = CreateTempDir();
        string source = Path.Combine(root, "old", "settings.json");
        string dest = Path.Combine(root, "data", "settings.json");

        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        File.WriteAllText(source, "{\"old\":1}");
        File.WriteAllText(dest, "{\"new\":2}");

        SettingsMigrationResult result = AppDataPaths.TryMigrateSettingsFileIfNeeded(source, dest);

        Assert.False(result.Migrated);
        Assert.Equal("{\"new\":2}", File.ReadAllText(dest));
    }
}

