using WindBoard.CrashReporter;

namespace WindBoard.Tests.Errors;

public sealed class CrashReporterArgsTests
{
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
        CrashReporterArgs parsed = CrashReporterArgs.Parse(new[] { "--report" });

        Assert.NotNull(parsed);
        Assert.Equal(string.Empty, parsed.ReportPath);
        Assert.Equal(string.Empty, parsed.LogsDirectory);
        Assert.Equal(string.Empty, parsed.Source);
    }
}

