using WindBoard.CrashReporter;

namespace WindBoard.Tests.Errors;

public sealed class CrashReporterArgsTests
{
    // CA1861：常量数组提为 static readonly，避免重复分配。
    private static readonly string[] MissingValueArgs = { "--report" };

    [Fact]
    public void Parse_WithKnownArgs_ParsesValues_AndIgnoresUnknown()
    {
        string[] args =
        {
            "--report", @"C:\Temp\crash 1.txt",
            "--logs-dir", @"C:\Temp\Logs",
            "--source", "WinUIUnhandledException",
            "--unknown", "x",
        };

        CrashReporterArgs parsed = CrashReporterArgs.Parse(args);

        Assert.Equal(@"C:\Temp\crash 1.txt", parsed.ReportPath);
        Assert.Equal(@"C:\Temp\Logs", parsed.LogsDirectory);
        Assert.Equal("WinUIUnhandledException", parsed.Source);
    }

    [Fact]
    public void Parse_WithMissingValue_DoesNotThrow_AndKeepsDefaults()
    {
        CrashReporterArgs parsed = CrashReporterArgs.Parse(MissingValueArgs);

        Assert.NotNull(parsed);
        Assert.Equal(string.Empty, parsed.ReportPath);
        Assert.Equal(string.Empty, parsed.LogsDirectory);
        Assert.Equal(string.Empty, parsed.Source);
    }
}

