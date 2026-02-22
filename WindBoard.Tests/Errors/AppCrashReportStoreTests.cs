using System;
using System.IO;
using WindBoard.Errors;

namespace WindBoard.Tests.Errors;

public sealed class AppCrashReportStoreTests : IDisposable
{
    private readonly string _tempRoot;

    public AppCrashReportStoreTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"WindBoardTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true); } catch { }
    }

    [Fact]
    public void TryWriteCrashReport_WritesFile_AndContainsKeyFields()
    {
        var ex = new InvalidOperationException("boom");

        bool ok = AppCrashReportStore.TryWriteCrashReport(
            AppCrashSource.WinUIUnhandledException,
            ex,
            exceptionObject: null,
            isTerminating: null,
            logDirectoryOverride: _tempRoot,
            out AppCrashReport report,
            out Exception? error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.False(string.IsNullOrWhiteSpace(report.ReportFilePath));
        Assert.True(File.Exists(report.ReportFilePath));

        string text = File.ReadAllText(report.ReportFilePath);
        Assert.Contains("AppVersion:", text, StringComparison.Ordinal);
        Assert.Contains("Source:", text, StringComparison.Ordinal);
        Assert.Contains("ExceptionType:", text, StringComparison.Ordinal);
        Assert.Contains("InvalidOperationException", text, StringComparison.Ordinal);
    }
}

